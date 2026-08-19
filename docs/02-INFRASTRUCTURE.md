# 🏗️ المرحلة 2: البنية التحتية (Infrastructure)

> اختيار المكتبة الصح + السيرفر الصح + البروكسي الصح + الشريحة الصح = 70% من نجاح النظام.

---

## 1. المحركات المتاحة (Engines) — مقارنة تفصيلية

### الطبقة الأولى: المكتبات الأساسية (Libraries)

#### 🥇 Baileys (`@whiskeysockets/baileys`)
```
النوع:      WebSocket مباشر — بيحاكي بروتوكول واتساب (Noise Protocol)
اللغة:      TypeScript / Node.js
الموارد:    خفيف جداً — ~50-80MB RAM لكل جلسة
Repo:       github.com/WhiskeySockets/Baileys
```

**المميزات:**
- ✅ مفيش Chromium — يعني تقدر تشغّل 10-20 جلسة على VPS متوسط
- ✅ أسرع محرك متاح (اتصال مباشر بسيرفرات واتساب)
- ✅ تحكم كامل في البروتوكول (device fingerprint, presence, receipts)
- ✅ مجتمع كبير + تحديثات سريعة

**العيوب:**
- ❌ مشاكل `Bad MAC` / `No Session` متكررة (أشهر مشكلة في المكتبة)
- ❌ مشكلة LID/PN (بعد تحديث واتساب لـ Linked Identity في 2024)
- ❌ الأزرار التفاعلية غير مستقرة
- ❌ بيتعطل مع تحديثات بروتوكول واتساب

**استخدمه لو:** عايز أقصى تحكم وأقل استهلاك موارد، وعندك مطور Node.js.

---

#### 🥈 whatsapp-web.js
```
النوع:      Puppeteer — بيشغّل Chromium حقيقي على WhatsApp Web
اللغة:      JavaScript / Node.js
الموارد:    ثقيل — ~300-500MB RAM لكل جلسة
```

**المميزات:**
- ✅ **أثبت من ناحية "الشبه بالبشر"** — لأنه فعلاً بيستخدم واتساب ويب حقيقي
- ✅ أسهل في التعامل (API واضح)
- ✅ أقل مشاكل تشفير

**العيوب:**
- ❌ استهلاك موارد ضخم — 3-4 جلسات كحد أقصى على VPS متوسط
- ❌ أبطأ
- ❌ محتاج تجهيز Chromium dependencies

**استخدمه لو:** عندك 1-3 أرقام بس وعايز أقصى استقرار.

---

#### 🥉 WPPConnect
```
النوع:      Puppeteer-based
اللغة:      TypeScript
Repo:       github.com/wppconnect-team/wppconnect
```
شبيه بـ whatsapp-web.js لكن بمميزات إضافية (Business features، Labels). أقل شعبية.

---

### الطبقة الثانية: الـ Gateways الجاهزة (Self-hosted Servers)

هنا اللي بتوفّر عليك بناء إدارة الجلسات من الصفر.

#### 🏆 Evolution API — التوصية الأولى
```
النوع:      REST API + Webhooks فوق Baileys
النشر:      Docker
Repo:       github.com/evolution-foundation/evolution-api
```

**ليه ده الأفضل لحالتك:**
- ✅ **Multi-instance** — عدد لا نهائي من الأرقام في نفس الـ container
- ✅ REST API جاهز — `POST /message/sendText/{instance}`
- ✅ Webhooks لكل event (رسالة واردة، تغيير حالة، QR جديد)
- ✅ تكامل مدمج مع **Chatwoot** (صندوق وارد موحد)
- ✅ تكامل مدمج مع **Typebot** (بناء بوت visual)
- ✅ تكامل مع **RabbitMQ / SQS** (للطوابير)
- ✅ يدعم كمان **Meta Cloud API الرسمي** → لو قررت تنتقل، نفس الكود!
- ✅ Postgres/Redis للـ state persistence

**العيوب:**
- ❌ Baileys underneath → نفس مشاكل الحظر
- ❌ **مفيش Anti-Ban logic مدمج** — لازم تبنيه فوقه بنفسك (وده اللي هنعمله)
- ⚠️ ترخيص: راجع الـ license الحالي قبل الاستخدام التجاري

---

#### 🥈 WAHA (WhatsApp HTTP API)
```
النشر:      Docker
الموقع:     waha.devlike.pro
```

**المميزات:**
- ✅ يدعم **محركات متعددة**: WEBJS (Puppeteer)، NOWEB (Baileys)، GOWS (Go)
- ✅ توثيق ممتاز
- ✅ إصدار Core مجاني + Plus مدفوع

**ليه مهم:** لو Baileys اتعطل بسبب تحديث واتساب، تقدر تبدّل الـ engine لـ WEBJS بسطر واحد في الـ env. **ده تأمين ضد التعطل الكامل.**

---

#### مقارنة نهائية

| | Baileys خالص | Evolution API | WAHA | whatsapp-web.js |
|---|---|---|---|---|
| سهولة البدء | 🟡 متوسط | 🟢 سهل | 🟢 سهل | 🟢 سهل |
| Multi-session | يدوي | ✅ مدمج | ✅ مدمج | يدوي |
| REST API | ❌ | ✅ | ✅ | ❌ |
| Webhooks | يدوي | ✅ | ✅ | يدوي |
| موارد/جلسة | 🟢 50MB | 🟢 60MB | 🟡 متغير | 🔴 400MB |
| تحكم عميق | 🟢 كامل | 🟡 محدود | 🟡 محدود | 🟡 متوسط |
| Chatwoot | يدوي | ✅ مدمج | 🟡 | يدوي |
| بديل engine | ❌ | ❌ | ✅ | ❌ |
| **التوصية** | للتحكم الكامل | **👈 الأفضل** | بديل ممتاز | 1-3 أرقام |

---

## 2. طبقة الحماية: `baileys-antiban`

لو بتكتب Baileys مباشر، دي مكتبة مهمة جداً. بتوفّر منطق anti-ban جاهز.

```bash
npm install baileys baileys-antiban
```

### أهم الموديولات اللي بتوفرها

| الموديول | الوظيفة |
|---|---|
| `RateLimiter` | تأخير بـ Gaussian jitter + حدود دقيقة/ساعة/يوم |
| `WarmupScheduler` | تدرّج 7 أيام لرقم جديد |
| `HealthMonitor` | يرصد تدهور الجلسة قبل الحظر |
| `LidResolver` | يحل مشكلة LID/PN (سبب Bad MAC) |
| `ReplyRatioGuard` | يوقّف الإرسال للناس اللي مش بترد |
| `ContactGraphWarmer` | يفرض handshake قبل الإرسال الجماعي |
| `PresenceChoreographer` | Circadian rhythm + typing simulation (WPM) |
| `LegitimacySignalInjector` | يحقن "أخطاء بشرية" (typos + تصحيح) |
| `TopologyThrottler` | يحدد عدد جهات الاتصال الجديدة/ساعة |
| `proxyRotator` | تدوير البروكسي تلقائي |
| `BanRecoveryOrchestrator` | خطة تعافي منظمة بعد الحظر |
| `HumanEntropyService` | نشاط بشري في الخلفية (typing/read/presence) |
| `DeliveryTracker` | يرصد نسبة التوصيل (<60% = soft ban) |
| `CrossInstanceCoordinator` | يمنع 5 جلسات من تجاوز حد الـ IP |

### مثال إعداد

```javascript
import makeWASocket from 'baileys';
import { wrapSocketWithFingerprint } from 'baileys-antiban';

const sock = wrapSocketWithFingerprint(
  makeWASocket,
  { auth: state },
  {
    preset: 'conservative',     // ← ابدأ دايماً بده

    // حدود الإرسال
    maxPerMinute: 2,
    maxPerHour:   25,
    maxPerDay:    80,
    minDelayMs:   25_000,
    maxDelayMs:   90_000,

    // حماية من الرسائل المتطابقة
    maxIdenticalMessages: 2,
    identicalMessageWindowMs: 6 * 3600_000,

    // التدفئة
    warmupDays: 14,
    day1Limit:  5,
    growthFactor: 1.4,

    // إيقاف تلقائي
    autoPauseAt: 'medium',      // ← أوقف بدري مش بعد ما يبوظ

    // Contact graph
    contactGraph: {
      enabled: true,
      maxStrangerMessagesPerDay: 15,
      requireHandshakeBeforeGroupSend: true,
    },

    // نسبة الرد
    replyRatio: {
      enabled: true,
      minRatio: 0.10,
      minMessagesBeforeEnforce: 20,
      cooldownHoursOnViolation: 24,
    },

    // إيقاع بشري
    presence: {
      enabled: true,
      enableCircadianRhythm: true,
      enableTypingModel: true,
      typingWPM: 40,
      timezone: 'Africa/Cairo',
      activityCurve: 'office',
      distractionPauseProbability: 0.06,
      offlineGapProbability: 0.04,
      circadian: {
        enabled: true,
        profile: 'default',
        timezone: 'Africa/Cairo',
      },
    },

    // إشارات شرعية
    legitimacySignals: { typoProbability: 0.025 },

    // تنسيق بين الجلسات على نفس الـ IP
    instanceCoordinator: '/data/wa-pool.json',
    instancePoolMaxPerMinute: 4,

    persist: '/data/antiban-state.json',
    logging: true,
  }
);
```

> ⚠️ **تحذير أمني مهم:** في أبريل 2026 اتكشف إن باكدج اسمه `lotusbail` (56 ألف تحميل) كان بيسرّب بيانات الجلسات ورسائل الواتساب. **متثبّتش أي باكدج "anti-ban" من npm بدون:**
> - مراجعة الكود المصدري
> - التأكد من signed releases / SLSA provenance
> - التأكد إن مفيش telemetry أو network calls غريبة
> - عدد نجوم/مراجعات حقيقية على GitHub
>
> `baileys-antiban` بيوفّر SLSA-signed releases + zero telemetry، لكن **راجع بنفسك دايماً.**

---

## 3. الشرائح والأرقام (SIM Strategy) — أهم قرار

### تصنيف الأرقام بالمخاطر

| النوع | خطر الحظر | التكلفة | ملاحظات |
|---|---|---|---|
| 🔴 رقم شركتك الرسمي | كارثي | — | **متستخدموش نهائي في Outbound** |
| 🟢 شريحة فيزيائية جديدة (نفس بلد العملاء) | منخفض-متوسط | ~$3-8/شهر | **الأفضل** |
| 🟡 شريحة فيزيائية مستعملة (عمرها شهور) | منخفض | متوسط | ممتاز لو معاك تاريخ نشاط |
| 🔴 رقم افتراضي / VOIP (TextNow, Google Voice) | **عالي جداً** | رخيص | Meta بترصدها فوراً |
| 🔴 أرقام SMS-activate المؤجرة | **كارثي** | رخيص جداً | محروقة أصلاً — تُحظر في ساعات |
| 🟡 eSIM من مشغل حقيقي | متوسط | متوسط | أفضل من VOIP بكتير |

### 🔴 القاعدة الحديدية

```
┌────────────────────────────────────────────────────────┐
│  رقم شركتك الرسمي (اللي على الموقع والفيسبوك)          │
│  ─────────────────────────────────────────────────     │
│  ❌ متستخدموش في Outbound Campaigns أبداً              │
│  ✅ استخدمه بس للـ Inbound (لما العميل يكلمك)          │
│                                                        │
│  السبب: لو اتحظر، خسرت:                                │
│    • كل تاريخ محادثاتك مع العملاء                       │
│    • الرقم اللي كل حاجة مربوطة بيه                      │
│    • ثقة العملاء (الرقم "مش موجود")                     │
│    • احتمال ربط الحظر بالـ Business Manager             │
└────────────────────────────────────────────────────────┘
```

### التوزيع المقترح (لقاعدة 5000 عميل)

```
🔵 رقم رسمي (Inbound فقط)              × 1
   → مربوط على Chatwoot، مفيش outbound نهائي

🟢 أرقام حملات "دافية" (Outbound)        × 6-8
   → شرائح فيزيائية، مدفأة 3 أسابيع
   → 80-100 رسالة/يوم لكل واحد
   → السعة: ~600-800 رسالة/يوم
   → 5000 عميل ÷ 700 = 7 أيام للحملة الكاملة

🟡 أرقام احتياطي (Standby)              × 2-3
   → مدفأة وجاهزة، لو حصل حظر تحل مكانه فوراً
   → مهم جداً: التدفئة بتاخد أسابيع، مينفعش تستنى

🔴 رقم قرباني للاختبار (Canary)          × 1
   → تجرب عليه أي نص/تكتيك جديد الأول
   → لو اتحظر، معلومة مجانية بتحمي باقي الأرقام
```

### 💡 استراتيجية الـ Canary Number

```javascript
// قبل ما تشغّل حملة على 6 أرقام، اختبر على 1
async function canaryTest(campaignConfig, testAudience) {
  const canary = 'canary_session_1';

  // 1. ابعت 20 رسالة بس على العينة
  const results = await sendBatch(canary, testAudience.slice(0, 20), campaignConfig);

  // 2. استنى 6 ساعات
  await sleep(6 * 3600_000);

  // 3. قيّم
  const health = await getSessionHealth(canary);
  const metrics = {
    delivered:   results.filter(r => r.status === 'delivered').length / 20,
    replied:     await countReplies(canary, testAudience.slice(0, 20)) / 20,
    blocked:     await countBlocks(canary),
    sessionOk:   health.status === 'open',
    riskLevel:   health.risk,
  };

  // 4. قرار
  if (!metrics.sessionOk) {
    return { proceed: false, reason: '🔴 الجلسة اتحظرت — النص أو التكتيك خطر' };
  }
  if (metrics.delivered < 0.85) {
    return { proceed: false, reason: '🟠 توصيل ضعيف — soft ban محتمل' };
  }
  if (metrics.replied < 0.05) {
    return { proceed: false, reason: '🟡 مفيش تفاعل — الجمهور غلط أو النص ضعيف' };
  }
  if (metrics.blocked > 1) {
    return { proceed: false, reason: '🟠 الناس بتبلوك — راجع النص' };
  }

  return { proceed: true, metrics };
}
```

---

## 4. الشبكة والبروكسي (Network Layer) — الطبقة اللي معظم الناس بتغلط فيها

### المشكلة: Cluster Ban

```
❌ السيناريو الكارثي:
   VPS واحد، IP واحد: 203.0.113.45
   ├── جلسة رقم 1 (SIM A)
   ├── جلسة رقم 2 (SIM B)
   ├── جلسة رقم 3 (SIM C)
   └── جلسة رقم 4 (SIM D)

   Meta بتشوف: 4 أرقام مختلفة، من IP datacenter واحد،
   نفس البصمة، نفس السلوك، في نفس الوقت.
   → ربط الحسابات (Account Linking)
   → لما واحد يتحظر، الأربعة يتحظروا معاه 💀
```

### الحل: بروكسي منفصل لكل جلسة

| النوع | جودة | السعر التقريبي | ملاحظات |
|---|---|---|---|
| 🔴 Datacenter Proxy | سيء | $1-3/IP | **مرصود** — نفس مشكلة الـ VPS |
| 🟡 Residential (Rotating) | متوسط | $3-10/GB | ⚠️ التدوير بيقطع الجلسة! |
| 🟢 Residential (Static/Sticky) | جيد | $3-8/IP/شهر | **مناسب** |
| 🏆 Mobile / 4G Proxy | ممتاز | $15-50/IP/شهر | **الأفضل** — نفس IP المستخدم الحقيقي |

### 🔴 نقطة حرجة: Sticky vs Rotating

```
❌ Rotating Proxy (IP يتغير كل دقيقة/طلب)
   → واتساب بيشوف: نفس الحساب من 50 IP في ساعة
   → إشارة "Account Takeover" أو "بوت"
   → حظر فوري

✅ Sticky/Static Proxy (نفس IP لمدة طويلة)
   → واتساب بيشوف: حساب ثابت من نفس المكان
   → طبيعي تماماً
```

**قاعدة:** لو بتستخدم Rotating، اضبطه على **sticky session لمدة 24 ساعة+**، أو استخدم Static.

### مطابقة الجغرافيا (Geo Matching)

```
🎯 قاعدة ذهبية: IP البروكسي لازم يكون من نفس بلد كود الرقم

✅ رقم +20 (مصر) + IP مصري         → طبيعي
🟡 رقم +20 + IP سعودي              → مقبول (مصري مقيم بالسعودية)
🔴 رقم +20 + IP ألماني/أمريكي        → إشارة قوية جداً للحظر
🔴 رقم +20 + IP يتغير كل ساعة        → حظر شبه مؤكد
```

### إعداد البروكسي

#### مع Baileys مباشر
```javascript
import { SocksProxyAgent } from 'socks-proxy-agent';
import makeWASocket from 'baileys';

const agent = new SocksProxyAgent(
  'socks5://user:pass@eg-mobile-1.proxyprovider.com:1080'
);

const sock = makeWASocket({
  auth: state,
  agent,           // ← للاتصالات العامة
  fetchAgent: agent, // ← لتحميل الميديا
});
```

#### مع Evolution API (Docker)
```yaml
# كل جلسة في container منفصل، كل container ببروكسي
services:
  evo-session-1:
    image: evoapicloud/evolution-api:latest
    environment:
      PROXY_HOST: eg-mobile-1.provider.com
      PROXY_PORT: '1080'
      PROXY_PROTOCOL: socks5
      PROXY_USERNAME: user1
      PROXY_PASSWORD: pass1
```

#### أنضف طريقة: Docker + Gluetun
```yaml
services:
  # حاوية الشبكة
  gluetun-1:
    image: qmcgaw/gluetun
    cap_add: [NET_ADMIN]
    environment:
      VPN_SERVICE_PROVIDER: custom
      VPN_TYPE: openvpn
      # ملف تكوين البروكسي/VPN

  # الجلسة تستخدم شبكة الحاوية اللي فوق
  evo-1:
    image: evoapicloud/evolution-api:latest
    network_mode: 'service:gluetun-1'
    depends_on: [gluetun-1]
```

بكده **كل حزمة** خارجة من الجلسة بتمر بالبروكسي — مفيش DNS leaks ولا اتصالات مباشرة.

### تحقق من عدم التسريب

```bash
# داخل كل container، تأكد إن الـ IP الخارج هو البروكسي
docker exec evo-session-1 curl -s https://ifconfig.me
# ✅ لازم يرجّع IP البروكسي، مش IP السيرفر
```

---

## 5. بصمة الجهاز (Device Fingerprint)

كل جلسة بتبلّغ واتساب ببيانات الجهاز. لو 8 جلسات نفس البصمة بالحرف = ربط فوري.

```javascript
// ❌ الغلط — كل الجلسات نفس البصمة
browser: ['Chrome (Linux)', 'Chrome', '120.0.0']

// ✅ الصح — بصمة متنوعة وواقعية لكل جلسة
const DEVICE_POOL = [
  { browser: ['WhatsApp', 'Chrome',  '131.0.6778.86'],  os: 'Windows 10' },
  { browser: ['WhatsApp', 'Chrome',  '130.0.6723.117'], os: 'Windows 11' },
  { browser: ['WhatsApp', 'Safari',  '17.6'],           os: 'macOS 14.6' },
  { browser: ['WhatsApp', 'Edge',    '131.0.2903.86'],  os: 'Windows 11' },
  { browser: ['WhatsApp', 'Firefox', '133.0'],          os: 'Ubuntu 24.04' },
  { browser: ['WhatsApp', 'Chrome',  '131.0.6778.85'],  os: 'macOS 15.1' },
];

function fingerprintFor(sessionId) {
  // ثابت لكل جلسة (deterministic) — متغيّروش بين restarts!
  const hash = [...sessionId].reduce((a, c) => a + c.charCodeAt(0), 0);
  return DEVICE_POOL[hash % DEVICE_POOL.length];
}

const fp = fingerprintFor('session_3');
const sock = makeWASocket({
  auth: state,
  browser: fp.browser,
  markOnlineOnConnect: false,  // ← مهم جداً! متعلنش online فوراً
});
```

> ⚠️ **البصمة لازم تكون ثابتة لكل جلسة.** لو غيّرتها كل restart، واتساب بيشوف "الحساب ده بيسجّل من 20 جهاز مختلف" = إشارة سرقة حساب.

---

## 6. مواصفات السيرفر (Server Specs)

### Baileys / Evolution API (خفيف)

| عدد الجلسات | CPU | RAM | Disk | التكلفة/شهر |
|---|---|---|---|---|
| 1-3 | 2 vCPU | 2 GB | 20 GB SSD | $6-12 |
| 4-8 | 2-4 vCPU | 4 GB | 40 GB SSD | $12-24 |
| 9-20 | 4 vCPU | 8 GB | 80 GB SSD | $24-48 |
| 20+ | 8 vCPU | 16 GB | 160 GB NVMe | $48-96 |

### whatsapp-web.js / WAHA-WEBJS (ثقيل)

| عدد الجلسات | CPU | RAM |
|---|---|---|
| 1-2 | 2 vCPU | 4 GB |
| 3-5 | 4 vCPU | 8 GB |
| 6-10 | 8 vCPU | 16 GB |

### 🔴 قاعدة الاستقرار المطلقة

```
البيانات اللي لو ضاعت هتخسر كل حاجة:
  📁 مجلد auth/session لكل جلسة
     → لو ضاع، لازم تمسح QR من أول وجديد
     → مسح QR متكرر = إشارة حظر قوية

✅ الحل:
  1. Volume مستقل ومحمي (مش داخل الـ container)
  2. Backup تلقائي كل ساعة
  3. رفع خارج السيرفر (S3/R2) يومياً
```

```bash
#!/bin/bash
# backup-sessions.sh — شغّله بـ cron كل ساعة
BACKUP_DIR="/backups/wa-sessions"
TS=$(date +%Y-%m-%d_%H%M)

mkdir -p "$BACKUP_DIR"
tar -czf "$BACKUP_DIR/sessions_$TS.tar.gz" \
    -C /data wa-sessions/

# احتفظ بآخر 72 نسخة (3 أيام)
ls -t "$BACKUP_DIR"/sessions_*.tar.gz | tail -n +73 | xargs -r rm

# رفع يومي
if [ "$(date +%H)" = "03" ]; then
  rclone copy "$BACKUP_DIR/sessions_$TS.tar.gz" r2:wa-backups/
fi
```

---

## 7. Stack كامل بـ Docker Compose

```yaml
version: '3.9'

networks:
  wa_net:

volumes:
  pg_data:
  redis_data:
  evo_1_data:
  evo_2_data:
  chatwoot_data:
  n8n_data:

services:
  # ═══════════════ قواعد البيانات ═══════════════
  postgres:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_PASSWORD: ${PG_PASSWORD}
      POSTGRES_MULTIPLE_DATABASES: evolution,chatwoot,campaigns,n8n
    volumes:
      - pg_data:/var/lib/postgresql/data
      - ./init-db.sh:/docker-entrypoint-initdb.d/init.sh
    networks: [wa_net]
    healthcheck:
      test: ['CMD-SHELL', 'pg_isready -U postgres']
      interval: 10s

  redis:
    image: redis:7-alpine
    restart: unless-stopped
    command: redis-server --appendonly yes --maxmemory 512mb
    volumes: [redis_data:/data]
    networks: [wa_net]

  # ═══════════════ جلسات الواتساب ═══════════════
  # كل جلسة/مجموعة جلسات في container ببروكسي منفصل
  evolution-1:
    image: evoapicloud/evolution-api:latest
    restart: unless-stopped
    environment:
      SERVER_URL: http://evolution-1:8080
      AUTHENTICATION_API_KEY: ${EVO_KEY}
      DATABASE_ENABLED: 'true'
      DATABASE_PROVIDER: postgresql
      DATABASE_CONNECTION_URI: postgresql://postgres:${PG_PASSWORD}@postgres:5432/evolution?schema=evo1
      CACHE_REDIS_ENABLED: 'true'
      CACHE_REDIS_URI: redis://redis:6379/1
      # 🔒 البروكسي
      PROXY_HOST: ${PROXY_1_HOST}
      PROXY_PORT: ${PROXY_1_PORT}
      PROXY_PROTOCOL: socks5
      PROXY_USERNAME: ${PROXY_1_USER}
      PROXY_PASSWORD: ${PROXY_1_PASS}
      # 🔗 Webhooks
      WEBHOOK_GLOBAL_ENABLED: 'true'
      WEBHOOK_GLOBAL_URL: http://dispatcher:3000/webhook/evo1
      WEBHOOK_EVENTS_MESSAGES_UPSERT: 'true'
      WEBHOOK_EVENTS_MESSAGES_UPDATE: 'true'
      WEBHOOK_EVENTS_CONNECTION_UPDATE: 'true'
      WEBHOOK_EVENTS_QRCODE_UPDATED: 'true'
      # ⚙️ إعدادات سلوكية
      CONFIG_SESSION_PHONE_CLIENT: WhatsApp
      CONFIG_SESSION_PHONE_NAME: Chrome
      DEL_INSTANCE: 'false'
      QRCODE_LIMIT: '10'
      LOG_LEVEL: ERROR
    volumes: [evo_1_data:/evolution/instances]
    networks: [wa_net]
    depends_on: [postgres, redis]

  evolution-2:
    image: evoapicloud/evolution-api:latest
    restart: unless-stopped
    environment:
      SERVER_URL: http://evolution-2:8080
      AUTHENTICATION_API_KEY: ${EVO_KEY}
      DATABASE_ENABLED: 'true'
      DATABASE_PROVIDER: postgresql
      DATABASE_CONNECTION_URI: postgresql://postgres:${PG_PASSWORD}@postgres:5432/evolution?schema=evo2
      CACHE_REDIS_ENABLED: 'true'
      CACHE_REDIS_URI: redis://redis:6379/2
      PROXY_HOST: ${PROXY_2_HOST}
      PROXY_PORT: ${PROXY_2_PORT}
      PROXY_PROTOCOL: socks5
      PROXY_USERNAME: ${PROXY_2_USER}
      PROXY_PASSWORD: ${PROXY_2_PASS}
      WEBHOOK_GLOBAL_ENABLED: 'true'
      WEBHOOK_GLOBAL_URL: http://dispatcher:3000/webhook/evo2
      WEBHOOK_EVENTS_MESSAGES_UPSERT: 'true'
      WEBHOOK_EVENTS_CONNECTION_UPDATE: 'true'
      CONFIG_SESSION_PHONE_CLIENT: WhatsApp
      CONFIG_SESSION_PHONE_NAME: Edge
      LOG_LEVEL: ERROR
    volumes: [evo_2_data:/evolution/instances]
    networks: [wa_net]
    depends_on: [postgres, redis]

  # ═══════════════ الموزع الذكي ═══════════════
  dispatcher:
    build: ./dispatcher
    restart: unless-stopped
    environment:
      DATABASE_URL: postgresql://postgres:${PG_PASSWORD}@postgres:5432/campaigns
      REDIS_URL: redis://redis:6379/0
      EVO_KEY: ${EVO_KEY}
      SESSIONS_CONFIG: /config/sessions.json
      TELEGRAM_BOT_TOKEN: ${TG_TOKEN}
      TELEGRAM_CHAT_ID: ${TG_CHAT}
      TZ: Africa/Cairo
    volumes:
      - ./config:/config:ro
      - ./data:/data
    ports: ['3000:3000']
    networks: [wa_net]
    depends_on: [postgres, redis, evolution-1, evolution-2]

  # ═══════════════ صندوق وارد موحد ═══════════════
  chatwoot:
    image: chatwoot/chatwoot:latest
    restart: unless-stopped
    environment:
      POSTGRES_HOST: postgres
      POSTGRES_PASSWORD: ${PG_PASSWORD}
      POSTGRES_DATABASE: chatwoot
      REDIS_URL: redis://redis:6379/3
      SECRET_KEY_BASE: ${CW_SECRET}
      FRONTEND_URL: ${CW_URL}
    volumes: [chatwoot_data:/app/storage]
    ports: ['3001:3000']
    networks: [wa_net]
    depends_on: [postgres, redis]

  # ═══════════════ أتمتة ═══════════════
  n8n:
    image: n8nio/n8n:latest
    restart: unless-stopped
    environment:
      DB_TYPE: postgresdb
      DB_POSTGRESDB_HOST: postgres
      DB_POSTGRESDB_DATABASE: n8n
      DB_POSTGRESDB_PASSWORD: ${PG_PASSWORD}
      N8N_ENCRYPTION_KEY: ${N8N_KEY}
      GENERIC_TIMEZONE: Africa/Cairo
    volumes: [n8n_data:/home/node/.n8n]
    ports: ['5678:5678']
    networks: [wa_net]
    depends_on: [postgres]
```

### ملف `.env`
```bash
PG_PASSWORD=<كلمة سر قوية>
EVO_KEY=<مفتاح API عشوائي طويل>
CW_SECRET=<64 حرف عشوائي>
N8N_KEY=<32 حرف عشوائي>

PROXY_1_HOST=eg-mobile-1.provider.com
PROXY_1_PORT=1080
PROXY_1_USER=user1
PROXY_1_PASS=pass1

PROXY_2_HOST=eg-mobile-2.provider.com
PROXY_2_PORT=1080
PROXY_2_USER=user2
PROXY_2_PASS=pass2

TG_TOKEN=<توكن بوت تليجرام للتنبيهات>
TG_CHAT=<آيدي الشات>
```

---

## 8. التكلفة الحقيقية (Real Cost)

### سيناريو: 8 أرقام، 5000 عميل، حملة شهرية

| البند | التكلفة الشهرية |
|---|---|
| VPS (4 vCPU / 8GB) | $24 |
| Mobile Proxies × 4 | $80–160 |
| شرائح فيزيائية × 11 (8 + 3 احتياطي) | $35–90 |
| نطاق + شهادة | $2 |
| Backup Storage (R2/S3) | $2 |
| **إجمالي تقني** | **$143–278** |
| ⏰ وقت صيانة (10-20 ساعة/شهر) | ← أكبر تكلفة فعلياً |
| 💥 تعويض أرقام محظورة (2-3/شهر) | $10–30 |

### مقارنة بالرسمي
```
غير رسمي:  ~$180/شهر ثابت + وقت + مخاطرة
           السعة: ~700 رسالة/يوم = 21,000/شهر

رسمي:      5000 محادثة × $0.05 = $250 للحملة
           + $0 اشتراك (Cloud API مباشر)
           السعة: آلاف/ساعة، مفيش مخاطرة
```

> 💡 **الخلاصة الصادمة:** لو بتبعت 5000 رسالة شهرياً بس، **الرسمي أرخص وأأمن**. غير الرسمي بيبدأ يبقى منطقي اقتصادياً عند **50,000+ رسالة/شهر** — وعندها بتحتاج 30+ رقم وفريق صيانة، والمخاطرة بتزيد أُسّياً.
>
> **المنطق الحقيقي لغير الرسمي:** استخدمه للـ **Inbound والمحادثات والبوت** (مجاني وآمن نسبياً)، والرسمي للـ **Outbound الجماعي**.

---

**التالي:** [`03-ANTIBAN-BIBLE.md`](./03-ANTIBAN-BIBLE.md) — 🔥 أهم ملف في الدليل
