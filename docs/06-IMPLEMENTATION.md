# 06 — التنفيذ العملي: كود جاهز + Docker + خطة 30 يوم

> **الهدف من الملف:** تحويل كل الكلام النظري في الملفات السابقة إلى **مشروع تقدر تشغّله فعلاً**.
> هنا هيكل المشروع الكامل، الملفات الأساسية بالكود، أوامر التشغيل، وخطة زمنية مقسّمة على 30 يوم.

---

## 0. قبل أي حرف كود — 5 أسئلة لازم تجاوب عليها

| # | السؤال | لو الجواب "لا" |
|---|---|---|
| 1 | العملاء دول تعاملوا معايا فعلاً (اشتروا / سجّلوا / سألوا)؟ | **توقّف.** أي رقم هتستخدمه هيتحظر. ارجع لـ [`01-DATA-ANALYSIS.md`](./01-DATA-ANALYSIS.md) |
| 2 | عندي رقم شركة رسمي على واتساب بيزنس بيشتغل حالياً؟ | كويس — بس **ممنوع** تبعت منه حملات. inbound بس |
| 3 | عندي 6-8 شرائح فعلية (فيزيكال SIM) تحت إيدي؟ | لازم تشتريها الأول. VOIP = موت |
| 4 | مستعد أستنى **21 يوم تدفئة** قبل أول رسالة حملة؟ | لو مستعجل → استخدم الـ Cloud API الرسمي |
| 5 | فاهم إن ده مخالف لشروط استخدام واتساب؟ | اقرأ [`07-RISKS-LEGAL.md`](./07-RISKS-LEGAL.md) قبل أي خطوة |

---

## 1. هيكل المشروع الكامل

```
whatsapp-marketing/
├── docker-compose.yml
├── .env
├── .env.example
├── Makefile
│
├── db/
│   ├── 001_schema.sql              # الجداول (من 04-ARCHITECTURE)
│   ├── 002_views.sql               # الـ Views والتقارير
│   └── 003_seed.sql                # بيانات أولية (proxies, sessions)
│
├── dispatcher/                     # ❤️ القلب — الموزّع الذكي
│   ├── package.json
│   ├── Dockerfile
│   └── src/
│       ├── index.js                # entrypoint
│       ├── config.js               # كل الأرقام والحدود في مكان واحد
│       ├── db.js                   # postgres pool
│       ├── redis.js                # redis + queues
│       │
│       ├── engine/
│       │   ├── delay.js            # DelayEngine (03-ANTIBAN §3)
│       │   ├── gates.js            # GateChain — 9 بوابات
│       │   ├── router.js           # resolveSession / sticky
│       │   ├── spintax.js          # spin() + generateUnique()
│       │   └── lock.js             # session lock عبر redis
│       │
│       ├── workers/
│       │   ├── campaign.worker.js  # إرسال الحملة
│       │   ├── reply.worker.js     # الردود الواردة → OrderBot
│       │   ├── status.worker.js    # تحديثات حالة الأوردر
│       │   └── optout.worker.js    # أولوية قصوى
│       │
│       ├── bot/
│       │   ├── orderbot.js         # الـ state machine (05-ORDER-FUNNEL)
│       │   ├── states.js
│       │   ├── render.js           # شاشات القوائم الرقمية
│       │   └── arabic.js           # normalizeArabic / normalizeDigits
│       │
│       ├── health/
│       │   ├── monitor.js          # HealthMonitor
│       │   ├── softban.js          # SoftBanDetector
│       │   ├── risk.js             # RiskScorer
│       │   └── killswitch.js       # GlobalKillSwitch
│       │
│       ├── warmup/
│       │   ├── plan.js             # WARMUP_PLAN (21 يوم)
│       │   ├── scheduler.js        # WarmupScheduler
│       │   └── crosswarm.js        # تدفئة متبادلة بين أرقامنا
│       │
│       ├── evolution/
│       │   └── client.js           # wrapper على Evolution API
│       │
│       ├── notify/
│       │   └── telegram.js         # Alerter
│       │
│       └── api/
│           ├── server.js           # express — webhooks + admin
│           ├── webhook.evolution.js
│           └── admin.routes.js
│
├── analysis/                       # 🐍 Python — تحليل الداتا
│   ├── requirements.txt
│   ├── clean_phones.py             # تنظيف الأرقام
│   ├── rfm.py                      # RFM + segmentation
│   ├── basket.py                   # cross-sell
│   └── allocate.py                 # توزيع sticky على الأرقام
│
├── landing/                        # ⚡ Next.js — صفحة العرض
│   ├── package.json
│   ├── app/
│   │   ├── offer/page.tsx
│   │   └── api/orders/create/route.ts
│   └── components/CheckoutForm.tsx
│
└── scripts/
    ├── backup-sessions.sh
    ├── canary-test.js
    ├── warmup-day.js               # cron يومي
    └── daily-report.js
```

---

## 2. الملفات الجوهرية بالكود

### 2.1 `.env.example` — كل المتغيرات

```bash
# ═══════════════ عام ═══════════════
NODE_ENV=production
TZ=Africa/Cairo
LOG_LEVEL=info

# ═══════════════ قواعد البيانات ═══════════════
DATABASE_URL=postgresql://wa:CHANGE_ME@postgres:5432/wa_marketing
REDIS_URL=redis://redis:6379

# ═══════════════ Evolution API ═══════════════
EVOLUTION_API_KEY=CHANGE_ME_LONG_RANDOM_STRING
# قائمة الحاويات — الموزّع يقرأها ويوزّع عليها
EVOLUTION_NODES=http://evolution-1:8080,http://evolution-2:8080,http://evolution-3:8080

# ═══════════════ البروكسي (واحد لكل جلسة!) ═══════════════
PROXY_1_HOST=eg.mobile-proxy.example.com
PROXY_1_PORT=10001
PROXY_1_USER=user1
PROXY_1_PASS=pass1
# ... كرّر لكل جلسة

# ═══════════════ التنبيهات ═══════════════
TELEGRAM_BOT_TOKEN=CHANGE_ME
TELEGRAM_CHAT_ID=CHANGE_ME

# ═══════════════ الأمان ═══════════════
OFFER_JWT_SECRET=CHANGE_ME_64_CHARS
ADMIN_API_TOKEN=CHANGE_ME

# ═══════════════ حدود السلامة (قابلة للتعديل بدون كود) ═══════════════
CAMPAIGNS_ENABLED=true
SAFE_MAX_PER_DAY=110          # حد أقصى لكل رقم ناضج
SAFE_MAX_PER_HOUR=25
SAFE_MAX_PER_MINUTE=2
SAFE_DELAY_MEAN_MS=45000
SAFE_DELAY_STDDEV_MS=18000
SAFE_MIN_DELAY_MS=25000
SAFE_MAX_DELAY_MS=90000
SAFE_SEND_WINDOW_START=9      # 9 صباحاً
SAFE_SEND_WINDOW_END=22       # 10 مساءً
SAFE_MIN_REPLY_RATIO=0.15
SAFE_MAX_OPTOUT_RATE=0.03
SAFE_MAX_PER_PROXY_PER_MIN=3
WARMUP_DAYS=21

# ═══════════════ Chatwoot ═══════════════
CHATWOOT_URL=http://chatwoot:3000
CHATWOOT_ACCOUNT_ID=1
CHATWOOT_TOKEN=CHANGE_ME
```

> ⚠️ **قاعدة:** كل حد سلامة يكون في `.env` مش في الكود. لأنك هتحتاج تخفّضه **فوراً** وقت الخطر بدون rebuild.

---

### 2.2 `dispatcher/src/config.js` — نقطة الحقيقة الواحدة

```javascript
// config.js — كل رقم في النظام يخرج من هنا. مفيش أرقام سحرية في الكود.
require('dotenv').config();

const num  = (k, d) => Number(process.env[k] ?? d);
const bool = (k, d) => (process.env[k] ?? String(d)) === 'true';

const config = {
  env: process.env.NODE_ENV || 'development',
  tz:  process.env.TZ || 'Africa/Cairo',

  db:    { url: process.env.DATABASE_URL },
  redis: { url: process.env.REDIS_URL },

  evolution: {
    apiKey: process.env.EVOLUTION_API_KEY,
    nodes: (process.env.EVOLUTION_NODES || '').split(',').filter(Boolean),
  },

  // ── حدود السلامة ──────────────────────────────
  safe: {
    maxPerDay:      num('SAFE_MAX_PER_DAY', 110),
    maxPerHour:     num('SAFE_MAX_PER_HOUR', 25),
    maxPerMinute:   num('SAFE_MAX_PER_MINUTE', 2),

    delayMeanMs:    num('SAFE_DELAY_MEAN_MS', 45_000),
    delayStdDevMs:  num('SAFE_DELAY_STDDEV_MS', 18_000),
    minDelayMs:     num('SAFE_MIN_DELAY_MS', 25_000),
    maxDelayMs:     num('SAFE_MAX_DELAY_MS', 90_000),

    windowStart:    num('SAFE_SEND_WINDOW_START', 9),
    windowEnd:      num('SAFE_SEND_WINDOW_END', 22),

    minReplyRatio:  num('SAFE_MIN_REPLY_RATIO', 0.15),
    maxOptOutRate:  num('SAFE_MAX_OPTOUT_RATE', 0.03),
    maxPerProxyMin: num('SAFE_MAX_PER_PROXY_PER_MIN', 3),

    warmupDays:     num('WARMUP_DAYS', 21),

    // الدفعات
    batchMin:       num('SAFE_BATCH_MIN', 12),
    batchMax:       num('SAFE_BATCH_MAX', 22),
    restMinMs:      num('SAFE_REST_MIN_MS', 25 * 60_000),
    restMaxMs:      num('SAFE_REST_MAX_MS', 90 * 60_000),
    longRestEvery:  num('SAFE_LONG_REST_EVERY', 3),
    longRestMs:     num('SAFE_LONG_REST_MS', 3 * 3600_000),
  },

  // ── حدود الإيقاف الطارئ ───────────────────────
  kill: {
    maxCriticalSessions: num('KILL_MAX_CRITICAL', 2),
    maxDangerRatio:      num('KILL_MAX_DANGER_RATIO', 0.30),
    maxBansPerDay:       num('KILL_MAX_BANS_DAY', 2),
    maxQrPer24h:         num('KILL_MAX_QR_24H', 3),
  },

  jwtSecret:  process.env.OFFER_JWT_SECRET,
  adminToken: process.env.ADMIN_API_TOKEN,

  telegram: {
    token:  process.env.TELEGRAM_BOT_TOKEN,
    chatId: process.env.TELEGRAM_CHAT_ID,
  },

  chatwoot: {
    url:       process.env.CHATWOOT_URL,
    accountId: process.env.CHATWOOT_ACCOUNT_ID,
    token:     process.env.CHATWOOT_TOKEN,
  },

  // مفتاح تعطيل شامل — تقدر تقفل الحملات بـ env بدون كود
  campaignsEnabled: bool('CAMPAIGNS_ENABLED', true),
};

// ── تحقق عند البدء: لو ناقص حاجة حرجة، لا تشتغل ──
const required = ['DATABASE_URL', 'REDIS_URL', 'EVOLUTION_API_KEY', 'OFFER_JWT_SECRET'];
const missing = required.filter((k) => !process.env[k]);
if (missing.length) {
  console.error('[FATAL] متغيرات ناقصة:', missing.join(', '));
  process.exit(1);
}

module.exports = config;
```

---

### 2.3 `dispatcher/src/evolution/client.js` — الـ wrapper

```javascript
// client.js — كل تواصل مع Evolution API يمر من هنا.
// الميزة: لو غيّرنا المحرك (Evolution → WAHA) نعدّل ملف واحد بس.
const config = require('../config');

class EvolutionClient {
  constructor(baseUrl, apiKey = config.evolution.apiKey) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
    this.apiKey  = apiKey;
  }

  async _req(method, path, body, { timeoutMs = 30_000 } = {}) {
    const ctrl = new AbortController();
    const t = setTimeout(() => ctrl.abort(), timeoutMs);
    try {
      const res = await fetch(`${this.baseUrl}${path}`, {
        method,
        headers: { 'Content-Type': 'application/json', apikey: this.apiKey },
        body: body ? JSON.stringify(body) : undefined,
        signal: ctrl.signal,
      });
      const text = await res.text();
      let data;
      try { data = text ? JSON.parse(text) : null; } catch { data = { raw: text }; }
      if (!res.ok) {
        const err = new Error(`Evolution ${res.status}: ${String(text).slice(0, 300)}`);
        err.status = res.status;
        err.data = data;
        throw err;
      }
      return data;
    } finally {
      clearTimeout(t);
    }
  }

  // ── إدارة الجلسات ──────────────────────────────
  createInstance(instance, opts = {}) {
    return this._req('POST', '/instance/create', {
      instanceName: instance,
      qrcode: true,
      integration: 'WHATSAPP-BAILEYS',
      ...opts,
    });
  }

  connect(instance)        { return this._req('GET',    `/instance/connect/${instance}`); }
  state(instance)          { return this._req('GET',    `/instance/connectionState/${instance}`); }
  logout(instance)         { return this._req('DELETE', `/instance/logout/${instance}`); }
  deleteInstance(instance) { return this._req('DELETE', `/instance/delete/${instance}`); }

  // ── الإرسال ────────────────────────────────────
  sendText(instance, number, text, opts = {}) {
    return this._req('POST', `/message/sendText/${instance}`, {
      number,
      text,
      // إحنا بنتحكم في التأخير من برّه (DelayEngine)، فبنسيبه 0 هنا
      delay: opts.delay ?? 0,
      linkPreview: opts.linkPreview ?? false,
      ...opts.extra,
    });
  }

  sendMedia(instance, number, { mediatype, media, caption, fileName }) {
    return this._req('POST', `/message/sendMedia/${instance}`, {
      number, mediatype, media, caption, fileName,
    });
  }

  sendAudio(instance, number, audioUrl) {
    // الفويس نوت = أعلى معدل رد (3-5×). استخدمه بحكمة.
    return this._req('POST', `/message/sendWhatsAppAudio/${instance}`, {
      number, audio: audioUrl, encoding: true,
    });
  }

  // ── محاكاة السلوك البشري ───────────────────────
  setPresence(instance, number, presence /* composing | recording | paused | available */) {
    return this._req('POST', `/chat/sendPresence/${instance}`, { number, presence, delay: 0 });
  }

  markRead(instance, keys) {
    return this._req('POST', `/chat/markMessageAsRead/${instance}`, { readMessages: keys });
  }

  // ── التحقق من الأرقام (استخدمه بحذر شديد!) ────
  checkNumbers(instance, numbers) {
    return this._req('POST', `/chat/whatsappNumbers/${instance}`, { numbers });
  }

  // ── تكاملات ────────────────────────────────────
  setChatwoot(instance, payload) {
    return this._req('POST', `/chatwoot/set/${instance}`, payload);
  }

  setWebhook(instance, url, events) {
    return this._req('POST', `/webhook/set/${instance}`, {
      webhook: { enabled: true, url, events, webhookByEvents: false },
    });
  }
}

// ── مصنع: يرجّع العميل الصح للعقدة الصح ──────────
const clients = new Map();
function clientFor(nodeUrl) {
  if (!clients.has(nodeUrl)) clients.set(nodeUrl, new EvolutionClient(nodeUrl));
  return clients.get(nodeUrl);
}

// كل جلسة مرتبطة بعقدة معينة (محفوظة في sessions.node_url)
function clientForSession(session) {
  return clientFor(session.node_url);
}

module.exports = { EvolutionClient, clientFor, clientForSession };
```

---

### 2.4 `dispatcher/src/engine/lock.js` — قفل الجلسة (حرج!)

```javascript
// lock.js — بدون القفل ده، عاملين (workers) هيبعتوا من نفس الرقم في نفس اللحظة
// = رشقة رسائل = إشارة آلية واضحة = حظر.
const { redis } = require('../redis');
const crypto = require('crypto');

const LOCK_TTL_MS = 5 * 60_000; // أطول من أي إرسال منطقي

async function acquire(sessionId, { waitMs = 0 } = {}) {
  const key = `lock:session:${sessionId}`;
  const token = crypto.randomBytes(16).toString('hex');
  const deadline = Date.now() + waitMs;

  do {
    const ok = await redis.set(key, token, 'PX', LOCK_TTL_MS, 'NX');
    if (ok) return { key, token };
    if (waitMs > 0) await new Promise((r) => setTimeout(r, 500 + Math.random() * 1000));
  } while (Date.now() < deadline);

  return null; // مش قدرنا — الوظيفة ترجع للطابور
}

// الإطلاق آمن: بنتأكد إننا صاحب القفل قبل الحذف (Lua atomic)
const RELEASE_LUA = `
if redis.call("get", KEYS[1]) == ARGV[1] then
  return redis.call("del", KEYS[1])
else
  return 0
end`;

async function release(lock) {
  if (!lock) return;
  try { await redis.eval(RELEASE_LUA, 1, lock.key, lock.token); } catch (_) {}
}

// تمديد القفل لو الإرسال طوّل (heartbeat)
async function extend(lock) {
  if (!lock) return false;
  const r = await redis.eval(
    `if redis.call("get", KEYS[1]) == ARGV[1] then return redis.call("pexpire", KEYS[1], ARGV[2]) else return 0 end`,
    1, lock.key, lock.token, LOCK_TTL_MS
  );
  return r === 1;
}

module.exports = { acquire, release, extend };
```

---

### 2.5 `dispatcher/src/redis.js` — الطوابير بأولويات

```javascript
const IORedis = require('ioredis');
const { Queue } = require('bullmq');
const config = require('./config');

const redis = new IORedis(config.redis.url, { maxRetriesPerRequest: null });
const connection = redis;

// ⚠️ أقل رقم = أولوية أعلى
const PRIORITY = {
  OPTOUT:   1,  // إلغاء الاشتراك — فوري دايماً، بدون أي rate limit
  REPLY:    2,  // ردود العملاء — التأخير هنا يقتل التحويل
  STATUS:   3,  // تحديثات حالة الأوردر
  CAMPAIGN: 10, // الحملة — آخر واحد في الأولوية
};

const defaultJobOptions = {
  attempts: 3,
  backoff: { type: 'exponential', delay: 60_000 },
  removeOnComplete: { age: 7 * 24 * 3600, count: 50_000 },
  removeOnFail: { age: 30 * 24 * 3600 },
};

const queues = {
  optout:   new Queue('optout',   { connection, defaultJobOptions }),
  reply:    new Queue('reply',    { connection, defaultJobOptions }),
  status:   new Queue('status',   { connection, defaultJobOptions }),
  campaign: new Queue('campaign', { connection, defaultJobOptions }),
};

async function enqueueCampaign(job) {
  return queues.campaign.add('send', job, {
    priority: PRIORITY.CAMPAIGN,
    jobId: `camp:${job.campaignId}:${job.customerId}`, // منع التكرار على مستوى الطابور
  });
}

async function enqueueReply(job) {
  return queues.reply.add('handle', job, { priority: PRIORITY.REPLY });
}

// إيقاف كل الحملات فوراً (الـ kill switch بينادي عليها)
async function pauseAllCampaigns() { await queues.campaign.pause(); }
async function resumeCampaigns()   { await queues.campaign.resume(); }

// حذف كل وظائف عميل معيّن (لما يعمل opt-out)
async function purgeCustomerJobs(customerId) {
  const jobs = await queues.campaign.getJobs(['waiting', 'delayed', 'paused']);
  const targets = jobs.filter((j) => j.data?.customerId === customerId);
  await Promise.all(targets.map((j) => j.remove().catch(() => {})));
  return targets.length;
}

module.exports = {
  redis, connection, queues, PRIORITY,
  enqueueCampaign, enqueueReply,
  pauseAllCampaigns, resumeCampaigns, purgeCustomerJobs,
};
```

---

### 2.6 `dispatcher/src/index.js` — نقطة الدخول

```javascript
// index.js — يشغّل كل حاجة بترتيب سليم ويقفل بنظافة.
const config = require('./config');
const { pool } = require('./db');
const { redis, queues } = require('./redis');
const { startApiServer } = require('./api/server');
const { HealthMonitor } = require('./health/monitor');
const { GlobalKillSwitch } = require('./health/killswitch');
const { Alerter } = require('./notify/telegram');

const startCampaignWorker = require('./workers/campaign.worker');
const startReplyWorker    = require('./workers/reply.worker');
const startStatusWorker   = require('./workers/status.worker');
const startOptOutWorker   = require('./workers/optout.worker');

const alerter    = new Alerter();
const killSwitch = new GlobalKillSwitch({ alerter });
const monitor    = new HealthMonitor({ alerter, killSwitch });

const workers = [];
let server;

async function main() {
  console.log(`[boot] بدء التشغيل — env=${config.env} tz=${config.tz}`);

  // 1. تأكد إن قواعد البيانات شغالة قبل أي حاجة
  await pool.query('SELECT 1');
  await redis.ping();
  console.log('[boot] ✅ postgres + redis');

  // 2. لو الـ kill switch كان مفعّل قبل الـ restart، سيبه مفعّل!
  //    خطأ شائع: الـ restart يلغي الحماية ويرجّع الإرسال في وقت الخطر.
  if (await killSwitch.isEngaged()) {
    console.warn('[boot] ⛔ الـ Kill Switch مفعّل — الحملات موقوفة');
    await queues.campaign.pause();
  }

  // 3. شغّل العاملين (workers)
  workers.push(startOptOutWorker());   // الأول دايماً
  workers.push(startReplyWorker());
  workers.push(startStatusWorker());
  if (config.campaignsEnabled) {
    workers.push(startCampaignWorker());
  } else {
    console.warn('[boot] ⚠️ CAMPAIGNS_ENABLED=false — مفيش إرسال حملات');
  }
  console.log(`[boot] ✅ ${workers.length} workers`);

  // 4. مراقبة الصحة — كل دقيقة
  monitor.start(60_000);
  console.log('[boot] ✅ health monitor');

  // 5. سيرفر الـ webhooks والإدارة
  server = await startApiServer({ monitor, killSwitch, alerter });
  console.log('[boot] ✅ api server on :3100');

  await alerter.send('info', '🚀 الموزّع اشتغل', {
    workers: workers.length,
    campaigns: config.campaignsEnabled ? 'شغالة' : 'موقوفة',
  });
}

// ── إغلاق نظيف: مهم جداً! القفل المعلّق يوقف جلسة كاملة ──
async function shutdown(signal) {
  console.log(`[shutdown] ${signal} — بإغلاق نظيف...`);
  try {
    monitor.stop();
    if (server) await new Promise((r) => server.close(r));
    // نستنى العاملين يخلّصوا اللي في إيدهم (بدون قطع نص إرسال)
    await Promise.all(workers.map((w) => w.close()));
    await Promise.all(Object.values(queues).map((q) => q.close()));
    await redis.quit();
    await pool.end();
    console.log('[shutdown] ✅ تم');
    process.exit(0);
  } catch (e) {
    console.error('[shutdown] خطأ:', e);
    process.exit(1);
  }
}

process.on('SIGTERM', () => shutdown('SIGTERM'));
process.on('SIGINT',  () => shutdown('SIGINT'));

process.on('unhandledRejection', async (e) => {
  console.error('[unhandledRejection]', e);
  await alerter.send('error', 'unhandledRejection', { msg: String(e?.message || e) });
});

main().catch(async (e) => {
  console.error('[boot] فشل:', e);
  await alerter.send('critical', '❌ الموزّع فشل في التشغيل', { msg: String(e?.message || e) });
  process.exit(1);
});
```

---

### 2.7 `dispatcher/src/api/webhook.evolution.js` — استقبال الواردة

```javascript
// كل رسالة واردة من العميل تدخل من هنا.
// ⚠️ قاعدة: الـ webhook لازم يرد 200 في أقل من ثانية. أي شغل تقيل → الطابور.
const express = require('express');
const { queues, PRIORITY, enqueueReply } = require('../redis');
const { pool } = require('../db');
const { isOptOut } = require('../bot/arabic');
const config = require('../config');

const router = express.Router();

// تحقق بسيط من الهوية — منع أي حد يزقّ رسائل وهمية
function verify(req, res, next) {
  if (req.headers['apikey'] !== config.evolution.apiKey) {
    return res.status(401).json({ error: 'unauthorized' });
  }
  next();
}

router.post('/evolution/:instance', verify, async (req, res) => {
  // 1. رد فوري — قبل أي معالجة
  res.status(200).json({ ok: true });

  const instance = req.params.instance;
  const { event, data } = req.body || {};

  try {
    switch (event) {
      case 'messages.upsert':    await onMessage(instance, data);       break;
      case 'messages.update':    await onMessageStatus(instance, data); break;
      case 'connection.update':  await onConnection(instance, data);    break;
      default: break; // نتجاهل الباقي
    }
  } catch (e) {
    console.error(`[webhook:${instance}] ${event}:`, e.message);
  }
});

async function onMessage(instance, data) {
  const msgs = Array.isArray(data) ? data : [data];

  for (const m of msgs) {
    if (m?.key?.fromMe) continue;              // رسالتنا إحنا
    const jid = m.key?.remoteJid || '';
    if (jid.endsWith('@g.us')) continue;       // جروب — نتجاهل
    if (jid === 'status@broadcast') continue;

    const phone = jid.split('@')[0];
    const text =
      m.message?.conversation ||
      m.message?.extendedTextMessage?.text ||
      m.message?.imageMessage?.caption ||
      m.message?.buttonsResponseMessage?.selectedDisplayText ||
      '';

    // 2. سجّل كل وارد — ده أساس حساب reply ratio
    await pool.query(
      `INSERT INTO message_log (phone, session_instance, direction, body, status, created_at)
       VALUES ($1, $2, 'in', $3, 'delivered', now())`,
      [phone, instance, text.slice(0, 4000)]
    );

    // 3. حدّث آخر تفاعل — بيقلّل درجة خطورة العميل في DelayEngine
    await pool.query(
      `UPDATE customers SET last_inbound_at = now(), inbound_count = inbound_count + 1
        WHERE phone = $1`, [phone]
    );

    // 4. ⛔ الأولوية القصوى: إلغاء الاشتراك
    if (isOptOut(text)) {
      await queues.optout.add('handle', { phone, instance, text }, {
        priority: PRIORITY.OPTOUT, attempts: 5,
      });
      continue;
    }

    // 5. الباقي → البوت
    await enqueueReply({
      phone, instance, text,
      messageKey: m.key,
      hasMedia: !!(m.message?.imageMessage || m.message?.documentMessage),
      location: m.message?.locationMessage || null,
      receivedAt: Date.now(),
    });
  }
}

// تحديثات حالة الرسائل الصادرة → أساس كشف الـ soft ban
async function onMessageStatus(instance, data) {
  const arr = Array.isArray(data) ? data : [data];
  const MAP = { PENDING: 'sent', SERVER_ACK: 'sent', DELIVERY_ACK: 'delivered', READ: 'read', PLAYED: 'read' };

  for (const u of arr) {
    const st = MAP[u.status];
    if (!st) continue;
    await pool.query(
      `UPDATE message_log
          SET status = $1,
              delivered_at = CASE WHEN $1 IN ('delivered','read') AND delivered_at IS NULL THEN now() ELSE delivered_at END,
              read_at      = CASE WHEN $1 = 'read' AND read_at IS NULL THEN now() ELSE read_at END
        WHERE wa_message_id = $2`,
      [st, u.key?.id]
    );
  }
}

async function onConnection(instance, data) {
  const state = data?.state || data?.connection;
  await pool.query(
    `INSERT INTO session_events (session_instance, event_type, payload, created_at)
     VALUES ($1, 'connection', $2, now())`,
    [instance, JSON.stringify(data)]
  );
  // المراقب (HealthMonitor) هو اللي بياخد القرار — إحنا بنسجّل بس
  console.log(`[conn:${instance}] ${state}`);
}

module.exports = router;
```

---

### 2.8 `scripts/warmup-day.js` — الكرون اليومي

```javascript
#!/usr/bin/env node
// يشتغل كل يوم 8:30 صباحاً. بيعمل 4 حاجات:
//  1. يحسب حدود اليوم لكل جلسة حسب يوم التدفئة
//  2. يصفّر العدادات اليومية
//  3. يشغّل التدفئة المتبادلة بين أرقامنا
//  4. يرقّي الجلسات الجاهزة من warming → active
const { pool } = require('../dispatcher/src/db');
const { WarmupScheduler } = require('../dispatcher/src/warmup/scheduler');
const { crossWarm } = require('../dispatcher/src/warmup/crosswarm');
const { Alerter } = require('../dispatcher/src/notify/telegram');

const alerter = new Alerter();

async function run() {
  const { rows: sessions } = await pool.query(
    `SELECT * FROM sessions WHERE status NOT IN ('banned','retired')`
  );

  const report = [];

  for (const s of sessions) {
    const sched  = new WarmupScheduler(s);
    const limits = sched.getLimits();

    // 1+2. تحديث الحدود وتصفير العدادات
    await pool.query(
      `UPDATE sessions
          SET daily_quota      = $1,
              hourly_quota     = $2,
              sent_today       = 0,
              allowed_audience = $3,
              status           = $4
        WHERE id = $5`,
      [limits.out, limits.hourly, limits.audience, sched.nextStatus(), s.id]
    );

    report.push({
      instance: s.instance_name,
      day: s.warmup_day,
      quota: limits.out,
      audience: limits.audience,
      status: sched.nextStatus(),
      risk: s.risk_score,
    });

    // 3. التدفئة المتبادلة — للجلسات اللي لسه في مرحلة warming
    if (sched.needsCrossWarm()) {
      // ملاحظة: مش بننتظرها — بتشتغل في الخلفية على مدار اليوم
      crossWarm(s, sessions.filter((x) => x.id !== s.id && x.status !== 'banned'))
        .catch((e) => console.error(`[crosswarm:${s.instance_name}]`, e.message));
    }
  }

  // 4. تقرير للتليجرام
  const lines = report.map((r) =>
    `${statusIcon(r.status)} ${r.instance} — يوم ${r.day} | حد ${r.quota} | ${r.audience} | خطر ${r.risk}`
  );

  await alerter.send('info', '☀️ خطة اليوم', { text: lines.join('\n') });
  console.log(lines.join('\n'));
}

function statusIcon(s) {
  return { warming: '🔥', active: '✅', ready: '🟢', paused: '⏸', cooldown: '❄️', degraded: '⚠️' }[s] || '•';
}

run()
  .then(() => process.exit(0))
  .catch(async (e) => {
    console.error(e);
    await alerter.send('error', 'فشل كرون التدفئة', { msg: e.message });
    process.exit(1);
  });
```

**الكرون:**
```cron
# تدفئة وحدود اليوم
30 8      * * * cd /opt/wa && node scripts/warmup-day.js       >> logs/warmup.log 2>&1
# تقرير المساء
0  23     * * * cd /opt/wa && node scripts/daily-report.js     >> logs/report.log 2>&1
# نسخ احتياطي للجلسات — كل ساعة (فقدان الجلسة = QR جديد = إشارة شك)
0  *      * * * /opt/wa/scripts/backup-sessions.sh             >> logs/backup.log 2>&1
# استرجاع السلات المتروكة — 3 مرات باليوم بس
0  12,17,20 * * * cd /opt/wa && node scripts/abandoned-cart.js >> logs/cart.log 2>&1
```

---

### 2.9 `Makefile` — أوامر التشغيل

```makefile
.PHONY: help up down logs db-init seed qr status canary report backup shell-db panic resume

help:
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-16s\033[0m %s\n", $$1, $$2}'

up:            ## تشغيل كل الخدمات
	docker compose up -d
	@echo "⏳ استنى 30 ثانية للـ postgres..."
	@sleep 30
	@$(MAKE) status

down:          ## إيقاف (الداتا محفوظة)
	docker compose down

logs:          ## متابعة لوجات الموزّع
	docker compose logs -f dispatcher

db-init:       ## إنشاء الجداول
	docker compose exec -T postgres psql -U wa -d wa_marketing < db/001_schema.sql
	docker compose exec -T postgres psql -U wa -d wa_marketing < db/002_views.sql
	@echo "✅ الجداول اتعملت"

seed:          ## إدخال البروكسيات والجلسات
	docker compose exec -T postgres psql -U wa -d wa_marketing < db/003_seed.sql

qr:            ## عرض QR لجلسة — make qr S=wa-01
	@curl -s -H "apikey: $$EVOLUTION_API_KEY" \
	  http://localhost:8081/instance/connect/$(S) | jq -r '.base64' \
	  | sed 's/^data:image\/png;base64,//' | base64 -d > /tmp/qr-$(S).png
	@echo "📱 امسح: /tmp/qr-$(S).png"

status:        ## حالة كل الجلسات
	@docker compose exec -T postgres psql -U wa -d wa_marketing \
	  -c "SELECT instance_name, status, warmup_day, sent_today, daily_quota, risk_score, health_score FROM v_session_dashboard ORDER BY risk_score DESC;"

canary:        ## اختبار الكناري قبل الحملة — make canary S=wa-01
	docker compose exec dispatcher node scripts/canary-test.js --session=$(S)

report:        ## تقرير الحملة
	@docker compose exec -T postgres psql -U wa -d wa_marketing -c "SELECT * FROM v_campaign_funnel;"

backup:        ## نسخة احتياطية فورية
	./scripts/backup-sessions.sh

shell-db:      ## دخول psql
	docker compose exec postgres psql -U wa -d wa_marketing

panic:         ## ⛔ إيقاف كل الحملات فوراً
	@docker compose exec dispatcher node -e "require('./src/redis').pauseAllCampaigns().then(()=>console.log('⛔ الحملات موقوفة'))"
	@echo "لإعادة التشغيل: make resume"

resume:        ## ▶️ إعادة تشغيل الحملات
	@docker compose exec dispatcher node -e "require('./src/redis').resumeCampaigns().then(()=>console.log('▶️ الحملات شغالة'))"
```

> 💡 **الأمر الأهم في الملف ده هو `make panic`.**
> اعرفه بالغلط، وجرّبه قبل أي حملة حقيقية.
> لو مش عارف تقفل النظام في 5 ثواني، أنت مش جاهز تشغّله.

---

### 2.10 `scripts/canary-test.js` — الاختبار الإجباري

```javascript
#!/usr/bin/env node
// ⚠️ ممنوع تشغيل حملة على رقم قبل ما يعدّي الكناري.
// الفكرة: 20 رسالة لأفضل 20 عميل، نستنى 6 ساعات، نقرأ الأرقام.
const { pool } = require('../dispatcher/src/db');
const { enqueueCampaign } = require('../dispatcher/src/redis');
const { Alerter } = require('../dispatcher/src/notify/telegram');

const alerter = new Alerter();
const CANARY_SIZE = 20;
const WAIT_HOURS  = 6;

// الحدود اللي لو كسرناها = الرقم مش جاهز
const THRESHOLDS = {
  minDelivered: 0.90,  // 18 من 20 لازم توصل
  minReplied:   0.10,  // على الأقل 2 يردوا
  maxBlocked:   0.00,  // صفر بلوك — أي بلوك واحد في champions = إنذار أحمر
  maxOptOut:    0.05,
};

async function launch(instance) {
  const { rows: found } = await pool.query(
    `SELECT * FROM sessions WHERE instance_name = $1`, [instance]
  );
  const s = found[0];
  if (!s) throw new Error(`جلسة غير موجودة: ${instance}`);

  if (s.warmup_day < 14) {
    throw new Error(`❌ الرقم في يوم ${s.warmup_day} — لازم 14 يوم على الأقل قبل الكناري`);
  }
  if (['banned', 'degraded'].includes(s.status)) {
    throw new Error(`❌ حالة الجلسة: ${s.status}`);
  }

  // ✅ أفضل العملاء فقط — أعلى احتمال رد، أقل احتمال بلاغ
  const { rows: targets } = await pool.query(
    `SELECT c.id, c.phone, c.name, c.segment
       FROM customers c
      WHERE c.segment IN ('champions','loyal')
        AND c.opt_in = true
        AND c.phone NOT IN (SELECT phone FROM suppression_list)
        AND c.last_outbound_at IS NULL
      ORDER BY c.rfm_score DESC
      LIMIT $1`, [CANARY_SIZE]
  );

  if (targets.length < CANARY_SIZE) {
    throw new Error(`❌ عملاء غير كافيين: ${targets.length}/${CANARY_SIZE}`);
  }

  const { rows: [camp] } = await pool.query(
    `INSERT INTO campaigns (name, status, is_canary, target_count, created_at)
     VALUES ($1, 'canary', true, $2, now()) RETURNING id`,
    [`canary-${instance}-${new Date().toISOString().slice(0, 10)}`, CANARY_SIZE]
  );

  for (const t of targets) {
    await enqueueCampaign({
      campaignId: camp.id,
      customerId: t.id,
      phone: t.phone,
      name: t.name,
      segment: t.segment,
      forceSession: s.id,   // إجبار الجلسة — ده اختبار للرقم ده تحديداً
      isCanary: true,
    });
  }

  await alerter.send('info', `🐤 كناري بدأ — ${instance}`, {
    عملاء: CANARY_SIZE,
    'قراءة النتيجة بعد': `${WAIT_HOURS} ساعات`,
    الأمر: `node scripts/canary-test.js --evaluate --campaign=${camp.id}`,
  });

  console.log(`✅ كناري #${camp.id} بدأ. قيّمه بعد ${WAIT_HOURS} ساعات.`);
  return camp.id;
}

async function evaluate(campaignId) {
  const { rows: [m] } = await pool.query(
    `SELECT
        COUNT(*)                                               AS sent,
        COUNT(*) FILTER (WHERE status IN ('delivered','read'))  AS delivered,
        COUNT(*) FILTER (WHERE status = 'read')                 AS read,
        COUNT(*) FILTER (WHERE status = 'failed')               AS failed,
        COUNT(*) FILTER (WHERE status = 'blocked')              AS blocked,
        COUNT(DISTINCT phone) FILTER (WHERE has_reply)          AS replied
       FROM (
         SELECT ml.*, EXISTS(
           SELECT 1 FROM message_log r
            WHERE r.phone = ml.phone AND r.direction = 'in' AND r.created_at > ml.created_at
         ) AS has_reply
           FROM message_log ml
          WHERE ml.campaign_id = $1 AND ml.direction = 'out'
       ) x`,
    [campaignId]
  );

  const { rows: [o] } = await pool.query(
    `SELECT COUNT(*) AS n FROM suppression_list WHERE campaign_id = $1`, [campaignId]
  );

  const sent = Number(m.sent) || 1;
  const r = {
    sent,
    deliveredRate: Number(m.delivered) / sent,
    readRate:      Number(m.read)      / sent,
    replyRate:     Number(m.replied)   / sent,
    blockedRate:   Number(m.blocked)   / sent,
    optOutRate:    Number(o.n)         / sent,
    failed:        Number(m.failed),
  };

  const fails = [];
  if (r.deliveredRate < THRESHOLDS.minDelivered) fails.push(`تسليم ${pct(r.deliveredRate)} < ${pct(THRESHOLDS.minDelivered)}`);
  if (r.replyRate     < THRESHOLDS.minReplied)   fails.push(`ردود ${pct(r.replyRate)} < ${pct(THRESHOLDS.minReplied)}`);
  if (r.blockedRate   > THRESHOLDS.maxBlocked)   fails.push(`بلوك ${pct(r.blockedRate)} — أي بلوك من champions = خطر`);
  if (r.optOutRate    > THRESHOLDS.maxOptOut)    fails.push(`إلغاء ${pct(r.optOutRate)} > ${pct(THRESHOLDS.maxOptOut)}`);

  const passed = fails.length === 0;

  console.log('\n═══ نتيجة الكناري ═══');
  console.log(`مرسل:   ${r.sent}`);
  console.log(`تسليم:  ${pct(r.deliveredRate)}`);
  console.log(`قراءة:  ${pct(r.readRate)}`);
  console.log(`ردود:   ${pct(r.replyRate)}`);
  console.log(`بلوك:   ${pct(r.blockedRate)}`);
  console.log(`إلغاء:  ${pct(r.optOutRate)}`);
  console.log(`فشل:    ${r.failed}`);
  console.log(passed ? '\n✅ نجح — تقدر تكبّر تدريجياً' : `\n❌ فشل:\n - ${fails.join('\n - ')}`);

  if (!passed) {
    console.log('\n🛑 لا تكبّر. المشكلة في الرسالة أو الجمهور، مش في الكود.');
  }

  await alerter.send(passed ? 'info' : 'critical',
    passed ? '✅ الكناري نجح' : '❌ الكناري فشل',
    { المشاكل: fails.join(' | ') || 'لا شيء', تسليم: pct(r.deliveredRate), ردود: pct(r.replyRate) }
  );

  return { passed, metrics: r, fails };
}

const pct = (n) => `${(n * 100).toFixed(1)}%`;

// ── CLI ──
const args = Object.fromEntries(process.argv.slice(2).map((a) => {
  const [k, v] = a.replace(/^--/, '').split('=');
  return [k, v ?? true];
}));

(async () => {
  try {
    if (args.evaluate) await evaluate(Number(args.campaign));
    else await launch(args.session);
    process.exit(0);
  } catch (e) {
    console.error('❌', e.message);
    process.exit(1);
  }
})();
```

---

## 3. خطوات التشغيل الأولى (من صفر لأول رسالة)

```bash
# ═══ 1. تجهيز السيرفر ═══
# VPS: 8 vCPU / 16GB RAM / 200GB SSD — أوروبا أو الشرق الأوسط
ssh root@your-server
apt update && apt install -y docker.io docker-compose-plugin jq make git
mkdir -p /opt/wa && cd /opt/wa

# ═══ 2. المشروع ═══
git clone <your-repo> .
cp .env.example .env
nano .env                     # ⚠️ املأ كل CHANGE_ME

# ═══ 3. البنية التحتية ═══
make up                       # postgres + redis + evolution + chatwoot + n8n
make db-init
make seed

# ═══ 4. تحقق من عزل الشبكة (حرج!) ═══
# كل حاوية Evolution لازم تطلع من IP مختلف
for i in 1 2 3; do
  echo -n "evolution-$i: "
  docker compose exec -T evolution-$i curl -s --max-time 10 ifconfig.me
  echo
done
# ❌ لو أي اتنين طلعوا بنفس الـ IP → وقّف كل حاجة وصلّح الـ Gluetun

# ═══ 5. إنشاء الجلسات وربط الأرقام ═══
make qr S=wa-01               # امسح بالموبايل اللي عليه الشريحة
# ⚠️ مهم: سيب الواتساب مفتوح على الموبايل. متعملش logout منه.
make qr S=wa-02
# ... لكل رقم

# ═══ 6. تحقق ═══
make status
# كل الجلسات لازم تكون: status=warming, warmup_day=1

# ═══ 7. ⏳ استنى 21 يوم ═══
# التدفئة بتشتغل أوتوماتيك من الكرون. مفيش حملة خلال الفترة دي.
# راقب التليجرام كل يوم.

# ═══ 8. يوم 15+ — الكناري ═══
make canary S=wa-01
# بعد 6 ساعات:
docker compose exec dispatcher node scripts/canary-test.js --evaluate --campaign=1

# ═══ 9. لو الكناري نجح — أول حملة صغيرة ═══
docker compose exec dispatcher node scripts/launch-campaign.js \
  --segment=champions --limit=50 --sessions=wa-01,wa-02
```

---

## 4. خطة الـ 30 يوم

### 🗓️ الأسبوع 1 — التحضير (بدون أي إرسال)

| اليوم | المهام | مخرَج قابل للقياس |
|---|---|---|
| **1** | تنظيف الداتا: `clean_phones.py` → تقرير المكرر/الخطأ | ملف CSV نظيف + عدد الأرقام الصالحة |
| **2** | RFM + التقسيم: `rfm.py` → 8 سيجمنتس | جدول `customers` معمور بـ `segment` |
| **3** | شراء الشرائح (6-8 فيزيكال) + تسجيلها على موبايلات حقيقية | 8 أرقام واتساب شغالة يدوياً |
| **4** | تجهيز السيرفر + `make up` + `db-init` + تحقق عزل الـ IP | كل حاوية بـ IP مختلف ✅ |
| **5** | ربط الجلسات (`make qr`) + Chatwoot + التليجرام | 8 جلسات `status=warming` |
| **6** | كتابة الرسائل: 4 صيغ لكل سيجمنت × spintax ≥ 20× العدد | `variationCount()` ≥ 20 × المستلمين |
| **7** | اختبار داخلي: ابعت لنفسك ولزمايلك. اختبر `make panic` | البوت يخلّص أوردر كامل + الإيقاف الطارئ شغال |

> ⚠️ **يوم 3 هو أهم يوم في الخطة.**
> التسجيل من موبايل حقيقي، على واي فاي عادي، وبعدين استخدام طبيعي لأسبوع — ده الفرق الحقيقي
> بين رقم يعيش شهور ورقم يموت في يومين.

### 🗓️ الأسبوع 2 — التدفئة النشطة (لسه بدون حملات)

| اليوم | المهام | المؤشر |
|---|---|---|
| **8-10** | استخدام بشري: محادثات، فويس نوت، مكالمة واتساب، انضمام لجروبين | `warmup_day=3-5`, صفر outbound آلي |
| **11-12** | ربط الجلسة بـ Baileys/Evolution + `crossWarm()` بين أرقامنا | تدفئة متبادلة شغالة، الجلسة مستقرة 48 ساعة |
| **13-14** | inbound فقط: خلّي البوت يرد على الرسائل الواردة | `reply ratio` طبيعي، صفر disconnect |

### 🗓️ الأسبوع 3 — الكناري والتوسّع الحذر

| اليوم | المهام | بوابة القرار |
|---|---|---|
| **15** | كناري على رقم واحد: 20 رسالة لأفضل champions | استنى 6 ساعات |
| **16** | تقييم الكناري | ❌ فشل → ارجع للتدفئة وعدّل الرسالة. ✅ نجح → كمّل |
| **17** | 30 رسالة/رقم على رقمين — champions فقط | reply > 15%، opt-out < 2% |
| **18** | 50 رسالة/رقم على 4 أرقام — champions + loyal | delivery > 92% |
| **19-20** | 70 رسالة/رقم على 6 أرقام | risk score < 40 لكل الجلسات |
| **21** | مراجعة شاملة + قرار التوسّع | كل المؤشرات خضراء؟ |

### 🗓️ الأسبوع 4 — التشغيل الكامل

| اليوم | المهام | الإنتاجية |
|---|---|---|
| **22-24** | 8 أرقام × 90-110 رسالة/يوم — السيجمنتس النشطة | ~800-880 رسالة/يوم |
| **25-27** | الانتقال للسيجمنتس الأصعب (`at_risk`, `need_attention`) | ⚠️ راقب opt-out بدقة — هيرتفع هنا |
| **28** | `hibernating` — الأخير دايماً وبأقل حجم | لو opt-out > 3% → وقّف السيجمنت ده |
| **29** | استرجاع السلات المتروكة + متابعة الأوردرات | تحويل نهائي |
| **30** | تقرير كامل: `v_funnel_by_segment` + دروس مستفادة | قرار: نكمّل غير رسمي؟ ننقل للرسمي؟ هجين؟ |

---

## 5. الحسابات الواقعية

```
8 أرقام × 105 رسالة/يوم (متوسط ناضج)  =  840 رسالة/يوم
5,000 عميل ÷ 840                       ≈  6 أيام إرسال

لكن مع:
  - أيام راحة إجبارية بعد أي إنذار
  - جلسة أو اتنين هيسقطوا (خطّط لـ 20% خسارة)
  - سيجمنتس متأجّلة

الواقع:  8-12 يوم لتغطية 5,000 عميل
```

**متوقّع من 5,000 رسالة (لعملاء فعليين، رسالة كويسة):**

| المرحلة | النسبة | العدد |
|---|---|---|
| مرسل | 100% | 5,000 |
| مُسلَّم | 94% | 4,700 |
| مقروء | 78% | 3,900 |
| ردّ | 16% | 800 |
| دخل مسار الطلب | 9% | 450 |
| **أوردر مكتمل** | **4%** | **200** |
| ألغى الاشتراك | 1.5% | 75 |

> لو الأرقام بتاعتك أقل من نص ده بكتير، المشكلة **مش في الكود**.
> المشكلة في الجمهور أو الرسالة أو العرض.

---

## 6. أخطاء التنفيذ الشائعة (كل واحدة قتلت رقم فعلاً)

| # | الخطأ | النتيجة | العلاج |
|---|---|---|---|
| 1 | تشغيل الحملة يوم 1 "بس للتجربة" | حظر في ساعات | التدفئة إجبارية، بدون استثناء |
| 2 | نسيان قفل الجلسة (`lock.js`) | عاملين يبعتوا معاً = رشقة | القفل قبل أي إرسال |
| 3 | `docker restart` وسط حملة | أقفال معلّقة + جلسات معلّقة | `make panic` الأول، بعدين restart |
| 4 | بروكسي واحد لكل الحاويات | Cluster Ban — كل الأرقام مع بعض | تحقق `ifconfig.me` لكل حاوية |
| 5 | بروكسي rotating | الجلسة تتقطع كل شوية = QR متكرر | sticky/static فقط |
| 6 | فينجربرنت متغيّر كل restart | إشارة شك قوية | `fingerprintFor(id)` deterministic |
| 7 | لينك في أول رسالة | معدل بلاغ أعلى بكتير | اللينك في الرسالة التانية |
| 8 | تجاهل opt-out لـ 10 دقايق | بلاغ spam مؤكد | طابور `optout` بأولوية 1 |
| 9 | البدء بـ `hibernating` (أكبر سيجمنت) | reply ratio ≈ 0 → موت | ابدأ بـ champions دايماً |
| 10 | مفيش نسخ احتياطي للجلسات | فقدان الجلسة = QR جديد | `backup-sessions.sh` كل ساعة |
| 11 | حدود السلامة في الكود مش `.env` | مش قادر تخفّض بسرعة وقت الخطر | كله في `.env` |
| 12 | تحقق أرقام (`checkNumbers`) على رقم مهم | الرقم اللي بيتحقق يُحظر | رقم مستهلك مخصص، 200/يوم أقصى |
| 13 | مسح QR أكثر من 3 مرات في اليوم | إشارة اختراق حساب | حد 3، بعدها `degraded` |
| 14 | استخدام رقم الشركة الرسمي للحملة | تخسر أهم أصل عندك | inbound فقط، أبداً outbound |

---

## 7. المراقبة اليومية — 5 أرقام بس

كل يوم، بص على 5 أرقام. لو أي واحد خرج عن المدى، وقّف.

```sql
SELECT
  -- 1. أهم رقم في النظام كله
  ROUND(100.0 * COUNT(DISTINCT phone) FILTER (WHERE direction='in')
      / NULLIF(COUNT(DISTINCT phone) FILTER (WHERE direction='out'),0), 1) AS reply_pct,

  -- 2. مؤشر السلامة الأول
  ROUND(100.0 * (SELECT COUNT(*) FROM suppression_list WHERE created_at > now()-interval '1 day')
      / NULLIF(COUNT(*) FILTER (WHERE direction='out'),0), 2) AS optout_pct,

  -- 3. كشف الـ soft ban
  ROUND(100.0 * COUNT(*) FILTER (WHERE direction='out' AND status IN ('delivered','read'))
      / NULLIF(COUNT(*) FILTER (WHERE direction='out'),0), 1) AS delivery_pct,

  -- 4. الرسائل العالقة على صح واحدة
  ROUND(100.0 * COUNT(*) FILTER (WHERE direction='out' AND status='sent'
        AND created_at < now()-interval '2 hours')
      / NULLIF(COUNT(*) FILTER (WHERE direction='out'),0), 1) AS stuck_pct,

  -- 5. أعلى درجة خطر
  (SELECT MAX(risk_score) FROM sessions WHERE status NOT IN ('banned','retired')) AS max_risk
FROM message_log
WHERE created_at > now() - interval '1 day';
```

| الرقم | آمن | إنذار | ⛔ وقّف فوراً |
|---|---|---|---|
| `reply_pct` | > 15% | 8-15% | < 5% |
| `optout_pct` | < 1.5% | 1.5-3% | > 3% |
| `delivery_pct` | > 92% | 85-92% | < 80% |
| `stuck_pct` | < 10% | 10-25% | > 35% |
| `max_risk` | < 40 | 40-70 | > 70 |

---

## 8. الخلاصة

1. **الأسبوعين الأولين مفيش فيهم إرسال.** ده مش تأخير — ده الاستثمار الوحيد اللي بيجيب نتيجة.
2. **`make panic` قبل أي حملة.** اعرفه وجرّبه.
3. **كل حد في `.env`.** لأنك هتحتاج تخفّضه في ثواني.
4. **الكناري إجباري** على كل رقم جديد.
5. **5 أرقام مراقبة يومية.** لو واحد أحمر، وقّف — متتفاوضش مع نفسك.

---

**التالي:** [`07-RISKS-LEGAL.md`](./07-RISKS-LEGAL.md) — المخاطر الحقيقية، التكلفة المخفية، وخطة الطوارئ
