# 🏗️ 09 — معمارية النظام الهجين (Hybrid Architecture)

> **الملف اللي قبله:** [`08-HYBRID-OVERVIEW.md`](./08-HYBRID-OVERVIEW.md) — ليه هجين، والاقتصاد، والنوافذ، وقاعدة التوجيه.
> **الملف ده:** إزاي نبنيه فعلاً — الطبقات، والمكوّنات، والمخطط، والبوابات.
> **الملف اللي بعده:** [`10-HYBRID-IMPLEMENTATION.md`](./10-HYBRID-IMPLEMENTATION.md) — كود شغّال + قوالب + خطة هجرة.

---

## 0. الفكرة المعمارية في سطر واحد

```
دماغ واحدة  +  بوقين
```

النظام الهجين **مش نظامين**. لو عملته نظامين هتعيش في جحيم: عميلين مختلفين لنفس الشخص، تاريخ محادثة مقطوع، عميل يستلم رسالة مرتين، و opt-out على قناة مش بيمشي على التانية.

النظام الهجين الصح =

| الطبقة | عددها | ليه |
|---|---|---|
| قاعدة بيانات | **واحدة** | العميل كائن واحد. `opt_out` مرة واحدة يعني على القناتين. |
| منطق أعمال (segmentation، بوت، أوردرات) | **واحد** | مش عايز أكتب البوت مرتين. |
| طوابير | **واحدة** بأولويات | التنسيق مركزي. |
| مُوجّه (Router) | **واحد** | هو اللي بياخد القرار: رسمي ولا غير رسمي. |
| **مزوّدين إرسال (Providers)** | **اتنين** | `official` + `unofficial` — بنفس الواجهة بالظبط. |

> ⚠️ **القاعدة الحديدية:** أي كود فوق طبقة الـ Provider **ممنوع** يعرف إحنا بنبعت من أنهي قناة. لو الـ `OrderBot` فيه `if (channel === 'official')` — يبقى التصميم غلط والصيانة هتقتلك.

---

## 1. خريطة الطبقات

```
                    ┌──────────────────────────────────────────┐
                    │  مصادر الأحداث الداخلة (Ingestion)       │
                    ├────────────────────┬─────────────────────┤
                    │ Meta Cloud API     │  Evolution / Baileys │
                    │ Webhook (رسمي)     │  Webhook (غير رسمي)  │
                    └─────────┬──────────┴──────────┬──────────┘
                              │                     │
                              ▼                     ▼
                    ┌──────────────────────────────────────────┐
                    │   🔄 EventNormalizer                     │
                    │   شكلين مختلفين → InboundEvent واحد      │
                    └────────────────────┬─────────────────────┘
                                         │
                                         ▼
   ┌────────────────────────────────────────────────────────────────────┐
   │  🧠 الدماغ (Core) — مش بتعرف حاجة عن القنوات                       │
   │                                                                    │
   │   WindowTracker      OrderBot        Segmentation     OptOutEngine  │
   │   (نوافذ العميل)     (05)            (01)             (01)          │
   └────────────────────────────────┬───────────────────────────────────┘
                                    │  SendIntent
                                    ▼
   ┌────────────────────────────────────────────────────────────────────┐
   │  🚦 ChannelRouter  +  GateChain (موسّعة)                            │
   │  بيقرّر: القناة؟ قالب ولا حر؟ يستنى؟ يرفض؟                          │
   └───────────────┬──────────────────────────────┬─────────────────────┘
                   │                              │
                   ▼                              ▼
   ┌───────────────────────────┐   ┌──────────────────────────────────┐
   │ 🏢 OfficialProvider       │   │ ⚡ UnofficialProvider             │
   │ Cloud API / BSP           │   │ Evolution API (multi-session)    │
   │ • قوالب معتمدة فقط        │   │ • رسائل حرة                      │
   │ • Tier + Freq Cap         │   │ • DelayEngine + Warmup + Spintax │
   │ • بتدفع لكل رسالة         │   │ • مجاني + قابل للحظر             │
   └─────────────┬─────────────┘   └───────────────┬──────────────────┘
                 └──────────────┬─────────────────┘
                                ▼
                    ┌──────────────────────────────┐
                    │ 📒 message_log + cost_ledger │
                    │  سجل واحد للقناتين            │
                    └──────────────────────────────┘
```

---

## 2. طبقة تجريد المزوّدين (Provider Abstraction Layer)

### 2.1 العقد (The Contract)

أي مزوّد لازم ينفّذ الواجهة دي بالظبط. مفيش استثناءات.

```javascript
// providers/base.js

/**
 * @typedef {Object} SendRequest
 * @property {string}  to            رقم E.164 بدون +  (مثال: 201012345678)
 * @property {'text'|'image'|'video'|'document'|'audio'} type
 * @property {string}  [body]        نص الرسالة (للرسائل الحرة)
 * @property {string}  [mediaUrl]
 * @property {Object}  [template]    { name, language, components } للرسمي
 * @property {string}  [replyTo]     wa_message_id للردّ على رسالة
 * @property {string}  idempotencyKey  🔑 إجباري — يمنع الإرسال المزدوج
 * @property {Object}  meta          { customerId, campaignId, intent }
 */

/**
 * @typedef {Object} SendResult
 * @property {boolean} ok
 * @property {string}  [providerMessageId]
 * @property {string}  channel        'official' | 'unofficial'
 * @property {number}  [estimatedCostUsd]
 * @property {string}  [errorCode]
 * @property {boolean} [retryable]
 * @property {number}  [retryAfterMs]
 * @property {boolean} [fatal]        متبعتش تاني لنفس العميل
 */

export class Provider {
  /** @returns {'official'|'unofficial'} */
  get channel() { throw new Error('not implemented'); }

  /** هل المزوّد ده يقدر ينفّذ الطلب ده دلوقتي؟ (بدون إرسال) */
  async can(req) { throw new Error('not implemented'); }

  /** @param {SendRequest} req @returns {Promise<SendResult>} */
  async send(req) { throw new Error('not implemented'); }

  /** صحة المزوّد — بيستخدمها الـ Router للتدهور (degradation) */
  async health() { throw new Error('not implemented'); }

  /** تطبيع حدث داخل → InboundEvent */
  normalizeInbound(rawPayload) { throw new Error('not implemented'); }
}
```

**ليه `idempotencyKey` إجباري؟**
في نظام هجين، أخطر خطأ هو إن رسالة تتبعت مرتين من قناتين مختلفتين (Router أعاد المحاولة بعد timeout غامض). الـ key بيخلّي الإرسال المزدوج مستحيل:

```javascript
// المفتاح لازم يكون حتمي (deterministic) من محتوى النية — مش عشوائي
import { createHash } from 'crypto';

export function idempotencyKey({ customerId, intent, campaignId, dayBucket }) {
  return createHash('sha256')
    .update(`${customerId}|${intent}|${campaignId ?? '-'}|${dayBucket}`)
    .digest('hex').slice(0, 32);
}
// dayBucket = تاريخ اليوم YYYY-MM-DD → نفس النية في نفس اليوم = نفس المفتاح
```

قبل أي `send()`، الـ Provider بيعمل `SET key NX EX 172800` على Redis. لو رجع `null` → الرسالة دي اتبعتت خلاص، **اسكت وارجع**.

### 2.2 المزوّد الرسمي

```javascript
// providers/official.js
import { Provider } from './base.js';

export class OfficialProvider extends Provider {
  constructor({ http, phoneNumberId, token, tierStore, freqCap, costBook, redis }) {
    super();
    this.http = http;              // axios instance على graph.facebook.com
    this.phoneNumberId = phoneNumberId;
    this.token = token;
    this.tierStore = tierStore;    // بيتتبع الحد اليومي الحالي
    this.freqCap = freqCap;        // بيتتبع marketing/عميل/24h
    this.costBook = costBook;      // جدول الأسعار
    this.redis = redis;
  }

  get channel() { return 'official'; }

  async can(req) {
    // 1) الرسمي ميقدرش يبعت رسالة حرة بره نافذة
    if (!req.template && !req.meta.windowOpen)
      return { ok: false, reason: 'رسالة حرة بره نافذة — الرسمي بيرفضها' };

    // 2) القالب لازم يكون معتمد
    if (req.template) {
      const t = await this.getTemplate(req.template.name);
      if (!t || t.status !== 'APPROVED')
        return { ok: false, reason: `القالب ${req.template.name} غير معتمد` };
    }

    // 3) الحد اليومي (Messaging Tier)
    const tier = await this.tierStore.current();     // {limit, usedToday}
    if (tier.usedToday >= tier.limit * 0.98)
      return { ok: false, reason: 'وصلنا حد الـ tier اليومي', retryTomorrow: true };

    // 4) 🔴 سقف تكرار التسويق — 131049
    if (req.template?.category === 'MARKETING') {
      const hit = await this.freqCap.check(req.to);
      if (!hit.allowed)
        return { ok: false, reason: 'سقف رسائل التسويق للعميل (131049)', code: '131049' };
    }
    return { ok: true };
  }

  async send(req) {
    // 🔒 الحماية من الإرسال المزدوج
    const fresh = await this.redis.set(`idem:${req.idempotencyKey}`, '1', 'NX', 'EX', 172800);
    if (!fresh) return { ok: true, channel: this.channel, deduped: true };

    const payload = req.template
      ? { messaging_product: 'whatsapp', to: req.to, type: 'template',
          template: req.template }
      : { messaging_product: 'whatsapp', to: req.to, type: req.type,
          [req.type]: req.type === 'text' ? { body: req.body, preview_url: false }
                                          : { link: req.mediaUrl, caption: req.body } };

    try {
      const { data } = await this.http.post(`/${this.phoneNumberId}/messages`, payload);
      await this.tierStore.increment();
      if (req.template?.category === 'MARKETING') await this.freqCap.record(req.to);

      return {
        ok: true, channel: 'official',
        providerMessageId: data.messages[0].id,
        // ⚠️ تقديري — الفاتورة الحقيقية بتتأكد على التسليم (delivered) في الـ webhook
        estimatedCostUsd: this.costBook.price(req.to, req.template?.category ?? 'SERVICE'),
      };
    } catch (e) {
      return this.mapError(e);
    }
  }

  mapError(e) {
    const code = String(e.response?.data?.error?.code ?? 'unknown');
    const MAP = {
      '131049': { retryable: false, fatal: false, note: 'سقف تكرار تسويقي — أجّل 24س' },
      '131026': { retryable: false, fatal: true,  note: 'الرقم مش على واتساب' },
      '131047': { retryable: false, fatal: false, note: 'خارج نافذة 24س — لازم قالب' },
      '131048': { retryable: false, fatal: false, note: 'قيد spam على الرقم' },
      '130429': { retryable: true,  retryAfterMs: 60_000, note: 'تجاوز معدل الإرسال' },
      '133016': { retryable: true,  retryAfterMs: 300_000, note: 'الرقم موقوف مؤقتاً' },
      '80007':  { retryable: true,  retryAfterMs: 120_000, note: 'حد المعدل' },
      '4':      { retryable: true,  retryAfterMs: 120_000, note: 'API rate limit' },
    };
    const m = MAP[code] ?? { retryable: true, retryAfterMs: 60_000 };
    return { ok: false, channel: 'official', errorCode: code, ...m };
  }

  async health() {
    const tier = await this.tierStore.current();
    const q    = await this.getQualityRating();     // GREEN|YELLOW|RED
    return {
      up: true,
      headroom: 1 - tier.usedToday / tier.limit,
      degraded: q === 'RED' || tier.usedToday / tier.limit > 0.9,
      quality: q,
    };
  }
}
```

### 2.3 المزوّد غير الرسمي

الحلو إنه **كل الكود موجود بالفعل** في [`04-ARCHITECTURE.md`](./04-ARCHITECTURE.md) و[`06-IMPLEMENTATION.md`](./06-IMPLEMENTATION.md). إحنا بس بنلفّه في نفس الواجهة:

```javascript
// providers/unofficial.js
import { Provider } from './base.js';

export class UnofficialProvider extends Provider {
  constructor({ evolution, resolveSession, delayEngine, gateChain, spin, redis }) {
    super();
    this.evo = evolution;               // evolution/client.js من ملف 06
    this.resolveSession = resolveSession; // sticky + least-loaded من ملف 04
    this.delay = delayEngine;           // DelayEngine من ملف 03
    this.gates = gateChain;             // البوابات التسعة من ملف 04
    this.spin = spin;                   // Spintax من ملف 03
    this.redis = redis;
  }

  get channel() { return 'unofficial'; }

  async can(req) {
    const sessionId = await this.resolveSession(req.meta.customerId);
    if (!sessionId) return { ok: false, reason: 'مفيش جلسة صالحة' };

    const verdict = await this.gates.evaluate({
      phone: req.to, sessionId,
      segment: req.meta.segment, campaignId: req.meta.campaignId,
    });
    return verdict.allowed
      ? { ok: true, sessionId, contactState: verdict.contactState }
      : { ok: false, reason: verdict.reason, gate: verdict.gate,
          retryAt: verdict.retryAt, drop: verdict.drop };
  }

  async send(req) {
    const fresh = await this.redis.set(`idem:${req.idempotencyKey}`, '1', 'NX', 'EX', 172800);
    if (!fresh) return { ok: true, channel: this.channel, deduped: true };

    const gate = await this.can(req);
    if (!gate.ok) return { ok: false, channel: 'unofficial', errorCode: 'GATE',
                            reason: gate.reason, retryable: !gate.drop,
                            retryAfterMs: 900_000 };

    // 🎲 التنويع + التأخير البشري — الحاجات اللي الرسمي مش محتاجها
    const body  = req.body ? this.spin(req.body) : undefined;
    const waitMs = await this.delay.compute({
      phone: req.to, sessionId: gate.sessionId,
      contactState: gate.contactState, textLength: body?.length ?? 0,
    });
    await sleep(waitMs);

    try {
      const r = await this.evo.sendMessage(gate.sessionId, { ...req, body });
      return { ok: true, channel: 'unofficial',
               providerMessageId: r.key.id, sessionId: gate.sessionId,
               estimatedCostUsd: 0, delayUsedMs: waitMs };
    } catch (e) {
      return this.mapError(e, gate.sessionId);
    }
  }

  async health() {
    const s = await this.evo.sessionStats();  // {healthy, total, avgRisk}
    return {
      up: s.healthy > 0,
      headroom: s.healthy / Math.max(s.total, 1),
      degraded: s.healthy < 2 || s.avgRisk > 60,
    };
  }
}
```

> 🔑 **لاحظ التماثل:** الاتنين ليهم `can()` / `send()` / `health()` بنفس التوقيع. الفرق كله جوّه. ده اللي بيخلّي الـ Router بسيط.

### 2.4 سجل المزوّدين

```javascript
// providers/registry.js
export function buildRegistry(deps) {
  const official   = new OfficialProvider(deps.official);
  const unofficial = new UnofficialProvider(deps.unofficial);
  return {
    official, unofficial,
    get(channel) {
      const p = { official, unofficial }[channel];
      if (!p) throw new Error(`مزوّد مجهول: ${channel}`);
      return p;
    },
    all: [official, unofficial],
  };
}
```

**فايدة إضافية مجانية:** نفس الواجهة تخليك تضيف `providers/mock.js` للاختبار المحلي — تجرّب النظام كله من غير ما تبعت رسالة واحدة حقيقية ولا تدفع سنت.

---

## 3. `WindowTracker` — أهم مكوّن في النظام

### 3.1 المشكلة اللي بيحلّها

قبل أي إرسال، النظام محتاج يعرف: **إيه النافذة المفتوحة للعميل ده دلوقتي؟** لأن الإجابة بتحدّد:

| النافذة | الرسمي يقدر يبعت؟ | التكلفة | القرار المنطقي |
|---|---|---|---|
| **FEP مفتوحة (72س)** | ✅ أي حاجة، **مجاناً** | صفر | 🏢 **رسمي** — مجاني وآمن، أفضل الدنيا |
| **CSW مفتوحة (24س)** | ✅ رسائل حرة | مجاني اليوم ← مدفوع من أكتوبر | ⚡ **غير رسمي** — مجاني للأبد |
| **مفيش نافذة** | ⚠️ قوالب معتمدة بس | سعر كامل | 🏢 رسمي للتسويق / ⚡ غير رسمي لو خطر مقبول |

### 3.2 آلة الحالة (State Machine)

```
                     ┌──────────────────┐
       ضغط إعلان     │                  │
       CTWA أو زرار  │   FEP_OPEN       │  72 ساعة — كل حاجة مجاناً 🎁
    ──────────────►  │   (72h)          │  ملاحظة: بتنفتح مرة واحدة بس
                     └────────┬─────────┘  لكل ضغطة إعلان
                              │ انتهت
                              ▼
   العميل بعت رسالة   ┌──────────────────┐
    ──────────────►   │   CSW_OPEN       │  24 ساعة — رسائل حرة
   (وبيتجدّد مع كل    │   (24h)          │  بتتجدّد مع كل رسالة داخلة
    رسالة داخلة)      └────────┬─────────┘
                              │ 24 ساعة سكوت
                              ▼
                     ┌──────────────────┐
                     │   NO_WINDOW      │  قوالب معتمدة بس (على الرسمي)
                     └──────────────────┘
```

**قواعد الأسبقية** (لو أكتر من نافذة مفتوحة):
1. `FEP_OPEN` تكسب دايماً (مجانية).
2. بعدها `CSW_OPEN`.
3. الافتراضي `NO_WINDOW`.

### 3.3 المخطط

```sql
-- نافذة واحدة لكل (عميل، نوع) — الأحدث هي الفعّالة
CREATE TYPE window_kind AS ENUM ('fep', 'csw');

CREATE TABLE customer_windows (
  id           BIGSERIAL PRIMARY KEY,
  customer_id  BIGINT NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
  phone        VARCHAR(20) NOT NULL,

  kind         window_kind NOT NULL,
  opened_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  expires_at   TIMESTAMPTZ NOT NULL,

  -- إيه اللي فتحها — مهم جداً للتحليل والتدقيق
  opened_by    VARCHAR(30) NOT NULL,
  -- ctwa_ad | page_cta | inbound_message | inbound_reply
  source_ref   VARCHAR(120),          -- ad_id / campaign_id / wa_message_id
  channel_seen VARCHAR(12),           -- على أنهي قناة شفنا الحدث

  renew_count  INT DEFAULT 0,         -- CSW بتتجدّد
  created_at   TIMESTAMPTZ DEFAULT NOW(),

  UNIQUE (customer_id, kind)          -- 🔑 نافذة واحدة لكل نوع، بنحدّثها
);

CREATE INDEX idx_win_active ON customer_windows (phone, expires_at DESC);
CREATE INDEX idx_win_expiry ON customer_windows (expires_at)
  WHERE expires_at > NOW();
```

### 3.4 الكود

```javascript
// core/window-tracker.js

export const WINDOW = {
  FEP:  { kind: 'fep', hours: 72 },
  CSW:  { kind: 'csw', hours: 24 },
};

export class WindowTracker {
  constructor({ db, redis }) { this.db = db; this.redis = redis; }

  // ───────── فتح / تجديد ─────────

  /** ضغط إعلان Click-to-WhatsApp أو زرار الصفحة → نافذة 72 ساعة مجانية */
  async openFEP({ customerId, phone, source, sourceRef, channel }) {
    const expiresAt = new Date(Date.now() + WINDOW.FEP.hours * 3600_000);
    await this.db.none(`
      INSERT INTO customer_windows
        (customer_id, phone, kind, opened_at, expires_at, opened_by, source_ref, channel_seen)
      VALUES ($1,$2,'fep',NOW(),$3,$4,$5,$6)
      ON CONFLICT (customer_id, kind) DO UPDATE SET
        opened_at = NOW(), expires_at = $3,
        opened_by = $4, source_ref = $5, channel_seen = $6,
        renew_count = customer_windows.renew_count + 1
    `, [customerId, phone, expiresAt, source, sourceRef, channel]);
    await this.invalidate(phone);
    return expiresAt;
  }

  /** أي رسالة داخلة من العميل → تفتح/تجدّد نافذة 24 ساعة */
  async touchCSW({ customerId, phone, messageId, channel }) {
    const expiresAt = new Date(Date.now() + WINDOW.CSW.hours * 3600_000);
    await this.db.none(`
      INSERT INTO customer_windows
        (customer_id, phone, kind, opened_at, expires_at, opened_by, source_ref, channel_seen)
      VALUES ($1,$2,'csw',NOW(),$3,'inbound_message',$4,$5)
      ON CONFLICT (customer_id, kind) DO UPDATE SET
        expires_at = $3, source_ref = $4, channel_seen = $5,
        renew_count = customer_windows.renew_count + 1
    `, [customerId, phone, expiresAt, messageId, channel]);
    await this.invalidate(phone);
    return expiresAt;
  }

  // ───────── قراءة (المسار الحار — لازم يكون سريع) ─────────

  /**
   * بترجّع حالة النوافذ للعميل. مخزّنة في Redis لأنها بتتنده على كل رسالة.
   * @returns {Promise<{state:'FEP_OPEN'|'CSW_OPEN'|'NO_WINDOW',
   *                    fepUntil:Date|null, cswUntil:Date|null,
   *                    freeFormAllowed:boolean, marketingFree:boolean}>}
   */
  async state(phone) {
    const cacheKey = `win:${phone}`;
    const cached = await this.redis.get(cacheKey);
    if (cached) return this.hydrate(JSON.parse(cached));

    const rows = await this.db.any(`
      SELECT kind, expires_at FROM customer_windows
      WHERE phone = $1 AND expires_at > NOW()
    `, [phone]);

    const fep = rows.find(r => r.kind === 'fep')?.expires_at ?? null;
    const csw = rows.find(r => r.kind === 'csw')?.expires_at ?? null;
    const raw = { fep, csw };

    // TTL = أقرب انتهاء، بحد أقصى 5 دقايق (نضمن الاتساق)
    const nearest = [fep, csw].filter(Boolean)
      .map(d => new Date(d).getTime() - Date.now()).sort((a, b) => a - b)[0];
    const ttl = Math.max(10, Math.min(300, Math.floor((nearest ?? 300_000) / 1000)));
    await this.redis.set(cacheKey, JSON.stringify(raw), 'EX', ttl);

    return this.hydrate(raw);
  }

  hydrate({ fep, csw }) {
    const now = Date.now();
    const fepOpen = fep && new Date(fep).getTime() > now;
    const cswOpen = csw && new Date(csw).getTime() > now;
    return {
      state: fepOpen ? 'FEP_OPEN' : cswOpen ? 'CSW_OPEN' : 'NO_WINDOW',
      fepUntil: fepOpen ? new Date(fep) : null,
      cswUntil: cswOpen ? new Date(csw) : null,
      freeFormAllowed: Boolean(fepOpen || cswOpen),
      marketingFree:   Boolean(fepOpen),   // 🎁 التسويق مجاني في FEP بس
    };
  }

  async invalidate(phone) { await this.redis.del(`win:${phone}`); }
}
```

> ⚠️ **تحذير عملي:** الـ cache TTL محسوب على أقرب انتهاء بحد 5 دقايق. لو خلّيته ساعة، ممكن تحسب إن نافذة مفتوحة وهي قافلة → الرسمي يرفض الرسالة الحرة بـ 131047 وتضيّع محاولة. **الاتساق أهم من الأداء هنا.**

### 3.5 من فين بتتغذّى النوافذ؟

| الحدث | المصدر | الفعل |
|---|---|---|
| رسالة داخلة على الرسمي | Cloud API webhook `messages` | `touchCSW(channel:'official')` |
| رسالة داخلة على غير الرسمي | Evolution webhook `messages.upsert` | `touchCSW(channel:'unofficial')` |
| ضغطة إعلان CTWA | Cloud API webhook — الرسالة فيها `referral` object | `openFEP(source:'ctwa_ad')` |
| ضغط زرار الصفحة | نفس الـ webhook مع `referral.source_type` | `openFEP(source:'page_cta')` |

**الحدث ده هو أهم شيء في النظام كله** — لازم تصطاده صح:

```javascript
// webhook/normalize.official.js
export function detectFEP(msg) {
  // Cloud API بتحط object اسمه referral لو الرسالة جاية من إعلان
  if (!msg.referral) return null;
  return {
    source: msg.referral.source_type === 'ad' ? 'ctwa_ad' : 'page_cta',
    sourceRef: msg.referral.source_id,        // ad_id
    headline: msg.referral.headline,
    ctwaClid: msg.referral.ctwa_clid,         // 🔑 للربط بالإعلان في Ads Manager
  };
}
```

> 🚨 **الفخ:** نافذة FEP بتتفتح على **الرقم الرسمي** اللي الإعلان ماشي عليه. لو العميل ضغط الإعلان وردّيت عليه من رقم **غير رسمي** — النافذة المجانية ضاعت وانت دفعت تكلفة الإعلان على الفاضي. راجع قاعدة التوجيه في §4.4.

---

## 4. `ChannelRouter` — القرار

### 4.1 تصنيف النوايا (Intent Taxonomy)

قبل ما نوجّه، لازم نسمّي. كل رسالة في النظام ليها **نية واحدة**:

```javascript
// core/intents.js
export const INTENT = {
  // ── تسويق (يبدأ من عندنا) ──
  CAMPAIGN_PROMO:      'campaign_promo',       // عرض/خصم لقطاع
  WINBACK:             'winback',              // استرجاع عميل نايم
  ABANDONED_CART:      'abandoned_cart',       // ⚠️ تسويقي عند Meta مش utility
  NEW_ARRIVAL:         'new_arrival',

  // ── معاملات (نتيجة فعل من العميل) ──
  ORDER_CONFIRMED:     'order_confirmed',
  ORDER_SHIPPED:       'order_shipped',
  ORDER_DELIVERED:     'order_delivered',
  ORDER_CANCELLED:     'order_cancelled',
  PAYMENT_REMINDER:    'payment_reminder',

  // ── محادثة (رد على العميل) ──
  BOT_REPLY:           'bot_reply',            // خطوات البوت
  AGENT_REPLY:         'agent_reply',           // موظف بشري
  FAQ_ANSWER:          'faq_answer',
  CATALOG_BROWSE:      'catalog_browse',        // صور/كتالوج

  // ── نظام ──
  OPT_OUT_ACK:         'opt_out_ack',           // تأكيد إلغاء الاشتراك
  OTP:                 'otp',
};

/** خصائص كل نية — الـ Router بيقرأ منها */
export const INTENT_SPEC = {
  campaign_promo:   { class: 'marketing',     critical: false, metaCategory: 'MARKETING' },
  winback:          { class: 'marketing',     critical: false, metaCategory: 'MARKETING' },
  abandoned_cart:   { class: 'marketing',     critical: false, metaCategory: 'MARKETING' },
  new_arrival:      { class: 'marketing',     critical: false, metaCategory: 'MARKETING' },

  order_confirmed:  { class: 'transactional', critical: true,  metaCategory: 'UTILITY' },
  order_shipped:    { class: 'transactional', critical: true,  metaCategory: 'UTILITY' },
  order_delivered:  { class: 'transactional', critical: false, metaCategory: 'UTILITY' },
  order_cancelled:  { class: 'transactional', critical: true,  metaCategory: 'UTILITY' },
  payment_reminder: { class: 'transactional', critical: true,  metaCategory: 'UTILITY' },

  bot_reply:        { class: 'conversational', critical: false, metaCategory: 'SERVICE' },
  agent_reply:      { class: 'conversational', critical: false, metaCategory: 'SERVICE' },
  faq_answer:       { class: 'conversational', critical: false, metaCategory: 'SERVICE' },
  catalog_browse:   { class: 'conversational', critical: false, metaCategory: 'SERVICE' },

  opt_out_ack:      { class: 'system', critical: true,  metaCategory: 'SERVICE' },
  otp:              { class: 'system', critical: true,  metaCategory: 'AUTHENTICATION' },
};
```

> 💡 **`critical: true` معناها:** الرسالة دي **لازم** توصل. لو القناة المفضّلة فشلت، جرّب التانية. لو التسويق فشل — منعملش حاجة، بس التسويق.

### 4.2 مصفوفة القرار

```
┌──────────────────┬─────────────┬─────────────┬────────────────┐
│  النية           │  FEP_OPEN   │  CSW_OPEN   │  NO_WINDOW     │
├──────────────────┼─────────────┼─────────────┼────────────────┤
│ تسويق            │ 🏢 رسمي     │ ⚡ غير رسمي  │ 🏢 رسمي قالب   │
│ (marketing)      │ حر/قالب     │ حر          │ (سقف 131049)   │
│                  │ **مجاناً** 🎁│ مجاناً       │ مدفوع          │
├──────────────────┼─────────────┼─────────────┼────────────────┤
│ معاملات critical │ 🏢 رسمي     │ 🏢 رسمي     │ 🏢 رسمي قالب   │
│ (transactional)  │ مجاناً       │ قالب utility│ قالب utility   │
├──────────────────┼─────────────┼─────────────┼────────────────┤
│ معاملات عادية    │ 🏢 رسمي     │ ⚡ غير رسمي  │ 🏢 رسمي قالب   │
├──────────────────┼─────────────┼─────────────┼────────────────┤
│ محادثة           │ ⚡ غير رسمي  │ ⚡ غير رسمي  │ ⛔ ممنوع        │
│ (conversational) │ (البوت هنا) │ (البوت هنا) │ (مفيش سياق)    │
├──────────────────┼─────────────┼─────────────┼────────────────┤
│ نظام (opt-out)   │ نفس قناة    │ نفس قناة    │ نفس قناة       │
│                  │ آخر رسالة   │ آخر رسالة   │ آخر رسالة      │
└──────────────────┴─────────────┴─────────────┴────────────────┘
```

**ليه المحادثة على غير الرسمي حتى داخل FEP المجانية؟**
لأن FEP مجانية لكنها **مؤقتة (72 ساعة)** والبوت ممكن ياخد 20-30 رسالة ذهاب وجياب. لو المحادثة طوّلت وخرجت من FEP وانت مبني على الرسمي، فجأة كل رسالة بقت مدفوعة (وبعد أكتوبر 2026 حتى الـ service بقت مدفوعة). خلّي **كل** المحادثة على مسار واحد ثابت التكلفة = صفر.
**الاستثناء الوحيد:** لو العميل جاي من إعلان، **أول رد** يبقى من الرقم الرسمي (عشان النافذة اتفتحت هناك) وبعد كده تنقل. تفاصيل في §4.4.

### 4.3 الكود

```javascript
// core/channel-router.js
import { INTENT_SPEC } from './intents.js';

export class ChannelRouter {
  constructor({ registry, windows, db, policy, log }) {
    this.registry = registry;
    this.windows  = windows;     // WindowTracker
    this.db = db;
    this.policy = policy;        // من .env — يخليك تغيّر السلوك بدون deploy
    this.log = log;
  }

  /**
   * @param {Object} intent { name, customerId, phone, body?, template?, campaignId?, segment? }
   * @returns {Promise<{channel, mode, reason, provider}>}
   */
  async route(intent) {
    const spec = INTENT_SPEC[intent.name];
    if (!spec) throw new Error(`نية مجهولة: ${intent.name}`);

    const win = await this.windows.state(intent.phone);

    // ── 0) مفتاح الطوارئ للقناة غير الرسمية (من ملف 03) ──
    const killed = await this.policy.isUnofficialKilled();

    // ── 1) opt-out: نفس قناة آخر تواصل، دايماً، فوراً ──
    if (spec.class === 'system' && intent.name === 'opt_out_ack') {
      const last = await this.lastChannelUsed(intent.phone);
      return this.pick(last ?? 'unofficial', 'free', 'رد opt-out على نفس القناة');
    }

    // ── 2) FEP مفتوحة = ذهب. كل حاجة رسمي ومجاناً — إلا المحادثة ──
    if (win.state === 'FEP_OPEN' && spec.class !== 'conversational') {
      return this.pick('official', win.freeFormAllowed ? 'free' : 'template',
        '🎁 نافذة FEP مفتوحة — الرسمي مجاني هنا');
    }

    // ── 3) معاملات حرجة: الرسمي دايماً (الموثوقية أهم من التكلفة) ──
    if (spec.class === 'transactional' && spec.critical) {
      return this.pick('official', win.freeFormAllowed ? 'free' : 'template',
        'رسالة معاملات حرجة — لازم توصل');
    }

    // ── 4) محادثة: غير رسمي دايماً (تكلفة صفر + مرونة كاملة) ──
    if (spec.class === 'conversational') {
      if (!win.freeFormAllowed)
        return this.deny('مفيش نافذة مفتوحة — منقدرش نبدأ محادثة');
      if (killed)
        return this.pick('official', 'free', '⚠️ القناة غير الرسمية موقوفة — تحويل للرسمي');
      return this.pick('unofficial', 'free', 'محادثة → غير رسمي (مجاني ومرن)');
    }

    // ── 5) العميل كلّمنا (CSW) → غير رسمي بلاش ──
    if (win.state === 'CSW_OPEN' && !killed) {
      return this.pick('unofficial', 'free',
        'نافذة CSW مفتوحة — العميل كلّمنا، الرد المجاني آمن');
    }

    // ── 6) مفيش نافذة + تسويق → رسمي بقالب (الأمان يستاهل الفلوس) ──
    if (spec.class === 'marketing') {
      if (this.policy.marketingChannel === 'unofficial' && !killed)
        return this.pick('unofficial', 'free',
          '⚠️ سياسة تفضّل غير الرسمي للتسويق — مخاطرة محسوبة');
      return this.pick('official', 'template',
        'تسويق بارد → قالب رسمي (بدون خطر حظر)');
    }

    // ── 7) الافتراضي: معاملات غير حرجة بره نافذة ──
    return this.pick('official', 'template', 'الافتراضي — قالب رسمي');
  }

  pick(channel, mode, reason) {
    return { channel, mode, reason, provider: this.registry.get(channel) };
  }
  deny(reason) { return { channel: null, mode: null, reason, provider: null }; }

  async lastChannelUsed(phone) {
    const r = await this.db.oneOrNone(`
      SELECT channel FROM message_log
      WHERE phone = $1 AND direction = 'out'
      ORDER BY created_at DESC LIMIT 1`, [phone]);
    return r?.channel ?? null;
  }
}
```

### 4.4 قاعدة "التسليم" — من الرسمي للغير رسمي

السيناريو الأخطر والأكتر ربحية في نفس الوقت: **عميل جاي من إعلان CTWA**.

```
   العميل يضغط إعلان ─────► رسالة تدخل على الرقم 🏢 الرسمي
                              │  (فيها referral → FEP اتفتحت 72 ساعة)
                              ▼
                   ┌─────────────────────────┐
                   │ ردّ أول من 🏢 الرسمي     │  مجاناً (FEP)
                   │ + بداية البوت           │
                   └───────────┬─────────────┘
                               │
            ┌──────────────────┴───────────────────┐
            ▼ الخيار أ (مُوصى به)                  ▼ الخيار ب
   خلّي المحادثة كلها على 🏢 الرسمي        اطلب من العميل يكلّم
   لحد ما FEP تخلص (72س)                  رقم ⚡ الخدمة (رقم تاني)
   → غالباً الأوردر يخلص جوّه المدة        → CSW تفتح على غير الرسمي
   ✅ صفر تعقيد، صفر تكلفة                ⚠️ 20-40% بيسيبوا في النقلة
```

**الحكم:** لو FEP مفتوحة → **متنقلش**. 72 ساعة أكتر من كفاية لإتمام أوردر. النقلة بين رقمين بتفقد عملاء أكتر من الفلوس اللي بتوفّرها.

```javascript
// نفّذ ده كـ حالة خاصة قبل قاعدة "المحادثة → غير رسمي"
async routeConversational(intent, win) {
  // لو العميل داخل من إعلان والنافذة المجانية لسه مفتوحة —
  // خلّي المحادثة على الرسمي، مجانية بالكامل
  if (win.state === 'FEP_OPEN' && this.policy.keepFepConversationsOfficial) {
    const hoursLeft = (win.fepUntil - Date.now()) / 3600_000;
    if (hoursLeft > 2)     // فيه وقت كفاية نخلّص أوردر
      return this.pick('official', 'free',
        `🎁 محادثة داخل FEP (باقي ${hoursLeft.toFixed(1)}س) — مجانية على الرسمي`);
  }
  return this.pick('unofficial', 'free', 'محادثة → غير رسمي');
}
```

### 4.5 التدهور (Degradation) — لما قناة تسقط

مش كفاية تختار قناة. لازم تعرف تعمل إيه لما القناة اللي اخترتها تبوظ.

| الحالة | التسويق | المعاملات الحرجة | المحادثة |
|---|---|---|---|
| ⚡ كل الجلسات محظورة | 🏢 حوّل للرسمي (قوالب) | 🏢 رسمي | 🏢 رسمي (مؤقت — راقب التكلفة) |
| 🏢 tier اتملى | ⏸️ أجّل لبكرة | ⚡ غير رسمي (استثناء مبرّر) | ⚡ غير رسمي |
| 🏢 quality = RED | ⏸️ وقّف التسويق الرسمي كله | 🏢 كمّل (utility بأمان) | ⚡ غير رسمي |
| 🏢 Cloud API واقعة | ⏸️ أجّل | ⚡ غير رسمي + تنبيه فوري | ⚡ غير رسمي |
| ⚡ + 🏢 الاتنين واقعين | 🚨 وقّف كل حاجة + تنبيه | 🚨 اطلب تدخل بشري | 🚨 رسالة "بنرجع لك" |

```javascript
// core/degradation.js
export async function resolveWithFallback(router, intent, { maxHops = 2 } = {}) {
  const tried = [];
  let decision = await router.route(intent);

  for (let hop = 0; hop < maxHops; hop++) {
    if (!decision.channel) return { ok: false, reason: decision.reason, tried };

    const h = await decision.provider.health();
    const can = await decision.provider.can(buildReq(intent, decision));

    if (h.up && !h.degraded && can.ok) return { ok: true, decision, tried };

    tried.push({ channel: decision.channel,
                 why: !h.up ? 'down' : h.degraded ? 'degraded' : can.reason });

    // التسويق ملهوش fallback — الأمان أهم من التسليم
    const spec = INTENT_SPEC[intent.name];
    if (spec.class === 'marketing' && !spec.critical)
      return { ok: false, reason: 'القناة المفضّلة مش متاحة والتسويق ملهوش بديل', tried };

    // بدّل القناة
    const alt = decision.channel === 'official' ? 'unofficial' : 'official';
    decision = router.pick(alt, 'auto', `fallback من ${decision.channel}`);
  }
  return { ok: false, reason: 'استنفدنا كل القنوات', tried };
}
```

> 🔑 **مبدأ:** التسويق **ملوش fallback**. لو الرسمي مش متاح، **متبعتش تسويق بارد من غير الرسمي** — دي أسرع طريقة تحرق أرقامك. أجّله بس.

---

## 5. البوابات الجديدة (Hybrid Gates)

الـ `GateChain` في [`04-ARCHITECTURE.md`](./04-ARCHITECTURE.md) فيها 9 بوابات لغير الرسمي. النظام الهجين بيضيف 4 بوابات **مشتركة** (بتشتغل على القناتين) + 3 خاصة بالرسمي.

```javascript
// core/gates/hybrid.js

// ═══════ بوابات مشتركة (القناتين) ═══════

/** 🔴 0. الموافقة (Opt-in) — قبل أي حاجة، والقناة مش بتغيّر ده */
async function gConsent({ phone, intentName }) {
  const spec = INTENT_SPEC[intentName];
  if (spec.class !== 'marketing') return { pass: true };   // المعاملات مستثناة

  const c = await this.db.oneOrNone(
    `SELECT opted_in, opt_in_source FROM customers WHERE phone = $1`, [phone]);
  if (!c?.opted_in)
    return { pass: false, reason: 'مفيش opt-in تسويقي', drop: true };
  return { pass: true };
}

/** 1. النافذة — هل الوضع المطلوب مسموح؟ */
async function gWindow({ phone, mode, channel }) {
  if (mode !== 'free') return { pass: true };              // القوالب مش محتاجة نافذة
  const w = await this.windows.state(phone);
  if (channel === 'official' && !w.freeFormAllowed)
    return { pass: false, reason: 'رسالة حرة بره نافذة على الرسمي (131047)',
             switchTo: 'template' };
  return { pass: true };
}

/** 2. سقف عالمي لكل عميل — أشدّ من سقف Meta، عبر القناتين */
async function gGlobalFrequency({ phone, intentName }) {
  const spec = INTENT_SPEC[intentName];
  if (spec.class !== 'marketing') return { pass: true };

  // 🔑 العدّاد بيحسب القناتين مع بعض — Meta بتحسب الرسمي بس،
  // لكن *العميل* بيحسّ بالاتنين. حماية السمعة قبل حماية الحساب.
  const key = `freq:mkt:${phone}`;
  const n = Number(await this.redis.get(key) ?? 0);
  const cap = this.policy.marketingPerCustomerPer24h;      // موصى به: 1
  if (n >= cap)
    return { pass: false, reason: `سقف تسويق موحّد ${n}/${cap} في 24س`,
             retryAt: new Date(Date.now() + 86_400_000) };
  return { pass: true };
}

/** 3. منع التكرار عبر القناتين — نفس النية لنفس العميل مرة واحدة */
async function gCrossChannelDedupe({ idempotencyKey }) {
  const seen = await this.redis.exists(`idem:${idempotencyKey}`);
  return seen
    ? { pass: false, reason: 'اتبعتت خلاص (idempotency)', drop: true }
    : { pass: true };
}

// ═══════ بوابات الرسمي فقط ═══════

/** 4. سقف Meta التسويقي — 131049 */
async function gMetaFrequencyCap({ phone, intentName, channel }) {
  if (channel !== 'official') return { pass: true };
  if (INTENT_SPEC[intentName].metaCategory !== 'MARKETING') return { pass: true };

  // ⚠️ السقف ده عند Meta وعبر *كل الشركات* — مش بس عندك.
  // يعني ممكن يترفض حتى لو انت مبعتلوش حاجة. تعامل معاه كاحتمال مش كضمان.
  const n = Number(await this.redis.get(`meta:mkt:${phone}`) ?? 0);
  if (n >= this.policy.metaMarketingCapAssumed)   // 2 كتقدير محافظ
    return { pass: false, reason: 'سقف Meta التسويقي المتوقع',
             retryAt: nextDay(), softFail: true };
  return { pass: true };
}

/** 5. الحد اليومي للـ Tier */
async function gMessagingTier({ channel }) {
  if (channel !== 'official') return { pass: true };
  const t = await this.tierStore.current();
  const used = t.usedToday / t.limit;
  if (used >= 1)    return { pass: false, reason: 'الـ tier خلص', retryAt: nextDay() };
  if (used >= 0.95) return { pass: false, reason: 'اقتربنا من حد الـ tier — نحجز الباقي للمعاملات',
                              reserveForCritical: true };
  return { pass: true };
}

/** 6. القالب معتمد ومتغيراته كاملة */
async function gTemplateReady({ channel, mode, template }) {
  if (channel !== 'official' || mode !== 'template') return { pass: true };
  const t = await this.templates.get(template.name);
  if (!t)                       return { pass: false, reason: 'القالب مش موجود', drop: true };
  if (t.status !== 'APPROVED')  return { pass: false, reason: `القالب ${t.status}`, drop: true };
  if (t.paused_until && t.paused_until > new Date())
    return { pass: false, reason: 'القالب موقوف بسبب جودة ضعيفة', retryAt: t.paused_until };

  const missing = t.requiredParams.filter(p => !(p in (template.params ?? {})));
  if (missing.length)
    return { pass: false, reason: `متغيرات ناقصة: ${missing.join(',')}`, drop: true };
  return { pass: true };
}
```

### ترتيب التنفيذ (مهم جداً)

```javascript
export const HYBRID_GATE_ORDER = [
  // ─── مشتركة: أرخص وأقطع أولاً ───
  gSuppression,          // من ملف 04 — قائمة الحظر
  gConsent,              // 🆕 opt-in
  gCrossChannelDedupe,   // 🆕 idempotency
  gGlobalFrequency,      // 🆕 سقفنا الموحّد

  // ─── بعد ما الـ Router يختار القناة ───
  gWindow,               // 🆕

  // ─── خاصة بالرسمي ───
  gMetaFrequencyCap,     // 🆕
  gMessagingTier,        // 🆕
  gTemplateReady,        // 🆕

  // ─── خاصة بغير الرسمي (كلها من ملف 04) ───
  gSessionStatus, gWarmup, gQuota, gCircadian,
  gRisk, gReplyRatio, gGlobalPool, gDuplicate,
];
```

> 🔑 **القاعدة:** البوابات المشتركة **الأول** لأنها أرخص (Redis/DB سريع) وبتقطع أكتر عدد. متعملش استدعاء لـ Cloud API عشان تكتشف إن العميل في قائمة الحظر.

---

## 6. المخطط الموحّد (Unified Schema Migration)

توسيع لجداول [`04-ARCHITECTURE.md`](./04-ARCHITECTURE.md) — **مش استبدال**. حافظنا على كل حاجة موجودة وضفنا أعمدة قابلة لـ NULL عشان الهجرة تبقى آمنة.

```sql
-- ═══════════════════════════════════════════════════════════
-- migrations/010_hybrid.sql
-- ═══════════════════════════════════════════════════════════

-- ── 1) نوع القناة ──
CREATE TYPE channel_kind AS ENUM ('official', 'unofficial');

-- ── 2) message_log: القناة + التكلفة + النية ──
ALTER TABLE message_log
  ADD COLUMN channel         channel_kind,
  ADD COLUMN intent          VARCHAR(40),
  ADD COLUMN window_state    VARCHAR(12),      -- FEP_OPEN|CSW_OPEN|NO_WINDOW لحظة الإرسال
  ADD COLUMN send_mode       VARCHAR(10),      -- free | template
  ADD COLUMN template_name   VARCHAR(120),
  ADD COLUMN meta_category   VARCHAR(20),      -- MARKETING|UTILITY|AUTHENTICATION|SERVICE
  ADD COLUMN idempotency_key VARCHAR(40),
  ADD COLUMN cost_estimated  NUMERIC(10,6) DEFAULT 0,
  ADD COLUMN cost_billed     NUMERIC(10,6),    -- بيتملى من webhook التسليم
  ADD COLUMN route_reason    TEXT,             -- ليه الـ Router اختار كده — للتدقيق
  ADD COLUMN fallback_from   channel_kind;     -- لو دي محاولة تانية

-- الأرقام القديمة كلها كانت غير رسمية
UPDATE message_log SET channel = 'unofficial' WHERE channel IS NULL;
ALTER TABLE message_log ALTER COLUMN channel SET NOT NULL;

-- 🔒 يمنع الإرسال المزدوج على مستوى قاعدة البيانات (حزام أمان تاني)
CREATE UNIQUE INDEX uq_msg_idem ON message_log (idempotency_key)
  WHERE idempotency_key IS NOT NULL;

CREATE INDEX idx_msg_channel_day ON message_log (channel, created_at DESC);
CREATE INDEX idx_msg_cost        ON message_log (created_at)
  WHERE cost_billed > 0;

-- ── 3) customers: تفضيلات القناة ──
ALTER TABLE customers
  ADD COLUMN preferred_channel  channel_kind,     -- NULL = خلّي الـ Router يقرر
  ADD COLUMN official_optin     BOOLEAN DEFAULT FALSE,
  ADD COLUMN official_optin_at  TIMESTAMPTZ,
  ADD COLUMN ctwa_clid          VARCHAR(200),     -- ربط الإعلان
  ADD COLUMN acquisition_source VARCHAR(40),      -- ctwa | organic | import | qr
  ADD COLUMN last_channel_used  channel_kind,
  ADD COLUMN mkt_sent_24h       SMALLINT DEFAULT 0,
  ADD COLUMN mkt_window_reset   TIMESTAMPTZ;

-- ── 4) conversations: المحادثة ممكن تعبر القنوات ──
ALTER TABLE conversations
  ADD COLUMN channel          channel_kind,
  ADD COLUMN channel_switches SMALLINT DEFAULT 0,
  ADD COLUMN opened_via       VARCHAR(30);      -- ctwa_ad | inbound | campaign_reply

-- ⚠️ القيد القديم UNIQUE(phone, session_id) كان بيفترض قناة واحدة.
-- على الرسمي مفيش session_id، فبنستخدم 'official' كقيمة ثابتة.
-- بديل أنضف: UNIQUE(phone) لو محادثة واحدة نشطة لكل عميل.

-- ── 5) 🆕 دفتر القوالب الرسمية ──
CREATE TABLE wa_templates (
  id            BIGSERIAL PRIMARY KEY,
  name          VARCHAR(120) UNIQUE NOT NULL,     -- اسم القالب عند Meta
  language      VARCHAR(10)  NOT NULL DEFAULT 'ar',
  category      VARCHAR(20)  NOT NULL,            -- MARKETING|UTILITY|AUTHENTICATION
  status        VARCHAR(20)  NOT NULL DEFAULT 'PENDING',
  -- PENDING|APPROVED|REJECTED|PAUSED|DISABLED
  quality       VARCHAR(10),                      -- GREEN|YELLOW|RED
  paused_until  TIMESTAMPTZ,

  body_text     TEXT NOT NULL,                    -- بمتغيرات {{1}} {{2}}
  header_kind   VARCHAR(12),                      -- text|image|video|document
  required_params JSONB DEFAULT '[]',             -- ["name","order_id"]
  intent        VARCHAR(40),                      -- 🔑 الربط بنياتنا الداخلية

  meta_id       VARCHAR(60),
  rejected_reason TEXT,
  submitted_at  TIMESTAMPTZ,
  approved_at   TIMESTAMPTZ,
  last_synced_at TIMESTAMPTZ,
  created_at    TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_tpl_intent ON wa_templates (intent, status);

-- ── 6) 🆕 دفتر التكاليف — عشان تعرف بتدفع كام فعلاً ──
CREATE TABLE cost_ledger (
  id            BIGSERIAL PRIMARY KEY,
  day           DATE NOT NULL,
  channel       channel_kind NOT NULL,
  meta_category VARCHAR(20),
  country_code  VARCHAR(4),

  msg_count     INT DEFAULT 0,
  delivered     INT DEFAULT 0,        -- 🔑 الفاتورة على التسليم مش الإرسال
  cost_usd      NUMERIC(12,6) DEFAULT 0,
  bsp_fee_usd   NUMERIC(12,6) DEFAULT 0,

  UNIQUE (day, channel, meta_category, country_code)
);

-- ── 7) 🆕 حالة الحساب الرسمي (tier + جودة) ──
CREATE TABLE official_status (
  id              SMALLINT PRIMARY KEY DEFAULT 1 CHECK (id = 1),
  phone_number_id VARCHAR(40),
  tier            VARCHAR(10),        -- TIER_250|TIER_1K|TIER_10K|TIER_100K|UNLIMITED
  daily_limit     INT,
  used_today      INT DEFAULT 0,
  quality_rating  VARCHAR(10),        -- GREEN|YELLOW|RED
  reset_at        TIMESTAMPTZ,
  last_checked_at TIMESTAMPTZ,
  notes           TEXT
);
INSERT INTO official_status (id, tier, daily_limit) VALUES (1, 'TIER_250', 250)
  ON CONFLICT DO NOTHING;
```

### عرض المراقبة الموحّد

```sql
CREATE VIEW v_hybrid_dashboard AS
SELECT
  DATE(created_at)                                   AS day,
  channel,
  meta_category,
  COUNT(*)                                            AS sent,
  COUNT(*) FILTER (WHERE status IN ('delivered','read')) AS delivered,
  COUNT(*) FILTER (WHERE status = 'failed')           AS failed,
  COUNT(*) FILTER (WHERE status = 'blocked')          AS blocked,
  ROUND(100.0 * COUNT(*) FILTER (WHERE status IN ('delivered','read'))
        / NULLIF(COUNT(*),0), 1)                      AS delivery_pct,
  ROUND(SUM(COALESCE(cost_billed, cost_estimated))::numeric, 2) AS cost_usd,
  ROUND(SUM(COALESCE(cost_billed, cost_estimated))::numeric
        / NULLIF(COUNT(*) FILTER (WHERE status IN ('delivered','read')),0), 5)
                                                      AS cost_per_delivered
FROM message_log
WHERE direction = 'out' AND created_at > NOW() - INTERVAL '30 days'
GROUP BY 1,2,3
ORDER BY 1 DESC, 2, 3;

-- 💡 المقياس اللي بيحكم على نجاح النظام الهجين
CREATE VIEW v_hybrid_efficiency AS
SELECT
  DATE(created_at) AS day,
  COUNT(*) FILTER (WHERE channel = 'unofficial')                      AS free_msgs,
  COUNT(*) FILTER (WHERE channel = 'official')                        AS paid_msgs,
  COUNT(*) FILTER (WHERE channel = 'official' AND window_state = 'FEP_OPEN') AS free_official,
  ROUND(100.0 * COUNT(*) FILTER (WHERE COALESCE(cost_billed, cost_estimated) = 0)
        / NULLIF(COUNT(*),0), 1)                                      AS free_pct,
  ROUND(SUM(COALESCE(cost_billed, cost_estimated))::numeric, 2)       AS spend_usd
FROM message_log
WHERE direction = 'out'
GROUP BY 1 ORDER BY 1 DESC;
```

> 🎯 **مؤشر النجاح:** `free_pct` لازم يكون **> 75%**. لو أقل، الـ Router مش شغال صح أو نسبة كبيرة من عملائك بارده (محتاج CTWA أكتر).

---

## 7. توحيد الأحداث الداخلة (Event Normalization)

القناتين بيبعتوا webhooks بأشكال مختلفة تماماً. الدماغ لازم تشوف شكل واحد.

```javascript
// webhook/normalizer.js

/**
 * @typedef {Object} InboundEvent
 * @property {'message'|'status'|'template_update'|'quality_alert'} kind
 * @property {'official'|'unofficial'} channel
 * @property {string} phone            E.164 بدون +
 * @property {string} [text]
 * @property {string} [mediaUrl]
 * @property {string} providerMessageId
 * @property {Date}   timestamp
 * @property {Object} [referral]       🔑 موجود بس لو جاي من إعلان
 * @property {string} [sessionId]      غير رسمي فقط
 * @property {string} [status]         sent|delivered|read|failed
 * @property {number} [billedAmount]   رسمي فقط — من status webhook
 */

export function normalizeOfficial(body) {
  const events = [];
  for (const entry of body.entry ?? []) {
    for (const ch of entry.changes ?? []) {
      const v = ch.value ?? {};

      // رسائل داخلة
      for (const m of v.messages ?? []) {
        events.push({
          kind: 'message', channel: 'official',
          phone: m.from,
          text: m.text?.body ?? m.button?.text ?? m.interactive?.list_reply?.title,
          mediaUrl: m.image?.id ?? m.document?.id,
          providerMessageId: m.id,
          timestamp: new Date(Number(m.timestamp) * 1000),
          referral: m.referral ?? null,          // 🎁 FEP!
        });
      }

      // تحديثات حالة — هنا الفاتورة الحقيقية
      for (const s of v.statuses ?? []) {
        events.push({
          kind: 'status', channel: 'official',
          phone: s.recipient_id,
          providerMessageId: s.id,
          status: s.status,                       // sent|delivered|read|failed
          timestamp: new Date(Number(s.timestamp) * 1000),
          // ⚠️ اسم الحقل ده اتغيّر مع نموذج per-message — أكّده من payload حقيقي
          billedAmount: s.pricing?.billable ? s.pricing : null,
          metaCategory: s.pricing?.category,
          errorCode: s.errors?.[0]?.code,
        });
      }
    }
  }
  return events;
}

export function normalizeUnofficial(body, sessionId) {
  const d = body.data ?? {};
  if (body.event === 'messages.upsert' && !d.key?.fromMe) {
    return [{
      kind: 'message', channel: 'unofficial',
      phone: d.key.remoteJid.split('@')[0],
      text: d.message?.conversation ?? d.message?.extendedTextMessage?.text,
      providerMessageId: d.key.id,
      timestamp: new Date((d.messageTimestamp ?? Date.now()/1000) * 1000),
      sessionId,
      referral: null,                              // مفيش CTWA على غير الرسمي
    }];
  }
  if (body.event === 'messages.update') {
    return [{
      kind: 'status', channel: 'unofficial',
      phone: d.key.remoteJid.split('@')[0],
      providerMessageId: d.key.id,
      status: { 2: 'sent', 3: 'delivered', 4: 'read' }[d.update?.status] ?? 'unknown',
      timestamp: new Date(), sessionId, billedAmount: null,
    }];
  }
  return [];
}
```

### المعالج الموحّد

```javascript
// webhook/handler.js
export async function handleInbound(event, deps) {
  const { db, windows, optOut, bot, queues } = deps;

  const customer = await upsertCustomer(db, event.phone, event.channel);

  if (event.kind === 'message') {
    // 1️⃣ FEP قبل أي حاجة — دي الفلوس
    if (event.referral) {
      await windows.openFEP({
        customerId: customer.id, phone: event.phone,
        source: event.referral.source_type === 'ad' ? 'ctwa_ad' : 'page_cta',
        sourceRef: event.referral.source_id, channel: event.channel,
      });
      await db.none(`UPDATE customers SET ctwa_clid=$2, acquisition_source='ctwa'
                     WHERE id=$1`, [customer.id, event.referral.ctwa_clid]);
    }

    // 2️⃣ CSW دايماً (أي رسالة داخلة)
    await windows.touchCSW({ customerId: customer.id, phone: event.phone,
                             messageId: event.providerMessageId, channel: event.channel });

    // 3️⃣ opt-out بأعلى أولوية — بيمشي على القناتين
    if (optOut.isOptOut(event.text)) {
      await queues.optOut.add('optout', { customerId: customer.id, phone: event.phone,
                                          channel: event.channel }, { priority: 1 });
      return;   // ⛔ متكمّلش للبوت
    }

    // 4️⃣ البوت — مش عارف ولا عايز يعرف القناة
    await queues.reply.add('bot', { event, customerId: customer.id }, { priority: 2 });
  }

  if (event.kind === 'status') {
    await db.none(`
      UPDATE message_log SET
        status = $2,
        delivered_at = CASE WHEN $2 = 'delivered' THEN $3 ELSE delivered_at END,
        read_at      = CASE WHEN $2 = 'read'      THEN $3 ELSE read_at      END,
        cost_billed  = COALESCE($4, cost_billed),
        error_code   = COALESCE($5, error_code)
      WHERE wa_message_id = $1`,
      [event.providerMessageId, event.status, event.timestamp,
       priceOf(event.billedAmount), event.errorCode]);

    // 💰 التكلفة بتتأكد على التسليم — حدّث الدفتر
    if (event.status === 'delivered') await deps.ledger.record(event);
  }
}
```

> ⏱️ **قاعدة الـ webhook (من ملف 06 وبتنطبق على القناتين):** رجّع `200` في **أقل من 5 ثواني** للرسمي (وإلا Meta بتعيد المحاولة وتقلل ثقتها) و**أقل من ثانية** لـ Evolution. **كل** المعالجة الحقيقية في طابور. متعملش شيء متزامن جوّه الـ handler.

---

## 8. ملخّص المكوّنات

| المكوّن | الملف | الدور | جديد؟ |
|---|---|---|---|
| `Provider` (عقد) | `providers/base.js` | الواجهة الموحّدة | 🆕 |
| `OfficialProvider` | `providers/official.js` | Cloud API + tier + freq cap | 🆕 |
| `UnofficialProvider` | `providers/unofficial.js` | لفّة على كود ملفات 03/04/06 | 🆕 (لفّة) |
| `WindowTracker` | `core/window-tracker.js` | FEP 72س / CSW 24س | 🆕 **الأهم** |
| `ChannelRouter` | `core/channel-router.js` | القرار: أنهي قناة وأنهي وضع | 🆕 |
| `resolveWithFallback` | `core/degradation.js` | التدهور عند السقوط | 🆕 |
| البوابات الهجينة | `core/gates/hybrid.js` | 7 بوابات إضافية | 🆕 |
| `EventNormalizer` | `webhook/normalizer.js` | شكلين → شكل واحد | 🆕 |
| `TemplateRegistry` | `core/templates.js` | القوالب المعتمدة ↔ النوايا | 🆕 (ملف 10) |
| `CostLedger` | `core/ledger.js` | تتبّع الفلوس الحقيقي | 🆕 |
| `DelayEngine`، `Warmup`، `Spintax`، `RiskScorer` | ملف 03 | زي ما هو — لغير الرسمي بس | ♻️ |
| `OrderBot`، `STATES` | ملف 05 | زي ما هو — مش بيعرف القناة | ♻️ |
| RFM، تنظيف الأرقام | ملف 01 | زي ما هو | ♻️ |
| الطوابير، `HealthMonitor`، Chatwoot | ملف 04 | زي ما هو | ♻️ |

**♻️ = مش محتاج تعديل.** ده مقياس نجاح التصميم: كل الكود القديم فضل شغّال زي ما هو، والهجين اتضاف كطبقة فوقه.

---

## 9. قائمة تحقّق التصميم

قبل ما تكتب سطر كود من ملف 10، جاوب على دي:

- [ ] هل فيه **قاعدة بيانات واحدة** بس؟ (لو اتنين — ارجع اقرأ §0)
- [ ] هل أي كود فوق طبقة Provider فيه `if (channel === ...)`؟ (لازم **لا**)
- [ ] هل `idempotencyKey` **إجباري** في كل مسارات الإرسال؟
- [ ] هل `opt_out` على أي قناة بيوقف **القناتين** فوراً؟
- [ ] هل `WindowTracker` بياخد تغذية من **الـ webhookين**؟
- [ ] هل بتصطاد `referral` object في الـ webhook الرسمي؟ (بدونه ضيّعت FEP)
- [ ] هل `route_reason` بيتسجّل في `message_log`؟ (بدونه مش هتعرف تدبّج القرارات)
- [ ] هل التسويق **مالوش fallback** لغير الرسمي؟
- [ ] هل `cost_billed` بيتملى من webhook التسليم مش من الإرسال؟
- [ ] هل عندك `providers/mock.js` تجرّب بيه بدون تكلفة؟

---

## 📚 التالي

| الملف | المحتوى |
|---|---|
| [`10-HYBRID-IMPLEMENTATION.md`](./10-HYBRID-IMPLEMENTATION.md) | الكود الشغّال، إدارة القوالب واعتمادها، خطة هجرة 6 أسابيع، دليل إعداد CTWA |

**مراجع داخلية:** [`04-ARCHITECTURE.md`](./04-ARCHITECTURE.md) (المخطط الأساسي والبوابات التسعة) · [`03-ANTIBAN-BIBLE.md`](./03-ANTIBAN-BIBLE.md) (DelayEngine، Warmup، Spintax) · [`05-ORDER-FUNNEL.md`](./05-ORDER-FUNNEL.md) (البوت — بيشتغل زي ما هو) · [`08-HYBRID-OVERVIEW.md`](./08-HYBRID-OVERVIEW.md) (الاقتصاد والقرار)

> ⚠️ **تنبيه دائم:** أسماء حقول Cloud API (خصوصاً `pricing` في status webhook) بتتغيّر مع تغيّر نموذج الفاتورة. **أكّد كل شكل payload من webhook حقيقي عندك قبل ما تعتمد عليه في الفواتير.**
