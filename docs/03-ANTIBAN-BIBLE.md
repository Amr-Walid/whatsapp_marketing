# 🛡️ كتاب تجنب الحظر (The Anti-Ban Bible)

> **الملف الأهم في الدليل.** كل إشارة بترصدها خوارزميات Meta، وإزاي تتعامل معاها.
> اقرأه كامل قبل ما تبعت رسالة واحدة.

---

## 🧠 إزاي Meta بترصدك؟ (نموذج التهديد)

Meta مش بتستخدم قاعدة واحدة. عندها **نموذج تعلم آلي** بيجمّع إشارات كتير وبيحسب **Risk Score**. لما السكور يعدّي حد معين → حظر.

### الإشارات مرتبة بالخطورة (من الأعلى للأقل)

```
🔴🔴🔴 الطبقة القاتلة — بلاغ واحد كفاية أحياناً
────────────────────────────────────────────────
1. User Spam Reports        ← 3-5 بلاغات = موت فوري
2. Block Rate               ← نسبة اللي بيبلوكوك
3. Zero Reply Ratio         ← بتبعت ومحدش بيرد

🔴🔴 الطبقة الخطيرة جداً
────────────────────────────────────────────────
4. Cold Contact Velocity    ← عدد الغرباء اللي كلمتهم/ساعة
5. Message Uniformity       ← نفس النص لناس كتير
6. Robotic Timing           ← فترات ثابتة رياضياً
7. New Number + High Volume ← رقم عمره يوم وبيبعت 200

🔴 الطبقة المهمة
────────────────────────────────────────────────
8. IP Reputation            ← Datacenter IP
9. Account Clustering       ← أرقام كتير من نفس IP/بصمة
10. Link Density            ← رابط في كل رسالة
11. Device Fingerprint Churn ← بصمة بتتغير كل مرة
12. Session Instability     ← QR متكرر، انقطاعات
13. Delivery Failure Rate   ← رسائل مش بتتوصل (soft ban)

🟡 الطبقة المساعدة
────────────────────────────────────────────────
14. No Profile Picture / Name
15. Circadian Anomaly       ← بتبعت 4 صباحاً
16. Zero Inbound Activity   ← ملكش تفاعل داخل خالص
17. Bulk Group Operations
18. Media Absence           ← نص خالص، مفيش صور/صوت
```

---

## 🔴 الطبقة القاتلة: البلاغات (Spam Reports)

### الحقيقة الصادمة

```
كل التكتيكات التقنية (بروكسي + delay + spintax + fingerprint)
تحميك من إشارة "أنت بوت".

لكنها لا تحميك من إشارة "أنت مزعج".

و Meta بتثق في المستخدم أكتر من أي خوارزمية.
بلاغ من مستخدم حقيقي = دليل قاطع عندها.
```

### إزاي تمنع البلاغات؟ (بترتيب الأهمية)

#### 1️⃣ متبعتش لغريب — أبداً

```
❌ قوائم مشتراة
❌ أرقام مسحوبة من جروبات
❌ أرقام من صفحات فيسبوك
❌ أرقام عشوائية بنمط (010xxxx001, 010xxxx002...)

✅ عملاء اشتروا منك فعلاً
✅ عملاء ملأوا فورم عندك
✅ عملاء كلموك على الواتساب قبل كده
✅ عملاء وافقوا صريحاً على استلام العروض
```

#### 2️⃣ خلّي أول رسالة "متذكِّرة" مش "بائعة"

الرسالة الأولى وظيفتها **تفكّر العميل مين انت**، مش تبيع.

```
❌ رسالة تستدعي البلاغ:
"🔥🔥 عرض مجنون!! خصم 70% لفترة محدودة!!
اطلب الآن من الرابط ده 👇 https://bit.ly/xyz
لا تفوت الفرصة!!! ⏰⏰⏰"

المشاكل: مين انت؟ + إيموجي مبالغ + رابط مختصر مشبوه
        + إلحاح + مفيش سياق + مفيش opt-out


✅ رسالة آمنة:
"أهلاً أحمد 👋
أنا سارة من [اسم متجرك] — كنت اشتريت منّنا [المنتج]
في [الشهر].

نزّلنا [المنتج المكمّل] وافتكرتك، لأنه بيكمّل اللي
اخدته. حابب أبعتلك الصور والسعر؟

لو مش مهتم، ابعت "قف" ومش هزعجك تاني 🙏"

الأسباب: هوية واضحة + سياق شخصي حقيقي + سؤال (مش أمر)
        + opt-out صريح + بدون روابط في أول رسالة
```

#### 3️⃣ الرابط في الرسالة الثانية، مش الأولى

```
الرسالة 1: تعريف + سياق + سؤال (بدون رابط)
     ↓ العميل يرد "أيوه" / "ابعت"
الرسالة 2: التفاصيل + الرابط

✨ الفايدة المزدوجة:
   • Reply Ratio بيرتفع (إشارة إيجابية قوية جداً)
   • مفيش Link Density في cold messages
   • العميل بقى "مهتم فعلاً" → احتمال البلاغ ≈ 0
   • التحويل أعلى بكتير
```

#### 4️⃣ opt-out واضح في كل رسالة outbound

```javascript
const OPT_OUT_LINES = [
  'لو مش مهتم، ابعت "قف" 🙏',
  'ابعت "قف" لو حابب توقف الرسائل.',
  'مش عايز رسائل؟ ابعت "قف" وهحترم طلبك فوراً.',
  'لإيقاف الرسائل: ابعت "قف"',
];

// اختار عشوائي (تنويع + بيمنع الرصد)
function withOptOut(body) {
  const line = OPT_OUT_LINES[
    Math.floor(Math.random() * OPT_OUT_LINES.length)
  ];
  return `${body}\n\n${line}`;
}
```

#### 5️⃣ رد فوري على أي "قف"

```javascript
// أعلى أولوية في النظام — يتخطى كل الطوابير والتأخيرات
async function handleOptOutImmediate(phone, sessionId) {
  // 1. suppress فوراً
  await db.suppress(phone, 'user_opt_out');

  // 2. امسح كل الرسائل المجدولة
  await queue.removeJobsByPhone(phone);

  // 3. رد مهذب — تخطى الـ rate limiter
  await sendMessageBypassLimits(sessionId, phone,
    'تم ✅ مش هتوصلك رسائل تاني. نعتذر عن الإزعاج 🙏'
  );

  // 4. تنبيه لو النسبة بتزيد
  const rate = await getOptOutRate(sessionId, '24h');
  if (rate > 0.03) {  // أكثر من 3%
    await alert(`🟠 opt-out rate = ${(rate*100).toFixed(1)}% على ${sessionId}
النص أو الجمهور فيه مشكلة. وقّف وراجع.`);
    await pauseSession(sessionId);
  }
}
```

---

## 🔴 نسبة الرد (Reply Ratio) — الإشارة الذهبية

### ليه دي أهم مقياس؟

```
واتساب مصمّم للمحادثات الثنائية.
حساب بيبعت 100 رسالة ويستقبل 0 = مش محادثة، دي broadcast.
حساب بيبعت 100 ويستقبل 30 = محادثة طبيعية.

نموذج Meta بيوزّن الإشارة دي بشدة.
```

### الحدود العملية

| Reply Ratio | التقييم | الإجراء |
|---|---|---|
| > 30% | 🟢 ممتاز | كمّل، تقدر تزيد الحجم تدريجياً |
| 15–30% | 🟢 صحي | كمّل بنفس المعدل |
| 8–15% | 🟡 تحذير | قلّل الحجم 50%، راجع النص |
| 3–8% | 🟠 خطر | وقّف الحملة، غيّر النص كلياً |
| < 3% | 🔴 كارثي | **وقّف فوراً** — الحظر جاي |

### كود المراقبة

```javascript
class ReplyRatioGuard {
  constructor(db, opts = {}) {
    this.db = db;
    this.minRatio        = opts.minRatio ?? 0.10;
    this.minSampleSize   = opts.minSampleSize ?? 20;
    this.warnRatio       = opts.warnRatio ?? 0.15;
  }

  async check(sessionId, windowHours = 48) {
    const { sent, replied } = await this.db.one(`
      SELECT
        COUNT(*) FILTER (WHERE direction = 'out')                   AS sent,
        COUNT(DISTINCT phone) FILTER (WHERE direction = 'in')       AS replied
      FROM message_log
      WHERE session_id = $1
        AND created_at > NOW() - ($2 || ' hours')::interval
    `, [sessionId, windowHours]);

    if (sent < this.minSampleSize) {
      return { ok: true, ratio: null, reason: 'sample صغير' };
    }

    const ratio = replied / sent;

    if (ratio < 0.03) {
      return {
        ok: false, ratio, severity: 'critical',
        action: 'stop_all',
        reason: `🔴 ${(ratio*100).toFixed(1)}% — وقّف كل حاجة`,
      };
    }
    if (ratio < this.minRatio) {
      return {
        ok: false, ratio, severity: 'high',
        action: 'pause_session',
        reason: `🟠 ${(ratio*100).toFixed(1)}% — أقل من الحد`,
      };
    }
    if (ratio < this.warnRatio) {
      return {
        ok: true, ratio, severity: 'warn',
        action: 'reduce_volume_50',
        reason: `🟡 ${(ratio*100).toFixed(1)}% — قلّل الحجم`,
      };
    }
    return { ok: true, ratio, severity: 'healthy' };
  }
}
```

### 🎯 تكتيكات ترفع نسبة الرد

```
1. اسأل سؤال مباشر
   ❌ "شوف العرض ده"
   ✅ "حابب أبعتلك السعر؟"
   ✅ "المقاس اللي اخدته المرة اللي فاتت كان L صح؟"

2. اطلب رد بكلمة واحدة (Micro-commitment)
   ✅ "ابعت 'ايوه' وأبعتلك الصور"
   ✅ "رد بـ 1 لو مهتم، 2 لو مش الوقت المناسب"

3. اذكر معلومة شخصية حقيقية
   ✅ "الطلب اللي كان رقمه #4521"
   ✅ "المنتج اللي اخدته في رمضان"
   → بيوصل رسالة: "ده مش سبام، ده يعرفني"

4. رسالة صوتية (Voice Note) قصيرة
   ✅ 8-15 ثانية بصوت حقيقي
   → نسبة الرد أعلى 3-5 أضعاف
   → إشارة "بشر حقيقي" قوية جداً عند واتساب

5. ابدأ بالسيجمنتس اللي بترد
   → champions و loyal الأول (بيردوا 40%+)
   → ده يبني رصيد ثقة قبل السيجمنتس الصعبة
```

---

## ⏱️ هندسة التوقيت (Timing Engineering)

### ❌ اللي معظم الناس بتعمله (والغلط)

```javascript
// كل الأمثلة دي بتتحظر
await sleep(5000);                              // ثابت — مرصود فوراً
await sleep(10000 + Math.random() * 5000);      // uniform — نمط مسطح مرصود
await sleep(randomInt(10, 30) * 1000);          // نفس المشكلة
```

**السبب:** التوزيع المنتظم (Uniform Distribution) له بصمة إحصائية واضحة. البشر مش بيوزعوا وقتهم بالتساوي — التوزيع البشري **Gaussian مع ذيل طويل** (long tail).

### ✅ محرك التوقيت الصحيح

```javascript
/**
 * توليد رقم عشوائي بتوزيع طبيعي (Box-Muller)
 */
function gaussian(mean, stdDev) {
  let u = 0, v = 0;
  while (u === 0) u = Math.random();
  while (v === 0) v = Math.random();
  const z = Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v);
  return mean + z * stdDev;
}

/**
 * منحنى النشاط اليومي — مضاعف التأخير حسب الساعة
 * أعلى قيمة = أبطأ إرسال
 */
const CIRCADIAN_CURVE = {
  0: 3.5, 1: 4.5, 2: 6.0, 3: 7.0,  4: 6.5,  5: 5.0,   // 🌙 ميت
  6: 3.5, 7: 2.2, 8: 1.4, 9: 1.0, 10: 0.95, 11: 0.9,  // ☀️ صعود
 12: 1.3, 13: 1.5, 14: 1.1, 15: 1.0, 16: 0.95, 17: 0.9,// 🍽️ غداء ثم نشاط
 18: 0.85, 19: 0.9, 20: 1.0, 21: 1.2, 22: 1.8, 23: 2.5,// 🌆 المساء
};

/**
 * مضاعف حسب علاقتك بالعميل
 */
const CONTACT_RISK = {
  stranger:            2.6,  // غريب تماماً — أبطأ ما يكون
  handshake_sent:      1.9,  // بعتنا وهو ملردش
  handshake_complete:  1.3,  // رد مرة
  known:               1.0,  // بيتكلم معانا
  active_conversation: 0.6,  // في محادثة نشطة الآن
};

class DelayEngine {
  constructor(opts = {}) {
    this.baseMean   = opts.baseMean   ?? 45_000;
    this.baseStdDev = opts.baseStdDev ?? 18_000;
    this.minDelay   = opts.minDelay   ?? 12_000;
    this.maxDelay   = opts.maxDelay   ?? 300_000;
    this.tz         = opts.timezone   ?? 'Africa/Cairo';
  }

  localHour() {
    return parseInt(
      new Intl.DateTimeFormat('en-US', {
        hour: 'numeric', hour12: false, timeZone: this.tz,
      }).format(new Date()), 10
    ) % 24;
  }

  compute({ contactState = 'stranger', messageLength = 100, sentInHour = 0 }) {
    // 1. الأساس Gaussian
    let d = gaussian(this.baseMean, this.baseStdDev);

    // 2. منحنى النشاط اليومي
    d *= CIRCADIAN_CURVE[this.localHour()];

    // 3. مخاطرة جهة الاتصال
    d *= CONTACT_RISK[contactState] ?? 2.0;

    // 4. محاكاة الكتابة (WPM)
    //    45 كلمة/دقيقة ≈ 4 أحرف/ثانية
    const wpm = gaussian(42, 12);
    const charsPerSec = Math.max(1.5, (wpm * 5) / 60);
    d += (messageLength / charsPerSec) * 1000;

    // 5. وقفة تفكير (8% احتمال)
    if (Math.random() < 0.08) {
      d += 800 + Math.random() * 2700;
    }

    // 6. تشتت (5% احتمال — العميل رفع تليفونه وسابه)
    if (Math.random() < 0.05) {
      d += (5 + Math.random() * 15) * 60_000;  // 5-20 دقيقة
    }

    // 7. غياب مفاجئ (2.5% — قام من مكانه)
    if (Math.random() < 0.025) {
      d += (10 + Math.random() * 30) * 60_000; // 10-40 دقيقة
    }

    // 8. تباطؤ تلقائي مع تكدس الساعة
    if (sentInHour > 12) d *= 1 + (sentInHour - 12) * 0.12;

    return Math.round(
      Math.min(this.maxDelay, Math.max(this.minDelay, d))
    );
  }

  /** هل الوقت الحالي مناسب للإرسال؟ */
  isSendWindow() {
    const h = this.localHour();
    // 🚫 متبعتش من 11م لـ 8ص
    if (h >= 23 || h < 8) return { ok: false, reason: 'وقت نوم' };
    // 🟡 وقت الصلاة/الغداء — بطّئ
    if (h === 12 || h === 13) return { ok: true, slow: true };
    return { ok: true };
  }
}
```

### نظام الدفعات (Batching) والاستراحات

```javascript
class BatchScheduler {
  constructor(opts = {}) {
    this.batchMin      = opts.batchMin      ?? 12;
    this.batchMax      = opts.batchMax      ?? 22;
    this.restMinMs     = opts.restMinMs     ?? 25 * 60_000;
    this.restMaxMs     = opts.restMaxMs     ?? 90 * 60_000;
    this.longRestEvery = opts.longRestEvery ?? 3;   // كل 3 دفعات
    this.longRestMs    = opts.longRestMs    ?? 3 * 3600_000;

    this.sentInBatch = 0;
    this.batchCount  = 0;
    this.batchTarget = this.nextTarget();
  }

  nextTarget() {
    return Math.floor(
      this.batchMin + Math.random() * (this.batchMax - this.batchMin)
    );
  }

  onSent() {
    this.sentInBatch++;
    if (this.sentInBatch < this.batchTarget) return null;

    // الدفعة خلصت
    this.batchCount++;
    this.sentInBatch = 0;
    this.batchTarget = this.nextTarget();

    // استراحة طويلة كل N دفعات
    if (this.batchCount % this.longRestEvery === 0) {
      const rest = this.longRestMs * (0.75 + Math.random() * 0.5);
      return { rest: Math.round(rest), type: 'long' };
    }

    const rest = this.restMinMs +
      Math.random() * (this.restMaxMs - this.restMinMs);
    return { rest: Math.round(rest), type: 'short' };
  }
}
```

### مثال حساب: كام رسالة/يوم فعلياً؟

```
نافذة الإرسال: 8ص → 11م = 15 ساعة = 900 دقيقة

متوسط delay فعلي (مع كل المضاعفات) ≈ 90 ثانية
استراحات قصيرة: 900/18 ≈ 50 دفعة... لأ استنى

الحساب الصح:
  دفعة = 17 رسالة × 90 ثانية = 25.5 دقيقة
  استراحة قصيرة = 55 دقيقة
  دورة = 80 دقيقة → 17 رسالة

  900 دقيقة ÷ 80 = 11 دورة
  ناقص 2 استراحة طويلة (3 ساعات × 2 = 360 دقيقة)

  (900 - 360) ÷ 80 = 6.75 دورة
  6.75 × 17 ≈ 115 رسالة/يوم/رقم

✅ ده رقم آمن لرقم دافي.
   8 أرقام × 115 = 920 رسالة/يوم
   5000 عميل ÷ 920 = 5.5 يوم للحملة
```

---

## 🔥 التدفئة (Warm-up) — الجزء اللي محدش بيصبر عليه

### ليه دي حرجة؟

```
رقم عمره 3 أيام + 200 رسالة outbound = حظر مؤكد 100%
رقم عمره 3 شهور + 200 رسالة outbound = مخاطرة متوسطة

الوقت والتاريخ (Account Age & History) من أقوى إشارات الثقة.
مفيش تكتيك تقني بيعوّض غياب التاريخ.
```

### جدول التدفئة الكامل (21 يوم)

#### 🔹 المرحلة 1: الأيام 1-3 — التسجيل والحياة الأساسية
```
✅ سجّل الرقم على تطبيق واتساب حقيقي على موبايل حقيقي
   (مش emulator، مش VPS، مش Baileys)
✅ ضيف صورة بروفايل حقيقية (لوجو شركتك أو صورة شخص)
✅ ضيف اسم واضح: "سارة - [متجرك]"
✅ ضيف About/Status نصي
✅ حافظ على الموبايل شغّال ومتصل بشبكة موبايل حقيقية

❌ 0 رسائل آلية
❌ متربطوش بـ Baileys/Evolution خالص
```

#### 🔹 المرحلة 2: الأيام 4-7 — نشاط بشري حقيقي
```
✅ ابعت 10-20 رسالة يدوياً لناس تعرفهم
   (زملاء، أصحاب، أرقام فريقك)
✅ استقبل ردود حقيقية — المهم يبقى في تبادل ثنائي
✅ انضم لـ 2-3 جروبات حقيقية وتكلم فيها
✅ حدّث الـ Status (صورة/نص) مرة أو مرتين
✅ ابعت 2-3 رسائل صوتية
✅ ابعت وستقبل صور
✅ اعمل مكالمة واتساب واحدة (صوتية) — إشارة قوية جداً

❌ لسه مفيش أتمتة
```

#### 🔹 المرحلة 3: الأيام 8-11 — أول ربط
```
✅ اربط الرقم بـ Baileys/Evolution (Linked Device)
   ⚠️ سيب الموبايل الأصلي شغّال! متشيلوش
✅ استخدمه للرد على رسائل واردة بس (Inbound only)
✅ 5-10 رسائل خارجة كحد أقصى — لناس معروفة
✅ شغّل HumanEntropyService (typing/presence/read في الخلفية)

❌ 0 رسائل لغرباء
```

#### 🔹 المرحلة 4: الأيام 12-16 — أول Outbound محكوم
```
يوم 12: 8 رسائل    — لعملاء champions (أعلى احتمال رد)
يوم 13: 12 رسالة   — champions
يوم 14: 18 رسالة   — champions + loyal
يوم 15: 25 رسالة   — loyal
يوم 16: 35 رسالة   — loyal

✅ راقب Reply Ratio كل يوم — لو أقل من 20% وقّف وراجع
✅ delay مش أقل من 60 ثانية
✅ رد على كل من يرد فوراً وبشكل حقيقي
```

#### 🔹 المرحلة 5: الأيام 17-21 — التوسّع
```
يوم 17: 45 رسالة
يوم 18: 55 رسالة
يوم 19: 70 رسالة
يوم 20: 85 رسالة
يوم 21: 100 رسالة

✅ راقب: delivery rate > 90%، reply ratio > 15%
✅ لو أي مقياس نزل → ارجع لليوم اللي قبله واستقر
```

#### 🔹 بعد اليوم 21 — سرعة التشغيل
```
✅ 100-140 رسالة/يوم كحد أقصى مستدام
🚫 متعدّيش 150 أبداً، مهما كان الرقم "دافي"
```

### كود الـ Warmup Scheduler

```javascript
const WARMUP_PLAN = [
  // day, maxOut, allowAutomation, minDelayMs, audience
  { d: 1,  out: 0,   auto: false, delay: 0,       aud: null },
  { d: 2,  out: 0,   auto: false, delay: 0,       aud: null },
  { d: 3,  out: 0,   auto: false, delay: 0,       aud: null },
  { d: 4,  out: 5,   auto: false, delay: 0,       aud: 'manual_friends' },
  { d: 5,  out: 5,   auto: false, delay: 0,       aud: 'manual_friends' },
  { d: 6,  out: 6,   auto: false, delay: 0,       aud: 'manual_friends' },
  { d: 7,  out: 6,   auto: false, delay: 0,       aud: 'manual_friends' },
  { d: 8,  out: 5,   auto: true,  delay: 180_000, aud: 'known' },
  { d: 9,  out: 6,   auto: true,  delay: 180_000, aud: 'known' },
  { d: 10, out: 8,   auto: true,  delay: 150_000, aud: 'known' },
  { d: 11, out: 10,  auto: true,  delay: 150_000, aud: 'known' },
  { d: 12, out: 8,   auto: true,  delay: 120_000, aud: 'champions' },
  { d: 13, out: 12,  auto: true,  delay: 110_000, aud: 'champions' },
  { d: 14, out: 18,  auto: true,  delay: 100_000, aud: 'champions' },
  { d: 15, out: 25,  auto: true,  delay:  95_000, aud: 'loyal' },
  { d: 16, out: 35,  auto: true,  delay:  90_000, aud: 'loyal' },
  { d: 17, out: 45,  auto: true,  delay:  85_000, aud: 'loyal' },
  { d: 18, out: 55,  auto: true,  delay:  80_000, aud: 'any' },
  { d: 19, out: 70,  auto: true,  delay:  75_000, aud: 'any' },
  { d: 20, out: 85,  auto: true,  delay:  70_000, aud: 'any' },
  { d: 21, out: 100, auto: true,  delay:  65_000, aud: 'any' },
];

class WarmupScheduler {
  constructor(db) { this.db = db; }

  async getLimits(sessionId) {
    const s = await this.db.one(
      `SELECT registered_at, health_status FROM sessions WHERE id = $1`,
      [sessionId]
    );

    const days = Math.floor(
      (Date.now() - new Date(s.registered_at)) / 86400_000
    ) + 1;

    // بعد 21 يوم
    if (days > 21) {
      return {
        day: days, maxOut: 130, auto: true,
        minDelay: 60_000, audience: 'any', mature: true,
      };
    }

    const plan = WARMUP_PLAN[days - 1];

    // 🔻 لو الصحة مش تمام، ارجع خطوة
    if (s.health_status === 'degraded') {
      const back = WARMUP_PLAN[Math.max(0, days - 4)];
      return {
        day: days, maxOut: Math.floor(back.out * 0.5),
        auto: back.auto, minDelay: back.delay * 1.6,
        audience: back.aud, throttled: true,
      };
    }

    return {
      day: days, maxOut: plan.out, auto: plan.auto,
      minDelay: plan.delay, audience: plan.aud,
    };
  }

  async canSend(sessionId, targetSegment) {
    const lim = await this.getLimits(sessionId);

    if (!lim.auto) {
      return { allowed: false, reason: `يوم ${lim.day}: تدفئة يدوية فقط` };
    }

    const sentToday = await this.db.oneValue(`
      SELECT COUNT(*) FROM message_log
      WHERE session_id = $1 AND direction = 'out'
        AND created_at >= CURRENT_DATE
    `, [sessionId]);

    if (sentToday >= lim.maxOut) {
      return { allowed: false, reason: `وصل الحد اليومي (${lim.maxOut})` };
    }

    // فحص الجمهور المسموح
    const AUD_MAP = {
      manual_friends: [],
      known:      ['known'],
      champions:  ['champions'],
      loyal:      ['champions', 'loyal'],
      any:        null,   // الكل
    };
    const allowed = AUD_MAP[lim.audience];
    if (allowed !== null && !allowed.includes(targetSegment)) {
      return {
        allowed: false,
        reason: `يوم ${lim.day}: مسموح "${lim.audience}" بس`,
      };
    }

    return { allowed: true, minDelay: lim.minDelay, quotaLeft: lim.maxOut - sentToday };
  }
}
```

### 🔥 تكتيك متقدم: التدفئة المتبادلة (Cross-Warming)

```javascript
/**
 * تخلّي أرقامك تتكلم مع بعض بشكل طبيعي
 * → بيبني تاريخ محادثات حقيقي + reply ratio 100%
 *
 * ⚠️ متعملهاش بشكل روبوتي! لازم:
 *    - نصوص متنوعة وطبيعية
 *    - أوقات عشوائية
 *    - أحياناً صور/صوت
 *    - محادثات غير متساوية الطول
 */
const WARM_TALK = [
  'ازيك عامل ايه', 'الحمد لله تمام وانت', 'كل سنة وانت طيب',
  'شوفت الماتش امبارح؟', 'ايوه يا عم كانت وحشة 😂',
  'بعتلك الملف على الميل', 'تمام هشوفه دلوقتي',
  'الاجتماع الساعة كام؟', '٣ باذن الله', 'ماشي',
  'تعالى نشرب قهوة', 'يلا 👍', 'انا نازل بعد شوية',
  'الطلب وصل؟', 'ايوه وصل الحمد لله', 'تسلم',
];

async function crossWarm(sessions) {
  // كل يوم، اعمل 3-6 محادثات عشوائية بين الأرقام
  const convoCount = 3 + Math.floor(Math.random() * 4);

  for (let i = 0; i < convoCount; i++) {
    const [a, b] = shuffle(sessions).slice(0, 2);

    // محادثة من 2-6 رسائل متبادلة
    const turns = 2 + Math.floor(Math.random() * 5);
    let sender = a, receiver = b;

    for (let t = 0; t < turns; t++) {
      const msg = WARM_TALK[Math.floor(Math.random() * WARM_TALK.length)];

      // 20% احتمال صورة بدل نص
      if (Math.random() < 0.2) {
        await sendRandomImage(sender.id, receiver.phone);
      } else {
        await sendWithTyping(sender.id, receiver.phone, msg);
      }

      [sender, receiver] = [receiver, sender];  // تبديل
      await sleep(15_000 + Math.random() * 120_000);
    }

    // فاصل بين المحادثات
    await sleep(30 * 60_000 + Math.random() * 120 * 60_000);
  }
}
```

---

## 🔀 تنويع النصوص (Content Variation)

### المشكلة
```
نفس النص بالحرف × 100 رسالة = بصمة نصية (Content Hash)
Meta بتحسب hash للنص وبتعد كم مرة اتبعت.
```

### الحل الأساسي: Spintax

```javascript
/**
 * محرك Spintax متداخل
 * {أهلاً|مرحباً|ازيك} {name}، {عندنا|نزّلنا} {عرض|خصم}
 */
function spin(template) {
  const re = /\{([^{}]+)\}/;
  let out = template;
  let guard = 0;

  while (re.test(out) && guard++ < 60) {
    out = out.replace(re, (_, group) => {
      const opts = group.split('|');
      return opts[Math.floor(Math.random() * opts.length)];
    });
  }
  return out;
}

// قالب حقيقي
const TEMPLATE = `
{أهلاً|أهلا|ازيك|مساء الخير} {{name}} {👋|🌸|}

{أنا|معاك} {{agent}} من {{store}}.
{كنت اشتريت|اشتريت|أخدت} منّنا {{last_product}} {في|} {{last_month}}
{و افتكرتك|و جالي في بالي|و قلت أقولك} {لما|بعد ما} {نزّلنا|وصلنا} {{new_product}}.

{هو|ده} {بيكمّل|مناسب جداً مع} {اللي اخدته|طلبك السابق}
{وفي عليه|و عليه} {خصم|عرض} {{discount}}% {لعملاءنا|ليك|للعملاء القدام}.

{حابب|تحب|عايز} {أبعتلك|أشوفلك} {الصور والسعر|التفاصيل|السعر}?
`;

// ⚠️ مهم: تحقق إن مفيش تكرار
function generateUnique(template, vars, existingHashes) {
  for (let attempt = 0; attempt < 25; attempt++) {
    let text = spin(template);
    for (const [k, v] of Object.entries(vars)) {
      text = text.replaceAll(`{{${k}}}`, v);
    }
    text = text.replace(/\s+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim();

    const h = hash(text);
    if (!existingHashes.has(h)) {
      existingHashes.add(h);
      return text;
    }
  }
  throw new Error('❌ مفيش تنويع كافي في القالب — زوّد الخيارات');
}
```

### حساب التنويع

```javascript
function variationCount(template) {
  let count = 1;
  const re = /\{([^{}]+)\}/g;
  let m;
  while ((m = re.exec(template))) {
    count *= m[1].split('|').length;
  }
  return count;
}

// ✅ القاعدة: لازم التنويع ≥ 20 × عدد المستلمين
const needed = recipients.length * 20;
const available = variationCount(TEMPLATE);
if (available < needed) {
  console.error(`❌ التنويع ${available} أقل من المطلوب ${needed}`);
}
```

### 🔥 تنويع متقدم — بعيد عن Spintax

```javascript
// 1. تنويع البنية (مش الكلمات بس)
const STRUCTURES = [
  (v) => `أهلاً ${v.name} 👋\n\nأنا ${v.agent} من ${v.store}. ${v.body}\n\n${v.cta}`,
  (v) => `${v.name}، مساء الخير 🌸\n\n${v.body}\n\n— ${v.agent}، ${v.store}\n${v.cta}`,
  (v) => `ازيك ${v.name}؟\n${v.store} معاك.\n\n${v.body}\n${v.cta}`,
  (v) => `${v.body}\n\n(${v.agent} من ${v.store})\n\n${v.cta}`,
];

// 2. تنويع نوع الوسيط
const MEDIA_MIX = [
  { type: 'text',  weight: 0.55 },
  { type: 'image', weight: 0.25 },  // صورة المنتج + caption
  { type: 'audio', weight: 0.12 },  // رسالة صوتية 8-15 ثانية
  { type: 'video', weight: 0.05 },  // فيديو قصير
  { type: 'doc',   weight: 0.03 },  // كاتالوج PDF
];

// 3. تنويع الإيموجي (مواضع مختلفة، أو بدون)
function varyEmoji(text) {
  const r = Math.random();
  if (r < 0.25) return text;                        // بدون خالص
  if (r < 0.50) return text.replace(/👋|🌸/g, '');  // شيل بعضهم
  return text;
}

// 4. تنويع الأخطاء البشرية (2-3% من الرسائل)
async function withHumanTypo(sessionId, phone, text) {
  if (Math.random() > 0.025) {
    return sendWithTyping(sessionId, phone, text);
  }

  // ابعت النص وفيه غلطة
  const words = text.split(' ');
  const i = Math.floor(Math.random() * words.length);
  const typo = [...words];
  typo[i] = introduceTypo(words[i]);

  await sendWithTyping(sessionId, phone, typo.join(' '));
  await sleep(2500 + Math.random() * 4000);
  // ثم التصحيح
  await sendWithTyping(sessionId, phone, `*${words[i]}`);
}

function introduceTypo(word) {
  if (word.length < 3) return word;
  const i = 1 + Math.floor(Math.random() * (word.length - 2));
  const arr = [...word];
  [arr[i], arr[i+1]] = [arr[i+1], arr[i]];  // بدّل حرفين
  return arr.join('');
}
```

---

## 🔗 كثافة الروابط (Link Density)

### القواعد

```
🔴 رابط في كل رسالة outbound          → إشارة spam قوية
🔴 روابط مختصرة (bit.ly, tinyurl)      → مرصودة، مشبوهة جداً
🔴 نفس الرابط لـ 500 شخص              → بصمة واضحة
🟡 دومين جديد (عمره أيام)             → تحذير
🟢 دومينك الخاص + عمره شهور           → آمن
🟢 مفيش رابط في أول رسالة             → الأفضل
```

### الحل الصحيح

```javascript
// ❌ الغلط
const link = 'https://bit.ly/myoffer';

// ✅ الصح — دومينك + مسار فريد لكل عميل
function buildLink(customer, campaign) {
  const token = signJWT({ cid: customer.id, cmp: campaign.id }, { expiresIn: '14d' });
  return `https://shop.yourdomain.com/o/${token}`;
}

// ✅ الأفضل — Subdomain مخصص للحملات
// campaigns.yourdomain.com  ← منفصل عن الموقع الرئيسي
// لو اتحرق، الموقع الرئيسي سليم
```

### استراتيجية "الرابط في الرسالة الثانية"

```javascript
// المرحلة 1: تعريف بدون رابط
await send(phone, spin(`
{أهلاً|ازيك} {{name}} 👋
أنا {{agent}} من {{store}}. {اشتريت|أخدت} منّنا {{product}} قبل كده.

{نزّلنا|وصلنا} {{new_product}} و{افتكرتك|قلت أقولك}.
{حابب|تحب} {أبعتلك|أشوفلك} {التفاصيل|الصور والسعر}?

لو مش مهتم ابعت "قف" 🙏
`));

// المرحلة 2: بعد ما يرد "أيوه" — ابعت الرابط
onReply(phone, async (text) => {
  if (isPositive(text)) {
    // ✅ الرابط هنا آمن تماماً — العميل طلبه
    await sendImage(phone, productImage, spin(`
{ده|أهو} {{new_product}} 📦
السعر: {{price}} جنيه ({بعد الخصم|خصم {{discount}}%})

{تقدر تطلب من|اطلب من} هنا:
{{link}}

{أو|ولو تحب} {اطلب من هنا|قولي وأنا أسجّل الأوردر} على طول 👍
`));
  }
});
```

---

## 📉 رصد الحظر الناعم (Soft Ban Detection)

قبل الحظر الكامل، واتساب بتعمل **soft ban**: الرسائل بتتبعت لكن مش بتتوصل. لو ملاحظتهاش، هتكمّل إرسال في الفراغ لحد الحظر النهائي.

### الإشارات

```javascript
class SoftBanDetector {
  constructor(db) {
    this.db = db;
  }

  async scan(sessionId) {
    const m = await this.db.one(`
      SELECT
        COUNT(*)                                              AS total,
        COUNT(*) FILTER (WHERE status IN ('delivered','read')) AS delivered,
        COUNT(*) FILTER (WHERE status = 'read')                AS read,
        COUNT(*) FILTER (WHERE status = 'sent')                AS stuck,
        COUNT(*) FILTER (WHERE status = 'failed')              AS failed
      FROM message_log
      WHERE session_id = $1 AND direction = 'out'
        AND created_at > NOW() - INTERVAL '3 hours'
    `, [sessionId]);

    if (m.total < 10) return { risk: 'unknown', reason: 'sample صغير' };

    const deliveryRate = m.delivered / m.total;
    const readRate     = m.read / Math.max(1, m.delivered);
    const stuckRate    = m.stuck / m.total;

    const signals = [];

    // 🔴 أخطر إشارة: علامة واحدة (sent) ومش بتتحول لاتنين
    if (stuckRate > 0.35) {
      signals.push({
        sev: 'critical',
        msg: `🔴 ${(stuckRate*100).toFixed(0)}% رسائل عالقة على ✓ واحدة = SOFT BAN`,
      });
    }

    if (deliveryRate < 0.55) {
      signals.push({
        sev: 'critical',
        msg: `🔴 توصيل ${(deliveryRate*100).toFixed(0)}% — حظر ناعم شبه مؤكد`,
      });
    } else if (deliveryRate < 0.80) {
      signals.push({
        sev: 'high',
        msg: `🟠 توصيل ${(deliveryRate*100).toFixed(0)}% — تدهور`,
      });
    }

    if (m.failed / m.total > 0.12) {
      signals.push({
        sev: 'high',
        msg: `🟠 ${m.failed} فشل إرسال`,
      });
    }

    if (deliveryRate > 0.85 && readRate < 0.12) {
      signals.push({
        sev: 'medium',
        msg: `🟡 بتتوصل بس محدش بيفتح — الجمهور غلط`,
      });
    }

    const worst = signals.reduce((a, s) =>
      ({critical:3, high:2, medium:1}[s.sev] > ({critical:3, high:2, medium:1}[a] ?? 0)
        ? s.sev : a), 'healthy');

    return {
      risk: worst,
      metrics: { deliveryRate, readRate, stuckRate, ...m },
      signals,
      action: worst === 'critical' ? 'stop_immediately'
            : worst === 'high'     ? 'pause_24h'
            : worst === 'medium'   ? 'reduce_50'
            : 'continue',
    };
  }
}
```

### أكواد الانقطاع (Disconnect Codes) وتفسيرها

```javascript
const DISCONNECT_MAP = {
  401: { name: 'Logged Out',           fatal: true,  action: 'مسح QR جديد — الجلسة انتهت' },
  403: { name: 'Forbidden / Banned',   fatal: true,  action: '🔴 الرقم اتحظر — استبدله' },
  408: { name: 'Timeout',              fatal: false, action: 'أعد الاتصال بعد 30 ثانية' },
  409: { name: 'Conflict',             fatal: false, action: 'جلسة أخرى شغالة — تحقق' },
  428: { name: 'Connection Replaced',  fatal: false, action: 'جهاز آخر اتصل — تحقق' },
  429: { name: 'Rate Limited',         fatal: false, action: '🟠 وقّف ساعتين على الأقل!' },
  440: { name: 'Session Replaced',     fatal: true,  action: 'مسح QR جديد' },
  500: { name: 'Internal Error',       fatal: false, action: 'أعد الاتصال بـ backoff' },
  503: { name: 'Unavailable',          fatal: false, action: 'سيرفر واتساب — انتظر' },
  515: { name: 'Restart Required',     fatal: false, action: 'أعد التشغيل فوراً' },
};

// ⚠️ الكود 429 هو أهم تحذير — لو ظهر:
async function handle429(sessionId) {
  await alert(`🟠 كود 429 (Rate Limited) على ${sessionId}
ده تحذير رسمي من واتساب.
الإجراء: إيقاف 4 ساعات + استئناف بـ 25% من السرعة + زيادة 25%/أسبوع`);

  await pauseSession(sessionId, 4 * 3600_000);
  await setRateMultiplier(sessionId, 0.25);
  await scheduleRamp(sessionId, { weeklyIncrease: 0.25 });
}
```

---

## 🧬 إشارات "أنا بشر" (Legitimacy Signals)

الحساب اللي بيعمل الحاجات دي بيبان طبيعي أكتر:

### 1. HumanEntropy — نشاط خلفي عشوائي

```javascript
/**
 * كل 2-6 ساعات، اعمل نشاط بشري "عديم الفائدة"
 * لكنه إشارة قوية إنك مش بوت
 */
class HumanEntropy {
  constructor(sock, opts = {}) {
    this.sock = sock;
    this.minMs = opts.minMs ?? 2 * 3600_000;
    this.maxMs = opts.maxMs ?? 6 * 3600_000;
    this.recentContacts = [];  // ⚠️ ناس كلموك انت الأول بس!
    this.timer = null;
  }

  addContact(jid) {
    if (!this.recentContacts.includes(jid)) {
      this.recentContacts.unshift(jid);
      this.recentContacts = this.recentContacts.slice(0, 30);
    }
  }

  start() {
    const schedule = () => {
      const wait = this.minMs + Math.random() * (this.maxMs - this.minMs);
      this.timer = setTimeout(async () => {
        try { await this.act(); } catch {}
        schedule();
      }, wait);
    };
    schedule();
  }

  stop() { clearTimeout(this.timer); }

  async act() {
    if (!this.recentContacts.length) return;
    const jid = this.recentContacts[
      Math.floor(Math.random() * this.recentContacts.length)
    ];

    const action = Math.floor(Math.random() * 3);

    if (action === 0) {
      // ⌨️ "بدأت أكتب وغيّرت رأيي"
      await this.sock.sendPresenceUpdate('composing', jid);
      await sleep(3000 + Math.random() * 5000);
      await this.sock.sendPresenceUpdate('paused', jid);

    } else if (action === 1) {
      // 👁️ قراءة متأخرة (بعد 10-60 دقيقة)
      const key = this.lastKeys?.[jid];
      if (key) await this.sock.readMessages([key]);

    } else {
      // 🟢 "فتح التليفون وقفله"
      await this.sock.sendPresenceUpdate('available');
      await sleep(30_000 + Math.random() * 90_000);
      await this.sock.sendPresenceUpdate('unavailable');
    }
  }
}
```

### 2. Typing Simulation صحيحة

```javascript
async function sendWithTyping(sock, jid, text) {
  // 1. اقرأ آخر رسالة الأول (طبيعي)
  await sock.readMessages([lastKey(jid)]).catch(() => {});
  await sleep(400 + Math.random() * 1200);

  // 2. حساب مدة الكتابة الواقعية
  const wpm = gaussian(42, 12);
  const charsPerSec = Math.max(1.5, (wpm * 5) / 60);
  let remaining = (text.length / charsPerSec) * 1000;

  // 3. كتابة مقطّعة بوقفات تفكير
  while (remaining > 0) {
    const chunk = Math.min(remaining, 3000 + Math.random() * 5000);
    await sock.sendPresenceUpdate('composing', jid);
    await sleep(chunk);
    remaining -= chunk;

    // 8% وقفة تفكير
    if (remaining > 0 && Math.random() < 0.08) {
      await sock.sendPresenceUpdate('paused', jid);
      await sleep(900 + Math.random() * 3000);
    }
  }

  // 4. ابعت
  await sock.sendPresenceUpdate('paused', jid);
  await sleep(200 + Math.random() * 500);
  await sock.sendMessage(jid, { text });
}
```

### 3. متعلنش Online فوراً (Stealth Connect)

```javascript
const sock = makeWASocket({
  auth: state,
  markOnlineOnConnect: false,   // ← 🔑 مهم جداً
  browser: fingerprintFor(sessionId),
  syncFullHistory: false,       // متسحبش كل التاريخ
});

// بعد الاتصال، استنى 45-120 ثانية قبل ما تعلن available
sock.ev.on('connection.update', async ({ connection }) => {
  if (connection === 'open') {
    await sleep(45_000 + Math.random() * 75_000);
    await sock.sendPresenceUpdate('available');
  }
});
```

### 4. البروفايل الكامل

```
✅ صورة بروفايل حقيقية (لوجو أو صورة شخص من فريقك)
✅ اسم واضح ومفهوم: "سارة - متجر كذا"
✅ About/نبذة نصية
✅ Status محدّث من فترة لفترة (مش كل يوم بشكل روبوتي)
❌ بروفايل فاضي = إشارة "حساب مؤقت"
```

### 5. تفاعل داخل (Inbound) طبيعي

```javascript
// الحساب اللي بيبعت بس ومحدش بيكلمه = بوت واضح
// الحل: خلّي فيه inbound حقيقي

// 1. حط الرقم على موقعك / بايو الانستجرام
//    → ناس بتكلمك من نفسها = inbound طبيعي ذهبي

// 2. رد على كل inbound بسرعة وبشكل حقيقي
//    (مش رد آلي واضح!)

// 3. خلي فريقك يستخدم الأرقام دي فعلاً للخدمة
```

---

## 🎛️ نظام التقييم والإيقاف التلقائي (Risk Scoring & Kill Switch)

```javascript
class RiskScorer {
  /**
   * السكور من 0 لـ 100
   * 0-25   🟢 آمن
   * 26-50  🟡 راقب
   * 51-75  🟠 قلّل
   * 76-100 🔴 وقّف
   */
  async score(sessionId) {
    const [reply, soft, health, optOut, blocks, quota, conn] =
      await Promise.all([
        this.replyRatio(sessionId),
        this.softBan(sessionId),
        this.sessionHealth(sessionId),
        this.optOutRate(sessionId),
        this.blockRate(sessionId),
        this.quotaPressure(sessionId),
        this.connectionStability(sessionId),
      ]);

    const factors = [
      // [الاسم, السكور 0-100, الوزن]
      ['reply_ratio',   this.scoreReply(reply),         25],
      ['soft_ban',      this.scoreSoft(soft),           25],
      ['opt_out_rate',  this.scoreOptOut(optOut),       18],
      ['block_rate',    this.scoreBlock(blocks),        14],
      ['session_health',this.scoreHealth(health),        8],
      ['quota',         this.scoreQuota(quota),          5],
      ['connection',    this.scoreConn(conn),            5],
    ];

    const totalWeight = factors.reduce((s, f) => s + f[2], 0);
    const weighted = factors.reduce((s, [, v, w]) => s + v * w, 0) / totalWeight;

    return {
      score: Math.round(weighted),
      level: weighted > 75 ? 'critical'
           : weighted > 50 ? 'high'
           : weighted > 25 ? 'medium' : 'low',
      factors: Object.fromEntries(factors.map(([n, v, w]) => [n, { v, w }])),
      recommendation: this.recommend(weighted),
    };
  }

  scoreReply(r) {
    if (r === null) return 20;
    if (r >= 0.30) return 0;
    if (r >= 0.15) return 15;
    if (r >= 0.08) return 45;
    if (r >= 0.03) return 75;
    return 100;
  }

  scoreSoft(s) {
    return { healthy: 0, medium: 40, high: 70, critical: 100 }[s.risk] ?? 25;
  }

  scoreOptOut(rate) {
    if (rate <= 0.005) return 0;
    if (rate <= 0.015) return 20;
    if (rate <= 0.030) return 50;
    if (rate <= 0.050) return 80;
    return 100;
  }

  scoreBlock(rate) {
    if (rate <= 0.002) return 0;
    if (rate <= 0.010) return 30;
    if (rate <= 0.025) return 65;
    return 100;
  }

  scoreHealth(h) {
    if (h.badMacRate > 0.15) return 80;
    if (h.badMacRate > 0.05) return 45;
    return 5;
  }

  scoreQuota(q) { return Math.min(100, q * 100); }

  scoreConn(c) {
    if (c.disconnects24h > 8) return 70;
    if (c.disconnects24h > 3) return 35;
    return 5;
  }

  recommend(score) {
    if (score > 75) return {
      action: 'STOP_SESSION',
      msg: '🔴 وقّف الجلسة فوراً. الحظر قريب جداً.',
    };
    if (score > 50) return {
      action: 'PAUSE_24H_THEN_25PCT',
      msg: '🟠 أوقف 24 ساعة، ثم استأنف بـ 25% من السرعة.',
    };
    if (score > 25) return {
      action: 'REDUCE_50PCT',
      msg: '🟡 قلّل السرعة 50% وراقب كل ساعة.',
    };
    return { action: 'CONTINUE', msg: '🟢 كل حاجة تمام.' };
  }
}
```

### Kill Switch عام

```javascript
class GlobalKillSwitch {
  constructor(db, alerter) {
    this.db = db;
    this.alerter = alerter;
    this.triggered = false;
  }

  /** يشتغل كل 5 دقايق */
  async monitor(sessions) {
    if (this.triggered) return;

    const scores = await Promise.all(
      sessions.map(async s => ({ id: s.id, ...(await scorer.score(s.id)) }))
    );

    const critical = scores.filter(s => s.level === 'critical');
    const high     = scores.filter(s => s.level === 'high');

    // 🔴 شرط الإيقاف الشامل: 30%+ من الأرقام في خطر
    const dangerRatio = (critical.length + high.length) / sessions.length;

    if (critical.length >= 2 || dangerRatio > 0.30) {
      await this.trigger(`
🚨🚨 KILL SWITCH ACTIVATED 🚨🚨

${critical.length} جلسة في حالة خطر حرج
${high.length} جلسة في حالة خطر عالي
النسبة: ${(dangerRatio*100).toFixed(0)}%

الجلسات الحرجة:
${critical.map(s => `  • ${s.id}: score ${s.score}`).join('\n')}

✋ تم إيقاف كل الحملات.
لازم تدخل يدوياً وتراجع النصوص والجمهور.
      `);
      return;
    }

    // إجراءات فردية
    for (const s of scores) {
      if (s.level === 'critical') await stopSession(s.id);
      else if (s.level === 'high') await pauseSession(s.id, 24 * 3600_000);
      else if (s.level === 'medium') await setRateMultiplier(s.id, 0.5);
    }
  }

  async trigger(reason) {
    this.triggered = true;
    await this.db.query(`UPDATE campaigns SET status = 'halted'`);
    await queue.pause();
    await Promise.all(
      (await getAllSessions()).map(s => stopSession(s.id))
    );
    await this.alerter.critical(reason);
  }

  async reset(approvedBy) {
    this.triggered = false;
    await this.db.log('kill_switch_reset', { approvedBy });
  }
}
```

---

## ✅ قائمة التحقق النهائية (Pre-Flight Checklist)

اطبعها وعلّم عليها قبل كل حملة:

### 📋 الداتا
```
□ كل الأرقام بصيغة E.164 صحيحة
□ مفيش مكرر
□ suppression list مطبقة ومفلترة
□ كل عميل عنده opt-in موثّق (تاريخ + مصدر)
□ استبعدت اللي مشتراش من 12 شهر+ (أو أعد تقييمهم)
□ كل عميل عنده سيجمنت وأولوية
□ Sticky session mapping جاهز (مين يبعت لمين)
```

### 📱 الأرقام
```
□ الرقم الرسمي للشركة مستبعد تماماً من Outbound
□ كل رقم كمّل 21 يوم تدفئة كاملة
□ كل رقم عنده صورة بروفايل + اسم + about
□ كل رقم عنده تاريخ محادثات حقيقي (inbound + outbound)
□ عندي 2-3 أرقام احتياطي مدفأة وجاهزة
□ عندي رقم canary للاختبار
□ مفيش رقم افتراضي/VOIP
```

### 🌐 الشبكة
```
□ كل جلسة على بروكسي منفصل
□ البروكسي Static/Sticky (مش rotating سريع)
□ البروكسي Mobile أو Residential (مش datacenter)
□ IP البروكسي من نفس بلد كود الرقم
□ اختبرت إن مفيش IP leak (curl ifconfig.me من داخل الحاوية)
□ كل جلسة بصمة جهاز مختلفة وثابتة
```

### 📝 المحتوى
```
□ التنويع ≥ 20 × عدد المستلمين
□ اختبرت الـ spintax وشفت 20 عيّنة مختلفة
□ الرسالة الأولى فيها: اسمي + مين انا + سياق شخصي
□ الرسالة الأولى مفيهاش رابط
□ الرسالة الأولى فيها سؤال (بيرفع reply ratio)
□ opt-out في كل رسالة outbound
□ مفيش روابط مختصرة (bit.ly وأخواتها)
□ الروابط من دوميني الخاص وعمره 3 شهور+
□ الإيموجي معقول (مش 10 في رسالة)
□ مفيش CAPS LOCK أو علامات تعجب متكررة
□ مفيش كلمات spam: "مجاني!!" "اربح" "عرض محدود جداً"
```

### ⚙️ التقني
```
□ Delay engine بـ Gaussian (مش uniform)
□ Circadian curve مفعّل
□ نافذة إرسال 8ص-11م بس
□ Batching + استراحات مفعّل
□ Reply ratio monitor شغال
□ Soft ban detector شغال
□ Risk scorer شغال كل 5 دقايق
□ Kill switch مفعّل ومختبر
□ Opt-out handler شغال ومختبر
□ Backup للـ session folders كل ساعة
□ Telegram alerts مربوطة ومختبرة
```

### 🧪 الاختبار
```
□ اختبرت على رقمي الشخصي الأول
□ شغّلت canary test على 20 رقم واستنيت 6 ساعات
□ delivery rate بعد الـ canary > 90%
□ reply rate بعد الـ canary > 10%
□ 0 بلاغات، 0 بلوك
□ الجلسة لسه شغالة وصحتها كويسة
```

### 🚦 الإطلاق
```
□ ابدأ بـ 20% من السعة اليومية
□ راقب أول ساعة بشكل مباشر (مش أتوماتيك)
□ لو كل حاجة تمام بعد 3 ساعات → 50%
□ لو تمام بعد 6 ساعات → 80%
□ متوصلش 100% إلا في اليوم التاني
□ متمشيش وتسيبه — راقب كل ساعة أول يومين
```

---

## 🎯 الخلاصة: الترتيب الحقيقي للأهمية

```
┌──────────────────────────────────────────────────────────┐
│                                                          │
│  60%  ← جودة الجمهور + جودة الرسالة                     │
│         (عملاء حقيقيين + رسالة شخصية + opt-out)         │
│                                                          │
│  20%  ← التدفئة والصبر                                   │
│         (21 يوم + تدرّج + متستعجلش)                      │
│                                                          │
│  12%  ← هندسة التوقيت                                    │
│         (Gaussian + Circadian + Batching)                │
│                                                          │
│   5%  ← الشبكة والبصمة                                   │
│         (بروكسي + fingerprint)                           │
│                                                          │
│   3%  ← تنويع النصوص                                     │
│         (Spintax)                                        │
│                                                          │
└──────────────────────────────────────────────────────────┘

💡 معظم الناس بتعكس الترتيب ده — بتصرف كل وقتها على
   البروكسي والـ spintax، وبتبعت لقوايم مشتراة برسالة
   بيعية فجّة. وبتتحظر في يوم.
```

---

**التالي:** [`04-ARCHITECTURE.md`](./04-ARCHITECTURE.md) — معمارية النظام
