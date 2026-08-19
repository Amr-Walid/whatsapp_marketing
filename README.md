# 🟢 دليل حملات الواتساب بالطريقة غير الرسمية (Unofficial WhatsApp Marketing Stack)

> **الهدف:** بناء نظام كامل يبعت حملة تسويقية لقاعدة عملائك على الواتساب، ويستقبل الأوردرات (من الشات أو من الموقع)، بشكل مؤتمت بالكامل — **بدون WhatsApp Business API الرسمي** — وبأقل احتمالية حظر ممكنة.

**ملفات الدليل:**

| الملف | المحتوى |
|---|---|
| [`README.md`](./README.md) | الفهرس + الخلاصة السريعة (الملف الحالي) |
| [`docs/01-DATA-ANALYSIS.md`](./docs/01-DATA-ANALYSIS.md) | تحليل الداتا، RFM، تنظيف الأرقام، التقسيم |
| [`docs/02-INFRASTRUCTURE.md`](./docs/02-INFRASTRUCTURE.md) | المكتبات، الأدوات، السيرفرات، البروكسي، الشرائح |
| [`docs/03-ANTIBAN-BIBLE.md`](./docs/03-ANTIBAN-BIBLE.md) | 🔥 كتاب تجنب الحظر — كل إشارة بترصدها Meta وإزاي تتعامل معاها |
| [`docs/04-ARCHITECTURE.md`](./docs/04-ARCHITECTURE.md) | معمارية النظام: Dispatcher، Queue، Rotation، Health Check |
| [`docs/05-ORDER-FUNNEL.md`](./docs/05-ORDER-FUNNEL.md) | مسار الطلب: البوت داخل الشات + صفحة الهبوط |
| [`docs/06-IMPLEMENTATION.md`](./docs/06-IMPLEMENTATION.md) | كود عملي جاهز + Docker + خطة تنفيذ 30 يوم |
| [`docs/07-RISKS-LEGAL.md`](./docs/07-RISKS-LEGAL.md) | المخاطر الحقيقية، التكلفة المخفية، خطة الطوارئ |
| [`docs/08-HYBRID-OVERVIEW.md`](./docs/08-HYBRID-OVERVIEW.md) | 🔀 **النظام الهجين** — الاقتصاد، النوافذ، أسعار Meta 2026، قاعدة التوجيه |
| [`docs/09-HYBRID-ARCHITECTURE.md`](./docs/09-HYBRID-ARCHITECTURE.md) | 🔀 تصميم الهجين: طبقة المزوّدين، `WindowTracker`، `ChannelRouter`، المخطط |
| [`docs/10-HYBRID-IMPLEMENTATION.md`](./docs/10-HYBRID-IMPLEMENTATION.md) | 🔀 كود الهجين + إدارة القوالب + خطة هجرة 6 أسابيع + دليل CTWA |
| [`docs/11-MANAGER-PITCH.md`](./docs/11-MANAGER-PITCH.md) | 🎤 **العرض على المدير** — سكربت الكلام، مقارنة الـ3، مخاطر كل واحد، نقطة التعادل، أسئلة متوقعة |
| [`deck.html`](./deck.html) | 🖥️ ديك عرض 13 شريحة (RTL) — افتحه في المتصفح، `←` `→` للتنقل، `P` للطباعة PDF |

---

## ⚠️ اقرأ ده الأول (تحذير أمانة)

الطريقة غير الرسمية **مخالفة لشروط استخدام واتساب (WhatsApp Terms of Service)**. مش قضية "ممكن تتحظر" بس — دي قضية إن Meta ليها كل الحق تحظر الرقم نهائي، وفي حالات نادرة تحظر الـ Business Manager بتاعك لو ربطته. كل اللي جاي في الدليل ده هو **تقليل للمخاطر (Risk Mitigation)**، مش إلغاء لها.

**القاعدة الذهبية اللي لو خرقتها هيتحظر أي رقم مهما عملت:**
> بعت لأرقام **مش عارفينك** ومش موافقة تستقبل منك (No Opt-in) → الناس بتضغط Report → الرقم يموت. خلاص. مفيش بروكسي ولا Delay ولا Spintax بينفع.

الدليل ده مبني على إن **عندك قاعدة عملاء فعليين** اشتروا منك أو سجلوا عندك. لو مش كده، لا تبدأ.

---

## 🎯 الخلاصة السريعة (TL;DR)

### الـ Stack الموصى بيه

```
Data Layer:      PostgreSQL + Redis (BullMQ Queue)
WhatsApp Engine: Evolution API (Docker, multi-instance) — مبني على Baileys
                 أو WAHA (لو عايز Puppeteer-based أثبت)
Anti-Ban Layer:  baileys-antiban (لو بتكتب كود مباشر) أو منطق مخصص
Orchestration:   n8n (للـ workflows) أو Node.js Worker مخصص
Unified Inbox:   Chatwoot (يجمع كل الأرقام في شاشة واحدة)
Bot Flow:        Typebot (visual) أو State Machine بكود
Network:         Mobile/Residential Proxy لكل جلسة (منفصل)
Landing:         Next.js / أي صفحة بتقرأ query params
Monitoring:      Telegram Bot Alerts + Health Dashboard
```

### أرقام السلامة (Safe Limits) — اللي المجتمع اتفق عليها

| العنصر | القيمة الآمنة |
|---|---|
| رسائل يوم 1 لرقم جديد | **0** (تدفئة يدوية بس) |
| رسائل بعد أسبوع تدفئة | 20–30 / يوم |
| رسائل رقم "دافي" (شهر+) | 80–150 / يوم كحد أقصى |
| Delay بين كل رسالة | **عشوائي 25–90 ثانية** (مش 5 ثواني!) |
| Batch قبل الاستراحة | 15–25 رسالة → استراحة 30–90 دقيقة |
| نسبة الرد المطلوبة (Reply Ratio) | > 15% وإلا وقّف الحملة |
| عدد جلسات على IP واحد | **1** (بروكسي منفصل لكل رقم) |
| بلاغات Spam قبل الحظر | 3–5 بلاغات = موت الرقم |

### معادلة التأخير الذكية

```javascript
// مش random ثابت — لازم Gaussian + عوامل سياقية
delay = gaussianRandom(mean=45s, stdDev=18s)
      × circadianMultiplier(hour)     // 4x أبطأ من 2-6 صباحاً
      × contactRiskMultiplier(jid)    // 2.5x للغرباء، 1.0x للمعروفين
      × messageLengthFactor(text)     // typing simulation
      + thinkPause(probability=0.08)
```

---

## 🗺️ خريطة النظام الكاملة

```
┌─────────────────────────────────────────────────────────────────┐
│                     CONTROL PLANE (لوحة التحكم)                  │
│   PostgreSQL (customers, orders, sessions, message_log)          │
│   Redis + BullMQ (campaign queue, retry, delayed jobs)           │
└────────────────────────────┬────────────────────────────────────┘
                             │
                ┌────────────▼────────────┐
                │   SMART DISPATCHER      │
                │  ─────────────────────  │
                │  • Round-Robin Rotation │
                │  • Gaussian Delay Engine│
                │  • Quota Enforcement    │
                │  • Circadian Gate       │
                │  • Reply-Ratio Guard    │
                │  • Kill Switch          │
                └────────────┬────────────┘
                             │
        ┌────────────────────┼────────────────────┐
        ▼                    ▼                    ▼
┌───────────────┐   ┌───────────────┐   ┌───────────────┐
│  WORKER A     │   │  WORKER B     │   │  WORKER C     │
│  SIM 1 + 2    │   │  SIM 3 + 4    │   │  SIM 5 + 6    │
│  Proxy 4G #1  │   │  Proxy 4G #2  │   │  Proxy 4G #3  │
│  Evolution    │   │  Evolution    │   │  Evolution    │
│  Instance     │   │  Instance     │   │  Instance     │
└───────┬───────┘   └───────┬───────┘   └───────┬───────┘
        │                   │                   │
        └───────────────────┼───────────────────┘
                            ▼
                    ┌───────────────┐
                    │  هاتف العميل   │
                    └───────┬───────┘
                            │
         ┌──────────────────┴──────────────────┐
         ▼                                     ▼
┌──────────────────────┐            ┌──────────────────────┐
│  مسار الشات          │            │  مسار صفحة الهبوط     │
│  ──────────────────  │            │  ──────────────────  │
│  رد "1" / "عايز اطلب"│            │  Click Link + ?cid=  │
│       ↓              │            │       ↓              │
│  Numbered Menu       │            │  Prefilled Checkout  │
│       ↓              │            │       ↓              │
│  State Machine       │            │  Order Created       │
│  (product→qty→addr)  │            │       ↓              │
│       ↓              │            │  Webhook → WhatsApp  │
│  Order Confirmed     │            │  تأكيد + رقم تتبع     │
└──────────┬───────────┘            └──────────┬───────────┘
           │                                   │
           └───────────────┬───────────────────┘
                           ▼
              ┌────────────────────────┐
              │  ORDERS DB + CRM       │
              │  + Chatwoot Inbox      │
              │  + Status Webhooks     │
              └────────────────────────┘
                           │
                    ┌──────▼──────┐
                    │  MONITORING │
                    │  Telegram   │
                    │  Alerts     │
                    └─────────────┘
```

---

## 📊 مقارنة سريعة: الرسمي vs غير الرسمي

| البند | الرسمي (Cloud API) | غير الرسمي (Baileys/Evolution) |
|---|---|---|
| تكلفة الرسالة | ~$0.03–0.09 / محادثة تسويقية | $0 |
| تكلفة شهرية ثابتة | $0 (Cloud API مباشر) أو $30–99 (SaaS) | $20–150 (VPS + Proxies + SIMs) |
| خطر حظر الرقم | منخفض جداً | **مرتفع** |
| أزرار تفاعلية | ✅ كامل | ⚠️ غير مستقر / مش شغال دايماً |
| كتالوج منتجات | ✅ | ❌ |
| استقرار | ✅ SLA | ❌ يتعطل مع تحديثات واتساب |
| موافقة قوالب | مطلوبة (تأخير) | غير مطلوبة (حرية كاملة) |
| سرعة البدء | 2–7 أيام (توثيق) | ساعات |
| Scaling | آلاف/ساعة | 100–150 / رقم / يوم |
| قانونياً | ✅ متوافق | ❌ مخالف للـ ToS |

### 💡 التوصية الحقيقية (Hybrid)

معظم الناس اللي نجحت فعلياً عملت كده:

```
الحملات الترويجية الجماعية (Outbound Marketing)
        ↓
   استخدم الرسمي (Cloud API) — القوالب معتمدة، مفيش حظر
   التكلفة: 5000 عميل × $0.05 = $250 للحملة
        ↓
─────────────────────────────────────────────
الردود والمحادثات + بوت الأوردرات (Inbound)
        ↓
   استخدم غير الرسمي — مجاني، حرية كاملة، ومخاطرة أقل
   لأن العميل هو اللي بدأ المحادثة (Inbound = آمن)
```

**السبب:** أكبر مصدر للحظر هو **Outbound Cold Messages**. الـ Inbound (لما العميل يكلمك الأول) بيكاد يكون آمن تماماً حتى بالطرق غير الرسمية.

لو أصرّيت على غير الرسمي في الـ Outbound كمان — اقرأ [`docs/03-ANTIBAN-BIBLE.md`](./docs/03-ANTIBAN-BIBLE.md) بالكامل قبل ما تبعت رسالة واحدة.

---

## 🚀 من فين تبدأ؟

1. **اقرأ** [`docs/07-RISKS-LEGAL.md`](./docs/07-RISKS-LEGAL.md) — اعرف بتخاطر بإيه بالضبط
2. **حلل داتاك** بـ [`docs/01-DATA-ANALYSIS.md`](./docs/01-DATA-ANALYSIS.md)
3. **جهّز البنية** بـ [`docs/02-INFRASTRUCTURE.md`](./docs/02-INFRASTRUCTURE.md)
4. **ادرس الـ Anti-Ban** بـ [`docs/03-ANTIBAN-BIBLE.md`](./docs/03-ANTIBAN-BIBLE.md) ← أهم ملف
5. **ابني** بـ [`docs/04`](./docs/04-ARCHITECTURE.md) + [`docs/05`](./docs/05-ORDER-FUNNEL.md) + [`docs/06`](./docs/06-IMPLEMENTATION.md)
6. **بعد ما يستقر** — انتقل للهجين بـ [`docs/08`](./docs/08-HYBRID-OVERVIEW.md) → [`docs/09`](./docs/09-HYBRID-ARCHITECTURE.md) → [`docs/10`](./docs/10-HYBRID-IMPLEMENTATION.md)

> **🎤 محتاج تعرض الموضوع على الإدارة؟** ابدأ من [`docs/11-MANAGER-PITCH.md`](./docs/11-MANAGER-PITCH.md) — فيه سكربت الكلام، مقارنة الـ3 اختيارات، مخاطر كل واحد، والأسئلة المتوقعة وردودها. واعرض من [`deck.html`](./deck.html).

---

## 🔀 النظام الهجين (المسار الموصى به)

بعد ما النظام غير الرسمي يستقر، الخطوة الطبيعية هي **تقسيم المسؤوليات** بين قناة رسمية وقناة غير رسمية:

```
العميل مكلمناش  →  🏢 رسمي (قالب معتمد) — عشان الخطر
العميل كلّمنا    →  ⚡ غير رسمي (رسالة حرة) — عشان التكلفة والمرونة
الاستثناء: رسائل المعاملات المهمة (تأكيد/شحن) → 🏢 رسمي دايماً
```

**ليه ده مهم دلوقتي:** Meta غيّرت نموذج الفاتورة من "لكل محادثة" لـ **"لكل رسالة"** في يوليو 2025، ومن **1 أكتوبر 2026** رسائل الـ service والـ utility اللي جوّه النافذة **بتبقى مدفوعة** بعد ما كانت مجانية. النظام الهجين بيحصّن تكلفة المحادثات والبوت من التغيير ده لأنها بتفضل على القناة غير الرسمية بتكلفة **صفر**.

**نافذة الـ FEP (72 ساعة) بتفضل مجانية** — وهي أقوى ورقة في النظام: عميل بيضغط إعلان Click-to-WhatsApp → 72 ساعة كل الرسائل مجاناً وبدون خطر حظر.

| المسار | ابدأ من |
|---|---|
| فاهم الاقتصاد والقرار | [`docs/08-HYBRID-OVERVIEW.md`](./docs/08-HYBRID-OVERVIEW.md) |
| عايز التصميم والمكوّنات | [`docs/09-HYBRID-ARCHITECTURE.md`](./docs/09-HYBRID-ARCHITECTURE.md) |
| عايز الكود وخطة الهجرة | [`docs/10-HYBRID-IMPLEMENTATION.md`](./docs/10-HYBRID-IMPLEMENTATION.md) |

> ⚠️ **متبدأش بالهجين.** فيه 3 أضعاف نقاط الفشل. شغّل غير الرسمي لوحده الأول ([`docs/06`](./docs/06-IMPLEMENTATION.md)) → استقر → ضيف الرسمي جنبه → وجّه تدريجياً (5% → 25% → 60% → 100%).

---

*آخر تحديث: أغسطس 2026 — المعلومات مبنية على أبحاث وتجارب مجتمع المطورين حتى هذا التاريخ. بروتوكول واتساب يتغير باستمرار.*
