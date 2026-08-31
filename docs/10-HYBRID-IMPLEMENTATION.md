# 🛠️ 10 — تنفيذ النظام الهجين (Hybrid Implementation)

> **قبله:** [`08-HYBRID-OVERVIEW.md`](./08-HYBRID-OVERVIEW.md) (ليه) · [`09-HYBRID-ARCHITECTURE.md`](./09-HYBRID-ARCHITECTURE.md) (إزاي التصميم)
> **الملف ده:** الكود اللي بتشغّله فعلاً، وإدارة القوالب، وخطة هجرة 6 أسابيع، وإعداد CTWA.

---

## 0. اقرأ ده قبل أي حاجة

### 0.1 الترتيب الإجباري

```
❌ غلط: أبني الهجين من أول يوم
✅ صح:  شغّل غير الرسمي لوحده → استقر → ضيف الرسمي جنبه → وجّه
```

**السبب:** النظام الهجين فيه 3 أضعاف نقاط الفشل. لو ابتديت بيه مش هتعرف أي مشكلة جاية من فين. ملف [`06-IMPLEMENTATION.md`](./06-IMPLEMENTATION.md) هو **المرحلة صفر**. لو ملفتّهاش، اقفل الملف ده وارجعله.

### 0.2 الحسابات المطلوبة (بترتيب)

| الخطوة | المدة | ملاحظات |
|---|---|---|
| 1. Facebook Business Manager | فوري | لو عندك واحد استخدمه |
| 2. **التحقق من الأعمال (Business Verification)** | 2 أيام – 3 أسابيع | 🔴 أطول خطوة. ابدأها **الأول** |
| 3. WhatsApp Business Account (WABA) | فوري بعد التحقق | |
| 4. رقم للـ API | فوري | ⚠️ **رقم جديد** — مش رقمك اللي شغال |
| 5. اعتماد القوالب | 1 دقيقة – 24 ساعة | ابدأ بـ 3-5 قوالب بس |
| 6. حساب إعلانات + بكسل | فوري | لـ CTWA |

> 🔴 **الفخ الأكبر:** رقم بتنقله للـ API **مش بيرجع** تستخدمه على تطبيق واتساب العادي. ولو نقلت رقم شغّال على غير الرسمي، **هتخسر كل جلساته وتاريخها**. استخدم **رقم جديد نضيف** للرسمي.

### 0.3 قرار: Cloud API مباشر ولا BSP؟

| | **Cloud API مباشر** | **BSP (Twilio/360dialog/إلخ)** |
|---|---|---|
| السعر | سعر Meta بس | + $0.003–0.010/رسالة |
| الإعداد | أنت بتعمل كل حاجة | بيسهّلوا التحقق والقوالب |
| التحكم | كامل | محدود بالـ dashboard بتاعهم |
| الدعم | مجموعات مطوّرين | دعم بشري |
| **التوصية** | ✅ لو عندك مطوّر ووقت | لو مستعجل وحجمك صغير |

**لو اخترت BSP، اسأل السؤالين دول بالنص قبل التعاقد** (من ملف 08):
1. *"هل بتضيفوا هامش على أسعار Meta نفسها، ولا رسومكم منفصلة؟"*
2. *"إيه اللي هيتغيّر في فاتورتي بعد 1 أكتوبر 2026 لما رسائل الـ service والـ utility الداخلية تبقى مدفوعة؟"*

لو مردّوش بوضوح على التانية — **مش فاهمين النموذج الجديد. متتعاقدش.**

---

## 1. هيكل المشروع بعد الهجين

توسيع لشجرة ملف 06:

```
whatsapp-hybrid/
├── .env
├── docker-compose.yml
├── Makefile
├── src/
│   ├── index.js
│   ├── config.js                   ⬅️ موسّع (§2)
│   │
│   ├── providers/                  🆕 طبقة التجريد
│   │   ├── base.js
│   │   ├── official.js
│   │   ├── unofficial.js
│   │   ├── mock.js                 ⬅️ للاختبار بدون تكلفة
│   │   └── registry.js
│   │
│   ├── core/
│   │   ├── intents.js              🆕
│   │   ├── window-tracker.js       🆕
│   │   ├── channel-router.js       🆕
│   │   ├── degradation.js          🆕
│   │   ├── send.js                 🆕 ⬅️ نقطة الدخول الوحيدة للإرسال
│   │   ├── templates.js            🆕
│   │   ├── ledger.js               🆕
│   │   ├── tier-store.js           🆕
│   │   ├── freq-cap.js             🆕
│   │   └── gates/
│   │       ├── index.js
│   │       ├── unofficial.js       ♻️ من ملف 04
│   │       └── hybrid.js           🆕
│   │
│   ├── official/                   🆕
│   │   ├── client.js               ⬅️ axios على Graph API
│   │   ├── templates-api.js        ⬅️ إنشاء/مزامنة القوالب
│   │   └── status-poller.js        ⬅️ tier + quality كل 6 ساعات
│   │
│   ├── evolution/client.js         ♻️ من ملف 06
│   ├── engine/lock.js              ♻️
│   ├── webhook/
│   │   ├── official.js             🆕
│   │   ├── evolution.js            ♻️ موسّع
│   │   ├── normalizer.js           🆕
│   │   └── handler.js              🆕
│   ├── bot/                        ♻️ من ملف 05 — بدون تعديل
│   ├── jobs/
│   │   ├── campaign.worker.js      ⬅️ معدّل ليستخدم الـ Router
│   │   ├── reply.worker.js
│   │   ├── status.worker.js
│   │   ├── sync-templates.js       🆕
│   │   └── warmup-day.js           ♻️
│   └── db/migrations/
│       └── 010_hybrid.sql          🆕 من ملف 09 §6
└── scripts/
    ├── canary-test.js              ♻️
    ├── hybrid-dry-run.js           🆕 ⬅️ اختبار الـ Router بدون إرسال
    └── cost-report.js              🆕
```

---

## 2. الإعداد (`.env` + `config.js`)

```bash
# ═══════════════════════════════════════════════════════
# .env.hybrid — إضافات فوق .env بتاع ملف 06
# ═══════════════════════════════════════════════════════

# ── تشغيل/إيقاف القنوات ──
CHANNEL_OFFICIAL_ENABLED=true
CHANNEL_UNOFFICIAL_ENABLED=true

# ── الرسمي (Cloud API) ──
WA_GRAPH_VERSION=v21.0
WA_PHONE_NUMBER_ID=
WA_BUSINESS_ACCOUNT_ID=
WA_ACCESS_TOKEN=                    # System User token — دايم، مش مؤقت
WA_WEBHOOK_VERIFY_TOKEN=            # اختره أنت، أي نص عشوائي طويل
WA_APP_SECRET=                      # 🔐 للتحقق من توقيع الـ webhook

# ── سياسة التوجيه (تغيّرها بدون deploy) ──
POLICY_MARKETING_CHANNEL=official           # official | unofficial
POLICY_KEEP_FEP_CONVERSATIONS_OFFICIAL=true
POLICY_MKT_PER_CUSTOMER_PER_24H=1           # سقفنا — أشدّ من سقف Meta
POLICY_META_MKT_CAP_ASSUMED=2               # تقدير محافظ لسقف 131049
POLICY_ALLOW_MARKETING_FALLBACK=false       # 🔴 خلّيها false

# ── حدود التكلفة (حزام أمان مالي) ──
COST_DAILY_LIMIT_USD=50
COST_MONTHLY_LIMIT_USD=800
COST_ALERT_AT_PCT=70
COST_HARD_STOP_AT_PCT=100                   # يوقف الرسمي أوتوماتيك

# ── الـ Tier ──
TIER_SAFETY_MARGIN=0.95                     # نستخدم 95% بس من الحد
TIER_RESERVE_FOR_CRITICAL=0.10              # نحجز 10% للمعاملات

# ── CTWA ──
CTWA_AD_ACCOUNT_ID=
CTWA_FEP_HOURS=72
```

```javascript
// src/config.js  — إضافات
const required = [
  // ... اللي في ملف 06 ...
];

if (process.env.CHANNEL_OFFICIAL_ENABLED === 'true') {
  required.push('WA_PHONE_NUMBER_ID', 'WA_ACCESS_TOKEN',
                'WA_WEBHOOK_VERIFY_TOKEN', 'WA_APP_SECRET');
}

for (const k of required) {
  if (!process.env[k]) {
    console.error(`❌ متغير مطلوب ناقص: ${k}`);
    process.exit(1);
  }
}

export const config = {
  // ... اللي في ملف 06 ...

  channels: {
    official:   process.env.CHANNEL_OFFICIAL_ENABLED === 'true',
    unofficial: process.env.CHANNEL_UNOFFICIAL_ENABLED === 'true',
  },

  official: {
    graphVersion:   process.env.WA_GRAPH_VERSION ?? 'v21.0',
    phoneNumberId:  process.env.WA_PHONE_NUMBER_ID,
    wabaId:         process.env.WA_BUSINESS_ACCOUNT_ID,
    token:          process.env.WA_ACCESS_TOKEN,
    verifyToken:    process.env.WA_WEBHOOK_VERIFY_TOKEN,
    appSecret:      process.env.WA_APP_SECRET,
  },

  policy: {
    marketingChannel: process.env.POLICY_MARKETING_CHANNEL ?? 'official',
    keepFepConversationsOfficial:
      process.env.POLICY_KEEP_FEP_CONVERSATIONS_OFFICIAL !== 'false',
    marketingPerCustomerPer24h: +(process.env.POLICY_MKT_PER_CUSTOMER_PER_24H ?? 1),
    metaMarketingCapAssumed:    +(process.env.POLICY_META_MKT_CAP_ASSUMED ?? 2),
    allowMarketingFallback:
      process.env.POLICY_ALLOW_MARKETING_FALLBACK === 'true',
  },

  cost: {
    dailyLimitUsd:   +(process.env.COST_DAILY_LIMIT_USD ?? 50),
    monthlyLimitUsd: +(process.env.COST_MONTHLY_LIMIT_USD ?? 800),
    alertAtPct:      +(process.env.COST_ALERT_AT_PCT ?? 70),
    hardStopAtPct:   +(process.env.COST_HARD_STOP_AT_PCT ?? 100),
  },

  tier: {
    safetyMargin:       +(process.env.TIER_SAFETY_MARGIN ?? 0.95),
    reserveForCritical: +(process.env.TIER_RESERVE_FOR_CRITICAL ?? 0.10),
  },
};
```

> 🔑 **مبدأ من ملف 06 بيتكرّر هنا:** كل حد أمان في `.env`. لما تخاف بالليل، بتغيّر رقم وبتعمل restart — مش بتعدّل كود.

---

## 3. عميل الـ Cloud API

```javascript
// src/official/client.js
import axios from 'axios';
import { config } from '../config.js';

export const graph = axios.create({
  baseURL: `https://graph.facebook.com/${config.official.graphVersion}`,
  timeout: 20_000,
  headers: {
    Authorization: `Bearer ${config.official.token}`,
    'Content-Type': 'application/json',
  },
});

// 📋 سجّل كل استدعاء — هتحتاجه في التدبّج
graph.interceptors.response.use(
  r => r,
  e => {
    const err = e.response?.data?.error;
    console.error('[graph]', e.config?.url, err?.code, err?.message, err?.error_data?.details);
    return Promise.reject(e);
  }
);

/** حالة الرقم: الـ tier + الجودة */
export async function fetchPhoneStatus() {
  const { data } = await graph.get(`/${config.official.phoneNumberId}`, {
    params: { fields: 'quality_rating,messaging_limit_tier,display_phone_number,verified_name' },
  });
  return {
    quality: data.quality_rating,                 // GREEN|YELLOW|RED
    tier:    data.messaging_limit_tier,           // TIER_250|TIER_1K|...
    number:  data.display_phone_number,
    name:    data.verified_name,
  };
}

export const TIER_LIMITS = {
  TIER_50: 50, TIER_250: 250, TIER_1K: 1_000,
  TIER_10K: 10_000, TIER_100K: 100_000, TIER_UNLIMITED: Number.MAX_SAFE_INTEGER,
};
```

### مراقب الحالة (كل 6 ساعات — نفس دورة تقييم Meta)

```javascript
// src/official/status-poller.js
import { fetchPhoneStatus, TIER_LIMITS } from './client.js';

export async function pollOfficialStatus({ db, alerter }) {
  const s = await fetchPhoneStatus();
  const limit = TIER_LIMITS[s.tier] ?? 250;

  const prev = await db.one(`SELECT tier, quality_rating FROM official_status WHERE id=1`);

  await db.none(`
    UPDATE official_status SET
      tier=$1, daily_limit=$2, quality_rating=$3,
      phone_number_id=$4, last_checked_at=NOW()
    WHERE id=1`,
    [s.tier, limit, s.quality, process.env.WA_PHONE_NUMBER_ID]);

  // 🚨 تنبيهات
  if (prev.quality !== s.quality) {
    const sev = s.quality === 'RED' ? 'critical' : s.quality === 'YELLOW' ? 'warn' : 'info';
    await alerter.send(sev, `جودة الرقم الرسمي: ${prev.quality} → ${s.quality}`);
    if (s.quality === 'RED') {
      // 🔴 في 2026 الأحمر مش بينزّل الـ tier فوراً، بس بيمنع الترقية.
      // برضه: وقّف التسويق الرسمي — الاستمرار بيعمّق المشكلة.
      await db.none(`UPDATE official_status SET notes='marketing_paused_red' WHERE id=1`);
    }
  }
  if (prev.tier !== s.tier)
    await alerter.send('info', `الـ Tier: ${prev.tier} → ${s.tier} (${limit}/يوم)`);

  return { ...s, limit };
}
```

```javascript
// src/core/tier-store.js
export class TierStore {
  constructor({ db, redis }) { this.db = db; this.redis = redis; }

  async current() {
    const row = await this.db.one(
      `SELECT tier, daily_limit, quality_rating, notes FROM official_status WHERE id=1`);
    const used = Number(await this.redis.get(this.key()) ?? 0);
    return {
      tier: row.tier, limit: row.daily_limit,
      usedToday: used, quality: row.quality_rating,
      marketingPaused: row.notes === 'marketing_paused_red',
      headroom: 1 - used / row.daily_limit,
    };
  }

  async increment(n = 1) {
    const k = this.key();
    const v = await this.redis.incrby(k, n);
    if (v === n) await this.redis.expire(k, 90_000);   // ~25 ساعة
    return v;
  }

  // ⚠️ Meta بتصفّر الحد على UTC — لو حسبتها بتوقيت محلي هتتفاجئ
  key() { return `tier:used:${new Date().toISOString().slice(0, 10)}`; }
}
```

---

## 4. `send()` — نقطة الدخول الوحيدة

**كل** إرسال في النظام بيمر من الدالة دي. مفيش استثناءات، مفيش استدعاء مباشر لمزوّد من أي مكان تاني.

```javascript
// src/core/send.js
import { idempotencyKey } from './idem.js';
import { INTENT_SPEC } from './intents.js';
import { resolveWithFallback } from './degradation.js';

/**
 * @param {Object} intent
 *   { name, customerId, phone, body?, mediaUrl?, type?,
 *     templateParams?, campaignId?, segment? }
 */
export async function send(intent, deps) {
  const { router, db, windows, templates, ledger, costGuard, log } = deps;
  const spec = INTENT_SPEC[intent.name];
  if (!spec) throw new Error(`نية مجهولة: ${intent.name}`);

  // ── 1) 💰 حزام الأمان المالي — قبل أي حاجة ──
  const budget = await costGuard.check();
  if (budget.hardStop && spec.class === 'marketing')
    return fail('تجاوزنا حد الميزانية — التسويق موقوف', { budget });

  // ── 2) 🔑 مفتاح منع التكرار ──
  const idem = idempotencyKey({
    customerId: intent.customerId, intent: intent.name,
    campaignId: intent.campaignId, dayBucket: today(),
  });

  // ── 3) 🚦 التوجيه + التدهور ──
  const routed = await resolveWithFallback(router, intent);
  if (!routed.ok) return fail(routed.reason, { tried: routed.tried });

  const { channel, mode, reason, provider } = routed.decision;
  const win = await windows.state(intent.phone);

  // ── 4) 📝 بناء الطلب ──
  let req = {
    to: intent.phone,
    type: intent.type ?? 'text',
    body: intent.body,
    mediaUrl: intent.mediaUrl,
    idempotencyKey: idem,
    meta: {
      customerId: intent.customerId, campaignId: intent.campaignId,
      segment: intent.segment, intent: intent.name,
      windowOpen: win.freeFormAllowed,
    },
  };

  // القوالب: لازم نجيب القالب المربوط بالنية دي
  if (mode === 'template') {
    const tpl = await templates.forIntent(intent.name);
    if (!tpl) return fail(`مفيش قالب معتمد للنية ${intent.name}`, { fatal: true });
    req.template = templates.build(tpl, intent.templateParams ?? {});
    req.template.category = tpl.category;
  }

  // ── 5) 📒 سجّل النية *قبل* الإرسال (عشان متضيّعش حاجة لو النظام وقع) ──
  const logId = await db.one(`
    INSERT INTO message_log
      (campaign_id, customer_id, phone, direction, channel, intent,
       window_state, send_mode, template_name, meta_category,
       idempotency_key, content, route_reason, status, session_id)
    VALUES ($1,$2,$3,'out',$4,$5,$6,$7,$8,$9,$10,$11,$12,'sending',$13)
    ON CONFLICT (idempotency_key) DO NOTHING
    RETURNING id`,
    [intent.campaignId, intent.customerId, intent.phone, channel, intent.name,
     win.state, mode, req.template?.name, spec.metaCategory,
     idem, intent.body, reason, routed.decision.sessionId]
  ).catch(() => null);

  if (!logId) {
    log.info({ idem }, 'اتبعتت قبل كده — بنتخطى');
    return { ok: true, deduped: true };
  }

  // ── 6) 🚀 الإرسال ──
  const result = await provider.send(req);

  // ── 7) 📒 تحديث السجل ──
  await db.none(`
    UPDATE message_log SET
      status = $2, wa_message_id = $3, error_code = $4,
      cost_estimated = $5, sent_at = CASE WHEN $2='sent' THEN NOW() ELSE NULL END,
      delay_used_ms = $6, session_id = COALESCE($7, session_id)
    WHERE id = $1`,
    [logId.id, result.ok ? 'sent' : 'failed', result.providerMessageId,
     result.errorCode, result.estimatedCostUsd ?? 0,
     result.delayUsedMs, result.sessionId]);

  // ── 8) عدّادات ──
  if (result.ok && spec.class === 'marketing') {
    const k = `freq:mkt:${intent.phone}`;
    if (await deps.redis.incr(k) === 1) await deps.redis.expire(k, 86_400);
    if (channel === 'official') {
      const mk = `meta:mkt:${intent.phone}`;
      if (await deps.redis.incr(mk) === 1) await deps.redis.expire(mk, 86_400);
    }
  }

  log.info({ channel, mode, intent: intent.name, ok: result.ok,
             cost: result.estimatedCostUsd, reason }, 'send');
  return { ...result, logId: logId.id, channel, mode };

  function fail(reason, extra = {}) {
    log.warn({ intent: intent.name, phone: mask(intent.phone), reason, ...extra }, 'send_blocked');
    return { ok: false, reason, ...extra };
  }
}

const today = () => new Date().toISOString().slice(0, 10);
const mask  = p => p.slice(0, 5) + '****' + p.slice(-2);
```

### حزام الأمان المالي

```javascript
// src/core/cost-guard.js
import { config } from '../config.js';

export class CostGuard {
  constructor({ db, redis, alerter }) {
    Object.assign(this, { db, redis, alerter });
  }

  async check() {
    const [d, m] = await Promise.all([this.spentToday(), this.spentThisMonth()]);
    const dPct = d / config.cost.dailyLimitUsd   * 100;
    const mPct = m / config.cost.monthlyLimitUsd * 100;
    const worst = Math.max(dPct, mPct);

    if (worst >= config.cost.alertAtPct)
      await this.alertOnce(worst, d, m);

    return {
      spentToday: d, spentMonth: m, pct: worst,
      hardStop: worst >= config.cost.hardStopAtPct,
    };
  }

  async spentToday() {
    const r = await this.db.one(`
      SELECT COALESCE(SUM(COALESCE(cost_billed, cost_estimated)),0) AS s
      FROM message_log
      WHERE channel='official' AND created_at >= CURRENT_DATE`);
    return Number(r.s);
  }

  async spentThisMonth() {
    const r = await this.db.one(`
      SELECT COALESCE(SUM(COALESCE(cost_billed, cost_estimated)),0) AS s
      FROM message_log
      WHERE channel='official' AND created_at >= DATE_TRUNC('month', CURRENT_DATE)`);
    return Number(r.s);
  }

  async alertOnce(pct, d, m) {
    const key = `costalert:${new Date().toISOString().slice(0,13)}`;   // مرة/ساعة
    if (await this.redis.set(key, '1', 'NX', 'EX', 3600))
      await this.alerter.send(pct >= 100 ? 'critical' : 'warn',
        `💰 التكلفة الرسمية ${pct.toFixed(0)}% — اليوم $${d.toFixed(2)} / الشهر $${m.toFixed(2)}`);
  }
}
```

> 🔴 **مهم:** الحد الصلب بيوقف **التسويق بس** — المعاملات الحرجة بتفضل ماشية. لو وقّفت رسالة "أوردرك اتشحن" عشان الميزانية، بتخسر عميل بتكلفة أكبر من سعر الرسالة ألف مرة.

---

## 5. إدارة القوالب

### 5.1 الفكرة: النية ↔ القالب

الكود بيعرف **النوايا** بس. جدول `wa_templates` هو الجسر:

```
intent: 'order_shipped'  ──►  template: 'order_shipped_ar_v2'  ──►  Meta
                                (APPROVED, UTILITY)
```

كده لو قالب اترفض، بتعمل نسخة جديدة وتربطها بنفس النية — **بدون تعديل كود**.

```javascript
// src/core/templates.js
export class TemplateRegistry {
  constructor({ db, redis }) { Object.assign(this, { db, redis }); }

  /** أحدث قالب معتمد وغير موقوف للنية دي */
  async forIntent(intent, lang = 'ar') {
    const cached = await this.redis.get(`tpl:${intent}:${lang}`);
    if (cached) return JSON.parse(cached);

    const t = await this.db.oneOrNone(`
      SELECT * FROM wa_templates
      WHERE intent = $1 AND language = $2 AND status = 'APPROVED'
        AND (paused_until IS NULL OR paused_until < NOW())
        AND (quality IS NULL OR quality <> 'RED')
      ORDER BY approved_at DESC NULLS LAST LIMIT 1`, [intent, lang]);

    if (t) await this.redis.set(`tpl:${intent}:${lang}`, JSON.stringify(t), 'EX', 300);
    return t;
  }

  async get(name) {
    return this.db.oneOrNone(`SELECT * FROM wa_templates WHERE name = $1`, [name]);
  }

  /** بناء payload القالب بترتيب المتغيرات الصحيح */
  build(tpl, params) {
    const required = tpl.required_params ?? [];
    const missing = required.filter(p => params[p] == null);
    if (missing.length) throw new Error(`متغيرات ناقصة: ${missing.join(', ')}`);

    return {
      name: tpl.name,
      language: { code: tpl.language },
      components: [{
        type: 'body',
        // 🔑 الترتيب مهم — {{1}} = أول عنصر في required_params
        parameters: required.map(p => ({ type: 'text', text: String(params[p]) })),
      }],
    };
  }
}
```

### 5.2 القوالب الأساسية (ابدأ بدول بس)

| النية | التصنيف | لازم؟ | ملاحظة |
|---|---|---|---|
| `order_confirmed` | UTILITY | ✅ إجباري | أرخص تصنيف، بيتعتمد بسهولة |
| `order_shipped` | UTILITY | ✅ إجباري | |
| `order_cancelled` | UTILITY | ✅ إجباري | |
| `campaign_promo` | MARKETING | ✅ | الأغلى — واحد عام بمتغيرات |
| `winback` | MARKETING | ⏳ بعدين | |
| `abandoned_cart` | MARKETING | ⏳ بعدين | ⚠️ تسويقي عند Meta مش utility |

> 💡 **متعملش 20 قالب.** كل قالب فيه احتمال رفض، وكل رفض بيأثر على تقييم الحساب. اعمل **قالب عام بمتغيرات** أحسن من 10 قوالب متشابهة.

### 5.3 إنشاء قالب

```javascript
// src/official/templates-api.js
import { graph } from './client.js';
import { config } from '../config.js';

export async function createTemplate({ name, language = 'ar', category, bodyText,
                                       example, footer, buttons }) {
  const components = [{ type: 'BODY', text: bodyText }];

  // ⚠️ Meta بترفض القوالب اللي فيها متغيرات بدون أمثلة — دي أشهر سبب رفض
  if (/\{\{\d+\}\}/.test(bodyText)) {
    if (!example) throw new Error('قالب فيه متغيرات لازم example');
    components[0].example = { body_text: [example] };
  }
  if (footer)  components.push({ type: 'FOOTER', text: footer });
  if (buttons) components.push({ type: 'BUTTONS', buttons });

  const { data } = await graph.post(`/${config.official.wabaId}/message_templates`, {
    name, language, category, components,
  });
  return { metaId: data.id, status: data.status };  // PENDING أو APPROVED فوراً
}

/** مزامنة الحالات — شغّلها كل ساعة */
export async function syncTemplates(db) {
  const { data } = await graph.get(`/${config.official.wabaId}/message_templates`, {
    params: { fields: 'name,language,status,category,components,quality_score,rejected_reason',
              limit: 200 },
  });

  for (const t of data.data) {
    const body = t.components?.find(c => c.type === 'BODY')?.text ?? '';
    const varCount = (body.match(/\{\{\d+\}\}/g) ?? []).length;

    await db.none(`
      INSERT INTO wa_templates
        (name, language, category, status, quality, body_text,
         meta_id, rejected_reason, last_synced_at,
         approved_at, required_params)
      VALUES ($1,$2,$3,$4,$5,$6,$7,$8,NOW(),
              CASE WHEN $4='APPROVED' THEN NOW() ELSE NULL END, $9)
      ON CONFLICT (name) DO UPDATE SET
        status = $4, quality = $5, body_text = $6,
        rejected_reason = $8, last_synced_at = NOW(),
        approved_at = CASE WHEN $4='APPROVED' AND wa_templates.approved_at IS NULL
                           THEN NOW() ELSE wa_templates.approved_at END,
        -- 🔴 قالب بقى أحمر → وقّفه 24 ساعة أوتوماتيك
        paused_until = CASE WHEN $5='RED' THEN NOW() + INTERVAL '24 hours'
                            ELSE wa_templates.paused_until END`,
      [t.name, t.language, t.category, t.status,
       t.quality_score?.score ?? null, body, t.id, t.rejected_reason,
       JSON.stringify(Array.from({length: varCount}, (_, i) => `p${i+1}`))]);
  }
  return data.data.length;
}
```

### 5.4 نماذج قوالب عربية جاهزة

```javascript
// scripts/seed-templates.js
export const SEED = [
  {
    name: 'order_confirmed_ar', category: 'UTILITY', intent: 'order_confirmed',
    bodyText: 'أهلاً {{1}} 👋\n\nأوردرك رقم *{{2}}* اتأكد ✅\nالإجمالي: {{3}} جنيه\n' +
              'هنبعتلك تحديث أول ما يتشحن.\n\nشكراً لثقتك 🌟',
    example: ['محمد', '10245', '450'],
    footer: 'للاستفسار كلّمنا في أي وقت',
    params: ['name', 'order_id', 'total'],
  },
  {
    name: 'order_shipped_ar', category: 'UTILITY', intent: 'order_shipped',
    bodyText: 'أوردرك رقم *{{1}}* في السكة 🚚\n\nالتوصيل المتوقع: {{2}}\n' +
              'رقم التتبع: {{3}}\n\nلو حصلت أي مشكلة ردّ على الرسالة دي.',
    example: ['10245', 'يوم الخميس 21 أغسطس', 'EG9938271'],
    params: ['order_id', 'eta', 'tracking'],
  },
  {
    name: 'order_cancelled_ar', category: 'UTILITY', intent: 'order_cancelled',
    bodyText: 'أوردرك رقم *{{1}}* اتلغى.\n\nالسبب: {{2}}\n' +
              'لو ده مش صح كلّمنا فوراً ونحلها.',
    example: ['10245', 'الكمية مش متوفرة'],
    params: ['order_id', 'reason'],
  },
  {
    // 🎯 قالب تسويقي واحد عام بمتغيرات — أحسن من 10 قوالب
    name: 'promo_generic_ar', category: 'MARKETING', intent: 'campaign_promo',
    bodyText: 'أهلاً {{1}} 👋\n\n{{2}}\n\n{{3}}\n\n' +
              'لو مش عايز رسايل تانية ابعت *إلغاء*',
    example: ['محمد', 'خصم 25% على كل المجموعة الصيفية',
              'العرض لحد يوم الجمعة — اضغط الزرار تحت 👇'],
    footer: 'ابعت إلغاء للإيقاف',
    buttons: [{ type: 'URL', text: 'شوف العرض',
                url: 'https://yourshop.com/o/{{1}}',
                example: ['https://yourshop.com/o/abc123'] }],
    params: ['name', 'offer_line', 'cta_line'],
  },
];
```

### 5.5 ليه القوالب بتترفض (وإزاي تتجنّبه)

| السبب | نسبة الحدوث | الحل |
|---|---|---|
| متغيرات بدون أمثلة (`example`) | 🔴 عالية جداً | حط `example` لكل متغير — دايماً |
| متغير في أول أو آخر النص | 🔴 عالية | متبدأش/تخلّصش بـ `{{1}}` — حط نص قبله وبعده |
| متغيرات متجاورة `{{1}} {{2}}` | 🟠 متوسطة | افصل بينهم بنص |
| تصنيف غلط (تسويقي مسمّى utility) | 🔴 عالية | ⚠️ Meta بتعيد التصنيف لوحدها وبتحاسبك على الصح |
| كلام عن منتجات ممنوعة | 🔴 فوري | مكمّلات/أدوية/رهانات/كحول/أسلحة = رفض |
| نص عام قوي ("مبروك انت كسبت") | 🟠 | خلّيه محدّد وواقعي |
| رابط مختصر (bit.ly) | 🟠 | استخدم دومينك |
| ذكر واتساب/Meta في النص | 🟠 | متذكرهمش |
| لغة وتصنيف مش متطابقين | 🟡 | `language: 'ar'` مع نص عربي |

```javascript
// فاحص قبلي — يوفّر عليك دورات رفض
export function lintTemplate(body) {
  const issues = [];
  if (/^\s*\{\{\d+\}\}/.test(body))     issues.push('بيبدأ بمتغير');
  if (/\{\{\d+\}\}\s*$/.test(body))     issues.push('بيخلّص بمتغير');
  if (/\{\{\d+\}\}\s*\{\{\d+\}\}/.test(body)) issues.push('متغيرين متجاورين');
  if (/(bit\.ly|tinyurl|t\.co|goo\.gl)/i.test(body)) issues.push('رابط مختصر');
  if (/whatsapp|واتساب|meta|فيسبوك/i.test(body))    issues.push('ذكر واتساب/Meta');
  if (body.length > 1024)               issues.push('أطول من 1024 حرف');
  const nums = [...body.matchAll(/\{\{(\d+)\}\}/g)].map(m => +m[1]);
  const expected = nums.map((_, i) => i + 1);
  if (JSON.stringify([...new Set(nums)].sort((a,b)=>a-b)) !== JSON.stringify(expected))
    issues.push('ترقيم المتغيرات مش متسلسل من 1');
  return issues;
}
```

---

## 6. الـ Webhook الرسمي

### 6.1 التحقق + الأمان

```javascript
// src/webhook/official.js
import crypto from 'crypto';
import { config } from '../config.js';
import { normalizeOfficial } from './normalizer.js';

export function registerOfficialWebhook(app, deps) {

  // ── 1) التحقق (Meta بتنده عليه مرة عند الإعداد) ──
  app.get('/webhook/official', (req, res) => {
    const mode  = req.query['hub.mode'];
    const token = req.query['hub.verify_token'];
    if (mode === 'subscribe' && token === config.official.verifyToken)
      return res.status(200).send(req.query['hub.challenge']);
    return res.sendStatus(403);
  });

  // ── 2) استلام الأحداث ──
  // ⚠️ لازم raw body للتوقيع — express.json() بيدمّره
  app.post('/webhook/official',
    express.raw({ type: 'application/json' }),
    async (req, res) => {

      // 🔐 التحقق من التوقيع — بدونه أي حد يقدر يزوّر أحداث
      const sig = req.get('X-Hub-Signature-256') ?? '';
      const expected = 'sha256=' + crypto
        .createHmac('sha256', config.official.appSecret)
        .update(req.body).digest('hex');

      if (sig.length !== expected.length ||
          !crypto.timingSafeEqual(Buffer.from(sig), Buffer.from(expected))) {
        deps.log.warn('توقيع webhook غلط — مرفوض');
        return res.sendStatus(401);
      }

      // ⏱️ رجّع 200 فوراً — Meta بتعيد المحاولة لو أبطأ من 5 ثواني
      res.sendStatus(200);

      // كل المعالجة بعد الرد
      try {
        const body = JSON.parse(req.body.toString());
        const events = normalizeOfficial(body);
        for (const ev of events) {
          // opt-out بأولوية مطلقة — بيدخل الطابور مباشرة
          const priority = deps.optOut.isOptOut(ev.text) ? 1
                         : ev.kind === 'message' ? 2 : 3;
          await deps.queues.inbound.add('official', ev, { priority });
        }
      } catch (e) {
        deps.log.error({ err: e.message }, 'فشل معالجة webhook رسمي');
      }
    });
}
```

> 🔐 **متتخطّاش التحقق من التوقيع.** الـ webhook بتاعك عام على الإنترنت. بدون توقيع، أي حد يقدر يبعتلك أحداث مزيفة — يفتح نوافذ FEP وهمية، يزوّر opt-outs، يخرّب داتاك.

### 6.2 توحيد الأحداث

الكود الكامل في [`09-HYBRID-ARCHITECTURE.md`](./09-HYBRID-ARCHITECTURE.md) §7. أهم حاجة تتأكد منها بعد أول رسالة حقيقية:

```javascript
// scripts/inspect-webhook.js — شغّله مرة واحدة وشوف الشكل الحقيقي
app.post('/webhook/inspect', express.json(), (req, res) => {
  console.log(JSON.stringify(req.body, null, 2));   // 👈 احفظ الناتج
  res.sendStatus(200);
});
```

**ليه؟** حقل `pricing` في status webhook اتغيّر مع نموذج per-message. الأمثلة في الدوكيومنتيشن ممكن تكون قديمة. **شوف الحقيقة بنفسك قبل ما تعتمد عليها في حساب الفلوس.**

---

## 7. عامل الحملة الهجين

التعديل على `campaign.worker.js` من ملف 04 أبسط مما تتخيل — لأن كل التعقيد جوّه الـ Router:

```javascript
// src/jobs/campaign.worker.js
import { Worker } from 'bullmq';
import { send } from '../core/send.js';

export function startCampaignWorker(deps) {
  return new Worker('campaign', async job => {
    const { customerId, phone, campaignId, segment, intentName, body, templateParams } = job.data;

    // 🔴 مفتاح الطوارئ العام (من ملف 03) — يشمل القناتين
    if (await deps.killSwitch.isActive())
      throw new Error('kill switch نشط — الحملة موقوفة');

    const result = await send({
      name: intentName ?? 'campaign_promo',
      customerId, phone, campaignId, segment,
      body, templateParams,
    }, deps);

    if (!result.ok) {
      // فشل نهائي؟ متعيدش المحاولة
      if (result.fatal || result.drop) {
        await deps.db.none(`
          UPDATE message_log SET status='failed', error_msg=$2
          WHERE id=$1`, [result.logId, result.reason]);
        return { skipped: true, reason: result.reason };
      }
      throw new Error(result.reason);   // BullMQ هيعيد بـ exponential backoff
    }
    return result;
  }, {
    connection: deps.redisConn,
    // ⚠️ concurrency = 1 للحملات. التوازي بيولّد أنماط غير بشرية.
    // التسريع بيجي من عدد الجلسات مش من التوازي على جلسة واحدة.
    concurrency: 1,
    limiter: { max: 1, duration: 8_000 },   // سقف علوي إضافي
  });
}
```

### طبّاخ الحملة (Campaign Planner)

```javascript
// src/jobs/campaign.planner.js

/**
 * بيقسّم قاعدة العملاء على القناتين *قبل* ما يحط في الطابور.
 * ده بيخليك تشوف التكلفة المتوقعة قبل ما تبعت حرف.
 */
export async function planCampaign({ campaignId, segment }, deps) {
  const customers = await deps.db.any(`
    SELECT c.id, c.phone, c.name, c.segment
    FROM customers c
    WHERE c.segment = $1
      AND c.opted_in = TRUE
      AND NOT EXISTS (SELECT 1 FROM suppression_list s WHERE s.phone = c.phone)
    ORDER BY c.priority ASC, c.monetary DESC`, [segment]);

  const plan = { official: [], unofficial: [], skipped: [], estimatedCostUsd: 0 };

  for (const c of customers) {
    const routed = await deps.router.route({
      name: 'campaign_promo', customerId: c.id, phone: c.phone, campaignId, segment,
    });

    if (!routed.channel) { plan.skipped.push({ ...c, reason: routed.reason }); continue; }

    const win = await deps.windows.state(c.phone);
    const cost = routed.channel === 'unofficial' ? 0
               : win.marketingFree ? 0                       // 🎁 FEP
               : deps.costBook.price(c.phone, 'MARKETING');

    plan[routed.channel].push({ ...c, reason: routed.reason, cost });
    plan.estimatedCostUsd += cost;
  }

  return plan;
}

/** اطبع الخطة — راجعها بعينك قبل الإرسال */
export function printPlan(plan) {
  console.log(`
╔══════════════════════════════════════════════════╗
║             خطة الحملة (معاينة)                  ║
╠══════════════════════════════════════════════════╣
║ ⚡ غير رسمي (مجاني)      : ${String(plan.unofficial.length).padStart(6)} عميل      ║
║ 🏢 رسمي (مدفوع)          : ${String(plan.official.length).padStart(6)} عميل      ║
║ ⛔ متخطّى                 : ${String(plan.skipped.length).padStart(6)} عميل      ║
╠══════════════════════════════════════════════════╣
║ 💰 التكلفة المتوقعة       : $${plan.estimatedCostUsd.toFixed(2).padStart(9)}      ║
║ 📊 نسبة المجاني           : ${(100*plan.unofficial.length/
      Math.max(1,plan.unofficial.length+plan.official.length)).toFixed(0).padStart(5)}%       ║
╚══════════════════════════════════════════════════╝`);

  // أول 5 أسباب تخطّي — عشان تكتشف مشاكل الداتا
  const reasons = {};
  for (const s of plan.skipped) reasons[s.reason] = (reasons[s.reason] ?? 0) + 1;
  console.log('أسباب التخطّي:', Object.entries(reasons)
    .sort((a,b) => b[1]-a[1]).slice(0,5));
}
```

**استخدمه كده:**

```bash
# معاينة بدون إرسال — دايماً اعمل ده الأول
node scripts/plan-campaign.js --segment=loyal --dry-run

# لو الأرقام منطقية، شغّل
node scripts/plan-campaign.js --segment=loyal --execute
```

---

## 8. الاختبار بدون تكلفة وبدون خطر

### 8.1 المزوّد الوهمي

```javascript
// src/providers/mock.js
import { Provider } from './base.js';

export class MockProvider extends Provider {
  constructor(channel, { failRate = 0, log } = {}) {
    super(); this._ch = channel; this.failRate = failRate; this.log = log;
    this.sent = [];
  }
  get channel() { return this._ch; }
  async can() { return { ok: true }; }
  async health() { return { up: true, degraded: false, headroom: 1 }; }

  async send(req) {
    this.sent.push(req);
    this.log?.info({ ch: this._ch, to: mask(req.to), mode: req.template ? 'tpl' : 'free',
                     body: (req.body ?? req.template?.name)?.slice(0, 60) }, '📮 MOCK');
    if (Math.random() < this.failRate)
      return { ok: false, channel: this._ch, errorCode: 'MOCK_FAIL', retryable: true };
    return { ok: true, channel: this._ch,
             providerMessageId: `mock_${Date.now()}_${Math.random().toString(36).slice(2,8)}`,
             estimatedCostUsd: this._ch === 'official' ? 0.025 : 0 };
  }
}
const mask = p => p.slice(0,5) + '****' + p.slice(-2);
```

```bash
# .env.test
PROVIDER_MODE=mock
MOCK_FAIL_RATE=0.05
```

### 8.2 اختبار جدول القرارات

```javascript
// scripts/hybrid-dry-run.js
// بيثبت إن الـ Router بياخد القرار الصح في كل تركيبة

const CASES = [
  // [النية,            حالة النافذة, القناة المتوقعة, الوضع المتوقع]
  ['campaign_promo',    'FEP_OPEN',   'official',   'free'],
  ['campaign_promo',    'CSW_OPEN',   'unofficial', 'free'],
  ['campaign_promo',    'NO_WINDOW',  'official',   'template'],
  ['order_confirmed',   'FEP_OPEN',   'official',   'free'],
  ['order_confirmed',   'CSW_OPEN',   'official',   'free'],
  ['order_confirmed',   'NO_WINDOW',  'official',   'template'],
  ['order_delivered',   'CSW_OPEN',   'unofficial', 'free'],
  ['bot_reply',         'FEP_OPEN',   'official',   'free'],   // سياسة FEP
  ['bot_reply',         'CSW_OPEN',   'unofficial', 'free'],
  ['bot_reply',         'NO_WINDOW',  null,         null],     // مرفوض بحق
  ['faq_answer',        'CSW_OPEN',   'unofficial', 'free'],
  ['abandoned_cart',    'NO_WINDOW',  'official',   'template'],
];

let pass = 0, fail = 0;
for (const [intent, winState, expCh, expMode] of CASES) {
  const phone = await seedPhoneWithWindow(winState);
  const r = await router.route({ name: intent, customerId: 1, phone });
  const ok = r.channel === expCh && (expCh === null || r.mode === expMode);
  console.log(`${ok ? '✅' : '❌'} ${intent.padEnd(18)} ${winState.padEnd(10)} ` +
              `→ ${String(r.channel).padEnd(11)} ${String(r.mode).padEnd(9)} ` +
              `${ok ? '' : `(متوقع ${expCh}/${expMode})`}`);
  ok ? pass++ : fail++;
}
console.log(`\n${pass} نجح / ${fail} فشل`);
process.exit(fail ? 1 : 0);
```

> 🧪 **شغّل ده في CI.** أي تعديل على الـ Router لازم يعدّي الجدول ده. من غير الاختبار ده، أول مرة تعدّل السياسة هتكتشف الغلط من الفاتورة.

### 8.3 الكناري الهجين

توسيع لـ `canary-test.js` من ملف 06 — الآن على القناتين:

```javascript
// scripts/canary-hybrid.js
const THRESHOLDS = {
  unofficial: { minDelivered: 0.90, minReplied: 0.10, maxBlocked: 0.00, maxOptOut: 0.05 },
  official:   { minDelivered: 0.95, minReplied: 0.05, maxBlocked: 0.00, maxOptOut: 0.03 },
  // 🆕 مقاييس هجينة
  hybrid:     { minFreePct: 0.70, maxCostPerOrder: 1.50, maxChannelSwitches: 0.15 },
};

// 20 عميل على كل قناة، نستنى 6 ساعات، نقيس
```

---

## 9. خطة الهجرة — 6 أسابيع

### 📅 الأسبوع 1 — الورق (بدون كود)

| اليوم | المهمة | المخرج |
|---|---|---|
| 1 | ابدأ **التحقق من الأعمال** 🔴 | مستندات مرفوعة |
| 1 | اقرأ ملف 08 + املأ جدول أسعار مصر بنفسك | جدول أسعار حقيقي |
| 2 | قرار: Cloud API مباشر ولا BSP | قرار مكتوب |
| 3 | اشتري **رقم جديد** للرسمي | شريحة/رقم |
| 4 | اكتب نصوص 3 قوالب utility + 1 تسويقي | نصوص جاهزة |
| 5 | شغّل `lintTemplate()` على النصوص | صفر تحذيرات |

> ⏳ التحقق من الأعمال بياخد **من يومين لـ 3 أسابيع**. طول ما هو ماشي، كمّل باقي الأسابيع على القناة غير الرسمية.

### 📅 الأسبوع 2 — الأساس

```bash
# 1) الهجرة
psql $DATABASE_URL -f src/db/migrations/010_hybrid.sql

# 2) الكود — الجديد مع الوهمي
PROVIDER_MODE=mock npm run dev

# 3) اختبار جدول القرارات
node scripts/hybrid-dry-run.js       # 👈 لازم 12/12
```

**قائمة تحقق الأسبوع 2:**
- [ ] `010_hybrid.sql` اتطبّق بدون خسارة داتا
- [ ] `WindowTracker` بيفتح ويقفل نوافذ صح (اختبار وحدة)
- [ ] `hybrid-dry-run.js` بيعدّي 100%
- [ ] `MockProvider` بيسجّل كل إرسال
- [ ] كل القناة غير الرسمية القديمة **لسه شغالة زي ما هي** 🔑

> 🔑 **معيار النجاح:** لو أي حاجة قديمة بطّلت تشتغل — **ارجع**. الهجين طبقة إضافية، مش تعديل.

### 📅 الأسبوع 3 — الرسمي بيتنفّس

| اليوم | المهمة |
|---|---|
| 1 | التحقق خلص → اعمل WABA + سجّل الرقم |
| 2 | إعداد الـ webhook + **اختبر التوقيع** |
| 2 | شغّل `inspect-webhook.js` واحفظ شكل payload حقيقي |
| 3 | قدّم 3 قوالب utility |
| 4 | `syncTemplates()` → تأكد الحالة APPROVED |
| 5 | ابعت **رسالة واحدة** لرقمك الشخصي من الرسمي |
| 6-7 | تتبّع دورة الرسالة كاملة: send → sent → delivered → read → `cost_billed` |

**قائمة تحقق الأسبوع 3:**
- [ ] Tier = `TIER_1K` على الأقل (بعد التحقق)
- [ ] الجودة `GREEN`
- [ ] 3 قوالب utility معتمدة
- [ ] `cost_billed` بيتملى فعلاً من webhook التسليم 💰
- [ ] `pollOfficialStatus()` ماشي كل 6 ساعات

### 📅 الأسبوع 4 — التوجيه الحقيقي (5% بس)

```javascript
// تشغيل تدريجي — مش تشغيل كامل
POLICY_ROUTE_SAMPLE_PCT=5     // 5% من الترافيك يمر على الـ Router الجديد
```

```javascript
// في send() — بوابة العيّنة
const useHybrid = hashPercent(intent.customerId) < config.policy.routeSamplePct;
if (!useHybrid) return legacySendUnofficial(intent, deps);   // المسار القديم
```

**اليوم 7:** قارن. الجدول ده هو اللي بيقول لو تكمّل ولا ترجع:

| المقياس | العيّنة الهجينة | المسار القديم | الحكم |
|---|---|---|---|
| نسبة التسليم | ? | ? | الهجين ≥ القديم |
| نسبة الرد | ? | ? | الهجين ≥ القديم |
| نسبة الحظر | ? | ? | الهجين ≤ القديم |
| تكلفة/أوردر | ? | $0 | مقبولة؟ |
| نسبة المجاني | ? | 100% | > 70% |

### 📅 الأسبوع 5 — CTWA + توسيع

```
POLICY_ROUTE_SAMPLE_PCT=5 → 25 → 60 → 100
```

+ شغّل أول إعلان Click-to-WhatsApp (§10). **هنا بتبدأ تكسب فعلاً.**

### 📅 الأسبوع 6 — الاستقرار

- [ ] `v_hybrid_dashboard` مربوطة على شاشة/تنبيه يومي
- [ ] `CostGuard` جرّبته بحد وهمي منخفض وتأكدت إنه بيوقف التسويق
- [ ] `resolveWithFallback` جرّبته بإطفاء قناة يدوي
- [ ] `make panic` بيوقف **القناتين**
- [ ] `free_pct > 75%`
- [ ] وثّقت شكل payload الحقيقي للـ status webhook

---

## 10. دليل CTWA — أهم استثمار في النظام

### 10.1 ليه ده أهم قسم في الملف

```
الطريقة العادية:                  طريقة CTWA:
─────────────────                 ────────────────
تدفع لكل رسالة تسويق              تدفع للإعلان (اللي بتدفعه أصلاً)
+ خطر spam report                 + العميل هو اللي جه لك ✅
+ سقف 131049                      + نافذة 72 ساعة كل حاجة مجاناً 🎁
+ محتاج opt-in                    + مفيش سقف تكرار
                                  + مفيش خطر حظر
```

**النافذة دي مش هتتأثر بتغيير أكتوبر 2026.** هي أثبت حاجة في النظام كله.

### 10.2 الإعداد خطوة بخطوة

```
1. Meta Ads Manager → حملة جديدة
2. الهدف: "Engagement" ← ثم "Messaging apps"
3. مكان الرسائل: ☑ WhatsApp  ☐ Messenger  ☐ Instagram
   ⚠️ اختار واتساب لوحده. المتعدد بيخفّف الإشارة.
4. اربط WABA + الرقم الرسمي
5. الجمهور: ابدأ بـ Custom Audience من عملائك
   (رفع أرقام العملاء = أعلى تحويل وأرخص كليك)
6. الإبداع: صورة/فيديو + نص
7. 🔑 الرسالة الافتتاحية (Welcome message):
```

**الرسالة الافتتاحية هي أهم 3 سطور في النظام كله.** هي اللي بتحوّل الضغطة لمحادثة، والمحادثة لأوردر:

```
❌ سيئة:
"أهلاً بك في متجرنا! كيف يمكننا مساعدتك؟"
   → العميل مش عارف يعمل إيه → بيسيب

✅ ممتازة:
"أهلاً 👋 وصلتك من إعلان *خصم الصيف 25%*

اكتب رقم اللي يهمك:
1️⃣ شوف العرض
2️⃣ أسعار وشحن
3️⃣ أتكلم مع حد"
   → فعل واضح → البوت بيمسك من هنا
```

> 💡 **اربطها بالبوت من ملف 05.** الرسالة الافتتاحية = الحالة `MENU_MAIN`. الفانل جاهز خلاص.

### 10.3 اصطياد الضغطة في الكود

```javascript
// الحدث اللي بيقول "دي ضغطة إعلان — النافذة المجانية اتفتحت"
if (event.referral) {
  await windows.openFEP({
    customerId, phone: event.phone,
    source: event.referral.source_type === 'ad' ? 'ctwa_ad' : 'page_cta',
    sourceRef: event.referral.source_id,        // 🔑 ad_id
    channel: 'official',
  });

  // احفظ ctwa_clid — بدونه مش هتعرف أي إعلان جاب الأوردر
  await db.none(`
    UPDATE customers SET
      ctwa_clid = $2, acquisition_source = 'ctwa',
      official_optin = TRUE, official_optin_at = NOW()
    WHERE id = $1`, [customerId, event.referral.ctwa_clid]);
}
```

> ⚠️ **ضغطة الإعلان = موافقة ضمنية (implied opt-in).** العميل هو اللي بدأ. ده بيحلّك مشكلة الـ opt-in اللي في ملف 07 من جذورها لأصل العملاء الجداد.

### 10.4 قياس الربحية الحقيقية

```sql
-- 💰 السؤال الوحيد المهم: الإعلان ده كسب ولا خسر؟
CREATE VIEW v_ctwa_roi AS
SELECT
  w.source_ref                              AS ad_id,
  COUNT(DISTINCT c.id)                      AS clicks,
  COUNT(DISTINCT o.customer_id)             AS buyers,
  ROUND(100.0 * COUNT(DISTINCT o.customer_id)
        / NULLIF(COUNT(DISTINCT c.id),0), 1) AS conv_pct,
  ROUND(SUM(o.total)::numeric, 2)           AS revenue,
  -- تكلفة الرسائل داخل نافذة FEP = صفر 🎁
  ROUND(COALESCE(SUM(m.cost_billed),0)::numeric, 4) AS msg_cost,
  ROUND(AVG(o.total)::numeric, 2)           AS avg_order
FROM customer_windows w
JOIN customers c        ON c.id = w.customer_id
LEFT JOIN orders o      ON o.customer_id = c.id
                       AND o.created_at BETWEEN w.opened_at AND w.opened_at + INTERVAL '7 days'
LEFT JOIN message_log m ON m.customer_id = c.id
                       AND m.created_at BETWEEN w.opened_at AND w.expires_at
WHERE w.opened_by = 'ctwa_ad'
GROUP BY 1 ORDER BY revenue DESC NULLS LAST;
```

ثم: `revenue - msg_cost - ad_spend` = الربح الحقيقي. لو سالب، وقّف الإعلان — مش تزوّد الميزانية.

### 10.5 معايير حكم بعد 7 أيام

| المقياس | ممتاز | مقبول | وقّف |
|---|---|---|---|
| تكلفة الضغطة | < $0.15 | $0.15–0.40 | > $0.40 |
| ضغطة → محادثة | > 70% | 45–70% | < 45% |
| محادثة → أوردر | > 15% | 7–15% | < 7% |
| تكلفة الأوردر | < 8% من قيمته | 8–20% | > 20% |

> **لو "ضغطة → محادثة" واطية:** المشكلة في **الرسالة الافتتاحية** مش في الإعلان. الناس ضغطت — يعني الإعلان شغال. عدّل الرسالة الأول.

---

## 11. المراقبة اليومية (5 دقايق)

```sql
-- 1️⃣ الصحة العامة على القناتين — آخر 24 ساعة
SELECT channel, meta_category,
       COUNT(*) AS sent,
       ROUND(100.0*COUNT(*) FILTER (WHERE status IN ('delivered','read'))
             /NULLIF(COUNT(*),0),1) AS deliv_pct,
       COUNT(*) FILTER (WHERE status='blocked') AS blocked,
       ROUND(SUM(COALESCE(cost_billed,cost_estimated))::numeric,2) AS cost
FROM message_log
WHERE direction='out' AND created_at > NOW()-INTERVAL '24 hours'
GROUP BY 1,2 ORDER BY 1,2;

-- 2️⃣ 💰 نسبة المجاني — مقياس نجاح الهجين
SELECT * FROM v_hybrid_efficiency ORDER BY day DESC LIMIT 7;

-- 3️⃣ 🚦 قرارات الـ Router — بتكتشف بيها الأخطاء
SELECT route_reason, channel, send_mode, COUNT(*)
FROM message_log
WHERE created_at > NOW()-INTERVAL '24 hours'
GROUP BY 1,2,3 ORDER BY 4 DESC LIMIT 15;

-- 4️⃣ ⚠️ أخطاء الرسمي — 131049 بيقول عن سقف التكرار
SELECT error_code, COUNT(*), MAX(created_at) AS last_seen
FROM message_log
WHERE channel='official' AND error_code IS NOT NULL
  AND created_at > NOW()-INTERVAL '24 hours'
GROUP BY 1 ORDER BY 2 DESC;

-- 5️⃣ 🪟 النوافذ المفتوحة الآن — فرص مجانية
SELECT kind, COUNT(*) AS open_now,
       COUNT(*) FILTER (WHERE expires_at < NOW()+INTERVAL '6 hours') AS closing_soon
FROM customer_windows WHERE expires_at > NOW() GROUP BY 1;
```

### حدود التوقف الفوري

| المقياس | الحد | الفعل |
|---|---|---|
| نسبة تسليم الرسمي | < 90% | راجع القوالب + الجودة |
| جودة الرقم الرسمي | `RED` | 🛑 وقّف التسويق الرسمي 48 ساعة |
| `131049` بيزيد | > 5% من التسويق | خفّض تكرار الحملات |
| نسبة المجاني | < 60% | الـ Router غلط أو محتاج CTWA |
| تكلفة/أوردر | > 20% من القيمة | 🛑 راجع الاقتصاد كله |
| جلسات غير رسمية سليمة | < 2 | 🛑 وقّف الحملة، ابدأ warmup |
| التكلفة اليومية | > الحد | أوتوماتيك — `CostGuard` |

### `make panic` الهجين

```makefile
panic:
	@echo "🚨 إيقاف طارئ — القناتين"
	@node -e "require('./src/core/kill-switch.js').activate('manual_panic')"
	@redis-cli SET policy:official:disabled 1
	@redis-cli SET policy:unofficial:disabled 1
	@node -e "require('./src/jobs/queues.js').pauseAll()"
	@echo "✅ اتوقف كل حاجة. الطوابير محفوظة."
	@echo "   للاستئناف: make resume"

resume:
	@redis-cli DEL policy:official:disabled policy:unofficial:disabled
	@node -e "require('./src/core/kill-switch.js').deactivate()"
	@node -e "require('./src/jobs/queues.js').resumeAll()"
```

---

## 12. الأخطاء الشائعة في التنفيذ الهجين

| # | الغلطة | النتيجة | الصح |
|---|---|---|---|
| 1 | قاعدتين بيانات | داتا متضاربة، opt-out مش شغال | قاعدة واحدة |
| 2 | نقل رقم شغّال للرسمي | ✝️ خسرت الرقم وتاريخه | رقم جديد |
| 3 | نسيت `X-Hub-Signature-256` | أي حد يزوّر أحداث | تحقق دايماً |
| 4 | ملقطتش `referral` | ضيّعت نوافذ FEP المجانية | §10.3 |
| 5 | `cost_estimated` بدل `cost_billed` | أرقامك غلط (الفاتورة على التسليم) | من webhook |
| 6 | 20 قالب من الأول | رفوضات كتير، تقييم أسوأ | 3-5 عامّة |
| 7 | متغيرات بدون `example` | رفض شبه مؤكد | حط أمثلة |
| 8 | `abandoned_cart` كـ utility | Meta تعيد التصنيف وتحاسبك MARKETING | صنّفه تسويقي |
| 9 | fallback تسويقي لغير الرسمي | حرقت أرقامك | `allowMarketingFallback=false` |
| 10 | `if (channel===...)` في البوت | صيانة جحيم | Provider abstraction |
| 11 | نقل العميل من رقم لرقم وسط الأوردر | فقدت 20-40% | خلّيه في نافذة FEP |
| 12 | مفيش `idempotencyKey` | العميل استلم مرتين | إجباري |
| 13 | cache نوافذ بـ TTL طويل | 131047 وضياع محاولات | ≤ 5 دقايق |
| 14 | `tier` بتوقيت محلي | فوجعة عند التصفير | UTC |
| 15 | حد التكلفة بيوقف المعاملات | عملاء متضايقين بلا داعي | التسويق بس |
| 16 | تشغيل هجين 100% من أول يوم | مش عارف الأعطال من فين | 5%→25%→60%→100% |
| 17 | `concurrency > 1` على جلسة | نمط غير بشري → حظر | 1 + زوّد الجلسات |
| 18 | متبعتش `free_pct` | بتدفع أكتر من اللازم وانت مش واخد بالك | راقبه يومياً |

---

## 13. قائمة تحقّق نهائية

**قبل أول رسالة رسمية حقيقية:**
- [ ] التحقق من الأعمال خلص
- [ ] رقم **جديد** (مش رقم شغّال)
- [ ] webhook بيتحقق من التوقيع
- [ ] شكل payload الحقيقي محفوظ ومفهوم
- [ ] 3 قوالب utility معتمدة على الأقل
- [ ] `lintTemplate()` صفر تحذيرات
- [ ] `hybrid-dry-run.js` 100%
- [ ] `CostGuard` مجرّب بحد وهمي
- [ ] `make panic` بيوقف القناتين
- [ ] جدول أسعار مصر **مليته بنفسك** من مصدر Meta الرسمي

**قبل أول حملة هجينة:**
- [ ] `planCampaign()` معاينة راجعتها بعينك
- [ ] التكلفة المتوقعة مقبولة
- [ ] نسبة المجاني > 70%
- [ ] `resolveWithFallback` مجرّب بإطفاء قناة
- [ ] الكناري على القناتين عدّى
- [ ] كل الجلسات غير الرسمية خلّصت warmup 21 يوم

**أسبوعياً:**
- [ ] `v_hybrid_efficiency` — `free_pct` مستقر؟
- [ ] `v_ctwa_roi` — الإعلانات رابحة؟
- [ ] جودة الرقم الرسمي `GREEN`؟
- [ ] `131049` مش بيزيد؟
- [ ] راجع `route_reason` — فيه قرار غريب؟

---

## 📚 الخلاصة

```
النظام الهجين مش "الأحسن دايماً" — هو الأحسن لو:

✅ عندك قاعدة عملاء كبيرة (> 5,000)
✅ فيها نسبة كبيرة بتردّ (محادثات كتير)
✅ بتصرف على إعلانات (CTWA يخليه ذهب)
✅ عندك مطوّر يصون 3 أضعاف التعقيد

لو مش كده — النظام الرسمي لوحده اختيار محترم وأبسط.
والنظام غير الرسمي لوحده اختيار مخاطر عالية لكن تكلفته صفر.

القرار مش تقني. اقتصادي.
```

| الملف | الدور |
|---|---|
| [`08-HYBRID-OVERVIEW.md`](./08-HYBRID-OVERVIEW.md) | الاقتصاد والقرار — **اقرأه الأول** |
| [`09-HYBRID-ARCHITECTURE.md`](./09-HYBRID-ARCHITECTURE.md) | التصميم والمكوّنات |
| **`10-HYBRID-IMPLEMENTATION.md`** | الكود والهجرة والتشغيل (الملف الحالي) |
| [`06-IMPLEMENTATION.md`](./06-IMPLEMENTATION.md) | 🔴 **المرحلة صفر** — لازم تخلّصها الأول |
| [`07-RISKS-LEGAL.md`](./07-RISKS-LEGAL.md) | المخاطر والقانون وخطة الطوارئ |

> ⚠️ **تنبيه دائم:** كل الأسعار والحدود وأسماء الحقول في الملف ده **بتتغيّر**. Meta غيّرت نموذج الفاتورة بالكامل في يوليو 2025، وفيه تغيير تاني في أكتوبر 2026. **أكّد كل رقم من مصدر Meta الرسمي قبل ما تبني عليه قرار مالي.**
