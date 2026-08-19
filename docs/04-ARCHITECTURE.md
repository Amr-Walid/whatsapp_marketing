# ⚙️ المرحلة 3: معمارية النظام (System Architecture)

> نظام توزيع ذكي متعدد الجلسات: **Multi-Session Load Balancing & Orchestration System**

---

## 1. المخطط العام

```
                        ┌───────────────────────────┐
                        │      ADMIN DASHBOARD      │
                        │  حملات · جلسات · تقارير    │
                        └─────────────┬─────────────┘
                                      │
┌─────────────────────────────────────▼──────────────────────────────────┐
│                          CONTROL PLANE                                  │
│  ┌──────────────────────┐         ┌──────────────────────────────┐     │
│  │  PostgreSQL          │         │  Redis + BullMQ              │     │
│  │  ─────────────────   │◄───────►│  ────────────────────        │     │
│  │  customers           │         │  queue:campaign              │     │
│  │  segments            │         │  queue:reply (أولوية عالية)   │     │
│  │  sessions            │         │  queue:optout (أعلى أولوية)   │     │
│  │  campaigns           │         │  queue:status_update         │     │
│  │  message_log         │         │  locks:session:*             │     │
│  │  orders              │         │  ratelimit:*                 │     │
│  │  conversations       │         │  pool:global (IP budget)     │     │
│  │  suppression_list    │         └──────────────────────────────┘     │
│  └──────────────────────┘                                              │
└─────────────────────────────┬──────────────────────────────────────────┘
                              │
                ┌─────────────▼─────────────┐
                │    SMART DISPATCHER       │
                │  ═══════════════════════  │
                │  1. QuotaGate             │  ← الحد اليومي/الساعي
                │  2. WarmupGate            │  ← يوم كام في التدفئة؟
                │  3. CircadianGate         │  ← الوقت مناسب؟
                │  4. HealthGate            │  ← صحة الجلسة
                │  5. RiskGate              │  ← Risk Score
                │  6. SuppressionGate       │  ← مش في الـ blacklist؟
                │  7. StickySessionResolver │  ← أي رقم يبعت؟
                │  8. DelayEngine           │  ← Gaussian + Circadian
                │  9. ContentGenerator      │  ← Spintax + تنويع
                │  10. Sender               │  ← الإرسال الفعلي
                └─────────────┬─────────────┘
                              │
      ┌───────────────────────┼───────────────────────┐
      ▼                       ▼                       ▼
┌──────────────┐      ┌──────────────┐      ┌──────────────┐
│ WORKER POD 1 │      │ WORKER POD 2 │      │ WORKER POD 3 │
│ ──────────── │      │ ──────────── │      │ ──────────── │
│ Evolution    │      │ Evolution    │      │ Evolution    │
│ SIM 1, 2, 3  │      │ SIM 4, 5, 6  │      │ SIM 7, 8     │
│ Proxy EG-1   │      │ Proxy EG-2   │      │ Proxy EG-3   │
│ FP: Chrome   │      │ FP: Edge     │      │ FP: Safari   │
└──────┬───────┘      └──────┬───────┘      └──────┬───────┘
       │                     │                     │
       └─────────────────────┼─────────────────────┘
                             │  Webhooks (رسائل واردة، حالة)
                             ▼
              ┌──────────────────────────────┐
              │      INBOUND ROUTER          │
              │  ────────────────────────    │
              │  • Opt-out Detector (أولوية) │
              │  • Order Bot State Machine   │
              │  • Chatwoot Sync             │
              │  • Reply Ratio Recorder      │
              │  • Human Handoff Escalation  │
              └──────────────┬───────────────┘
                             │
        ┌────────────────────┼────────────────────┐
        ▼                    ▼                    ▼
┌───────────────┐   ┌───────────────┐   ┌───────────────┐
│  Order System │   │   Chatwoot    │   │  Monitoring   │
│  + Payment    │   │ Unified Inbox │   │  + Telegram   │
└───────────────┘   └───────────────┘   └───────────────┘
```

---

## 2. مخطط قاعدة البيانات (Schema)

```sql
-- ═══════════════════════════════════════════════
--  العملاء والتقسيم
-- ═══════════════════════════════════════════════
CREATE TABLE customers (
  id              BIGSERIAL PRIMARY KEY,
  phone           VARCHAR(20)  UNIQUE NOT NULL,
  name            VARCHAR(120),
  email           VARCHAR(160),

  -- RFM
  recency_days    INT,
  frequency       INT,
  monetary        NUMERIC(12,2),
  rfm_r           SMALLINT, rfm_f SMALLINT, rfm_m SMALLINT,
  segment         VARCHAR(40),
  priority        SMALLINT DEFAULT 5,

  -- تاريخ
  first_order_at  TIMESTAMPTZ,
  last_order_at   TIMESTAMPTZ,
  last_product    VARCHAR(160),
  recommended     JSONB,

  -- opt-in
  opted_in        BOOLEAN DEFAULT FALSE,
  opted_in_at     TIMESTAMPTZ,
  opt_in_source   VARCHAR(60),
  opt_in_proof    TEXT,

  -- 🔑 Sticky session
  assigned_session VARCHAR(60),

  created_at      TIMESTAMPTZ DEFAULT NOW(),
  updated_at      TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_cust_segment ON customers(segment, priority);
CREATE INDEX idx_cust_session ON customers(assigned_session);


-- ═══════════════════════════════════════════════
--  الجلسات (الأرقام)
-- ═══════════════════════════════════════════════
CREATE TYPE session_status AS ENUM (
  'provisioning','warming','ready','active',
  'paused','cooldown','degraded','banned','retired'
);

CREATE TABLE sessions (
  id              VARCHAR(60) PRIMARY KEY,
  phone           VARCHAR(20) UNIQUE NOT NULL,
  display_name    VARCHAR(80),

  -- البنية
  worker_pod      VARCHAR(40),
  evo_instance    VARCHAR(60),
  evo_base_url    VARCHAR(200),
  proxy_id        VARCHAR(60),
  fingerprint     JSONB,

  -- الحالة
  status          session_status DEFAULT 'provisioning',
  connection      VARCHAR(20),         -- open | connecting | close
  last_seen_at    TIMESTAMPTZ,

  -- التدفئة
  registered_at   TIMESTAMPTZ NOT NULL,
  warmup_day      INT GENERATED ALWAYS AS (
                    FLOOR(EXTRACT(EPOCH FROM (NOW() - registered_at))/86400)::INT + 1
                  ) STORED,
  is_mature       BOOLEAN DEFAULT FALSE,

  -- الحدود
  daily_quota     INT DEFAULT 0,
  hourly_quota    INT DEFAULT 0,
  rate_multiplier NUMERIC(4,2) DEFAULT 1.00,

  -- الصحة
  risk_score      SMALLINT DEFAULT 0,
  risk_level      VARCHAR(12) DEFAULT 'low',
  reply_ratio_48h NUMERIC(5,4),
  delivery_rate   NUMERIC(5,4),
  opt_out_rate    NUMERIC(5,4),
  bad_mac_count   INT DEFAULT 0,
  disconnects_24h INT DEFAULT 0,

  -- التحكم
  paused_until    TIMESTAMPTZ,
  pause_reason    TEXT,
  banned_at       TIMESTAMPTZ,
  ban_code        INT,

  notes           TEXT,
  created_at      TIMESTAMPTZ DEFAULT NOW(),
  updated_at      TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_sess_status ON sessions(status) WHERE status IN ('active','ready');


-- ═══════════════════════════════════════════════
--  البروكسي
-- ═══════════════════════════════════════════════
CREATE TABLE proxies (
  id            VARCHAR(60) PRIMARY KEY,
  protocol      VARCHAR(10) NOT NULL,    -- socks5 | http
  host          VARCHAR(160) NOT NULL,
  port          INT NOT NULL,
  username      VARCHAR(80),
  password_enc  TEXT,
  kind          VARCHAR(20),             -- mobile | residential | datacenter
  country       CHAR(2),
  is_sticky     BOOLEAN DEFAULT TRUE,
  health        VARCHAR(12) DEFAULT 'ok',
  fail_count    INT DEFAULT 0,
  last_check_at TIMESTAMPTZ,
  observed_ip   VARCHAR(45),
  expires_at    TIMESTAMPTZ
);


-- ═══════════════════════════════════════════════
--  الحملات
-- ═══════════════════════════════════════════════
CREATE TYPE campaign_status AS ENUM (
  'draft','canary','running','paused','halted','completed','failed'
);

CREATE TABLE campaigns (
  id              BIGSERIAL PRIMARY KEY,
  name            VARCHAR(160) NOT NULL,
  status          campaign_status DEFAULT 'draft',

  target_segments TEXT[],
  template        TEXT NOT NULL,          -- spintax
  media_mix       JSONB,
  variables       JSONB,

  -- إعدادات الأمان
  max_per_session_day  INT DEFAULT 80,
  min_delay_ms         INT DEFAULT 45000,
  max_delay_ms         INT DEFAULT 120000,
  send_window_start    SMALLINT DEFAULT 9,
  send_window_end      SMALLINT DEFAULT 22,
  require_canary       BOOLEAN DEFAULT TRUE,

  -- تقدم
  total_targets   INT DEFAULT 0,
  sent            INT DEFAULT 0,
  delivered       INT DEFAULT 0,
  read_count      INT DEFAULT 0,
  replied         INT DEFAULT 0,
  opted_out       INT DEFAULT 0,
  failed          INT DEFAULT 0,
  orders          INT DEFAULT 0,
  revenue         NUMERIC(14,2) DEFAULT 0,

  started_at      TIMESTAMPTZ,
  completed_at    TIMESTAMPTZ,
  created_at      TIMESTAMPTZ DEFAULT NOW()
);


-- ═══════════════════════════════════════════════
--  سجل الرسائل — الجدول الأهم
-- ═══════════════════════════════════════════════
CREATE TABLE message_log (
  id            BIGSERIAL PRIMARY KEY,
  campaign_id   BIGINT REFERENCES campaigns(id),
  customer_id   BIGINT REFERENCES customers(id),
  session_id    VARCHAR(60) REFERENCES sessions(id),

  phone         VARCHAR(20) NOT NULL,
  direction     VARCHAR(4) NOT NULL,       -- out | in
  wa_message_id VARCHAR(120),

  msg_type      VARCHAR(16) DEFAULT 'text',
  content       TEXT,
  content_hash  VARCHAR(64),               -- لرصد التكرار
  media_url     TEXT,

  status        VARCHAR(16) DEFAULT 'queued',
  -- queued|sending|sent|delivered|read|failed|blocked
  error_code    VARCHAR(40),
  error_msg     TEXT,

  delay_used_ms INT,
  attempt       SMALLINT DEFAULT 1,

  queued_at     TIMESTAMPTZ DEFAULT NOW(),
  sent_at       TIMESTAMPTZ,
  delivered_at  TIMESTAMPTZ,
  read_at       TIMESTAMPTZ,
  created_at    TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_ml_session_time ON message_log(session_id, created_at DESC);
CREATE INDEX idx_ml_phone        ON message_log(phone, created_at DESC);
CREATE INDEX idx_ml_hash         ON message_log(content_hash);
CREATE INDEX idx_ml_status       ON message_log(status)
       WHERE status IN ('queued','sending','sent');


-- ═══════════════════════════════════════════════
--  المحادثات (حالة البوت)
-- ═══════════════════════════════════════════════
CREATE TABLE conversations (
  id              BIGSERIAL PRIMARY KEY,
  phone           VARCHAR(20) NOT NULL,
  session_id      VARCHAR(60) NOT NULL,
  customer_id     BIGINT REFERENCES customers(id),

  state           VARCHAR(50) DEFAULT 'idle',
  context         JSONB DEFAULT '{}',
  cart            JSONB DEFAULT '[]',

  is_bot_active   BOOLEAN DEFAULT TRUE,
  handoff_to      VARCHAR(80),
  handoff_at      TIMESTAMPTZ,

  last_in_at      TIMESTAMPTZ,
  last_out_at     TIMESTAMPTZ,
  msg_in_count    INT DEFAULT 0,
  msg_out_count   INT DEFAULT 0,
  expires_at      TIMESTAMPTZ,

  chatwoot_conv_id BIGINT,
  created_at      TIMESTAMPTZ DEFAULT NOW(),
  updated_at      TIMESTAMPTZ DEFAULT NOW(),
  UNIQUE(phone, session_id)
);
CREATE INDEX idx_conv_state ON conversations(state) WHERE state <> 'idle';


-- ═══════════════════════════════════════════════
--  الأوردرات
-- ═══════════════════════════════════════════════
CREATE TABLE orders (
  id            BIGSERIAL PRIMARY KEY,
  order_number  VARCHAR(30) UNIQUE NOT NULL,
  customer_id   BIGINT REFERENCES customers(id),
  campaign_id   BIGINT REFERENCES campaigns(id),
  session_id    VARCHAR(60),

  channel       VARCHAR(20) NOT NULL,   -- whatsapp_bot | landing_page
  items         JSONB NOT NULL,
  subtotal      NUMERIC(12,2),
  shipping      NUMERIC(12,2),
  discount      NUMERIC(12,2),
  total         NUMERIC(12,2) NOT NULL,

  customer_name VARCHAR(120),
  phone         VARCHAR(20) NOT NULL,
  address       TEXT,
  city          VARCHAR(80),
  governorate   VARCHAR(80),
  notes         TEXT,

  payment_method VARCHAR(30),           -- cod | card | wallet
  payment_status  VARCHAR(20) DEFAULT 'pending',

  status        VARCHAR(30) DEFAULT 'new',
  -- new|confirmed|preparing|shipped|out_for_delivery|delivered|cancelled|returned
  tracking_number VARCHAR(60),
  courier         VARCHAR(60),

  utm_source    VARCHAR(60),
  utm_campaign  VARCHAR(60),

  created_at    TIMESTAMPTZ DEFAULT NOW(),
  confirmed_at  TIMESTAMPTZ,
  shipped_at    TIMESTAMPTZ,
  delivered_at  TIMESTAMPTZ
);


-- ═══════════════════════════════════════════════
--  الحظر والأحداث
-- ═══════════════════════════════════════════════
CREATE TABLE suppression_list (
  phone      VARCHAR(20) PRIMARY KEY,
  reason     VARCHAR(40) NOT NULL,
  detail     TEXT,
  added_at   TIMESTAMPTZ DEFAULT NOW(),
  added_by   VARCHAR(60)
);

CREATE TABLE session_events (
  id          BIGSERIAL PRIMARY KEY,
  session_id  VARCHAR(60) NOT NULL,
  event_type  VARCHAR(50) NOT NULL,
  severity    VARCHAR(12),
  payload     JSONB,
  created_at  TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_se_session ON session_events(session_id, created_at DESC);


-- ═══════════════════════════════════════════════
--  Views مفيدة
-- ═══════════════════════════════════════════════
CREATE VIEW v_session_dashboard AS
SELECT
  s.id, s.phone, s.status, s.warmup_day, s.risk_score, s.risk_level,
  s.daily_quota,
  (SELECT COUNT(*) FROM message_log m
   WHERE m.session_id = s.id AND m.direction='out'
     AND m.created_at >= CURRENT_DATE)                        AS sent_today,
  s.daily_quota - (SELECT COUNT(*) FROM message_log m
   WHERE m.session_id = s.id AND m.direction='out'
     AND m.created_at >= CURRENT_DATE)                        AS quota_left,
  s.reply_ratio_48h, s.delivery_rate, s.opt_out_rate,
  s.connection, s.last_seen_at, s.paused_until
FROM sessions s
WHERE s.status NOT IN ('retired','banned');


CREATE VIEW v_campaign_funnel AS
SELECT
  c.id, c.name, c.status,
  c.total_targets, c.sent, c.delivered, c.read_count,
  c.replied, c.orders, c.revenue,
  ROUND(100.0*c.delivered/NULLIF(c.sent,0),   2) AS delivery_pct,
  ROUND(100.0*c.read_count/NULLIF(c.delivered,0),2) AS read_pct,
  ROUND(100.0*c.replied/NULLIF(c.sent,0),     2) AS reply_pct,
  ROUND(100.0*c.orders/NULLIF(c.sent,0),      2) AS conversion_pct,
  ROUND(100.0*c.opted_out/NULLIF(c.sent,0),   2) AS optout_pct,
  ROUND(c.revenue/NULLIF(c.orders,0),         2) AS avg_order_value
FROM campaigns c;
```

---

## 3. الموزع الذكي (Smart Dispatcher)

### الطوابير بالأولوية

```javascript
import { Queue, Worker } from 'bullmq';

const connection = { host: 'redis', port: 6379 };

// 🔴 أولوية 1 — أعلى: opt-out (فوري، بيتخطى كل حاجة)
export const optOutQueue = new Queue('optout', { connection });

// 🟠 أولوية 2 — ردود العملاء (لازم رد سريع)
export const replyQueue = new Queue('reply', { connection });

// 🟡 أولوية 3 — تحديثات حالة الأوردر (transactional)
export const statusQueue = new Queue('status', { connection });

// 🟢 أولوية 4 — الحملة (الأبطأ والأقل أولوية)
export const campaignQueue = new Queue('campaign', {
  connection,
  defaultJobOptions: {
    attempts: 3,
    backoff: { type: 'exponential', delay: 300_000 }, // 5 دقايق
    removeOnComplete: 5000,
    removeOnFail: false,
  },
});
```

### البوابات (Gates) — سلسلة الفحص

```javascript
/**
 * كل رسالة لازم تعدّي من كل البوابات دي قبل الإرسال
 */
class GateChain {
  constructor(deps) {
    this.db = deps.db;
    this.redis = deps.redis;
    this.warmup = deps.warmup;
    this.scorer = deps.scorer;
    this.delayEngine = deps.delayEngine;
  }

  async evaluate({ phone, sessionId, segment, campaignId }) {
    const gates = [
      this.gSuppression,
      this.gSessionStatus,
      this.gWarmup,
      this.gQuota,
      this.gCircadian,
      this.gRisk,
      this.gReplyRatio,
      this.gGlobalPool,
      this.gDuplicate,
    ];

    for (const gate of gates) {
      const r = await gate.call(this, { phone, sessionId, segment, campaignId });
      if (!r.pass) return { allowed: false, gate: gate.name, ...r };
    }

    // ✅ كل البوابات فاتت — احسب التأخير
    const contactState = await this.getContactState(phone, sessionId);
    return { allowed: true, contactState };
  }

  // ── 1. قائمة الحظر ──
  async gSuppression({ phone }) {
    const supp = await this.db.oneOrNone(
      `SELECT reason FROM suppression_list WHERE phone = $1`, [phone]
    );
    return supp
      ? { pass: false, reason: `في suppression: ${supp.reason}`, drop: true }
      : { pass: true };
  }

  // ── 2. حالة الجلسة ──
  async gSessionStatus({ sessionId }) {
    const s = await this.db.one(
      `SELECT status, connection, paused_until FROM sessions WHERE id = $1`,
      [sessionId]
    );
    if (['banned','retired'].includes(s.status))
      return { pass: false, reason: `الجلسة ${s.status}`, reassign: true };
    if (s.paused_until && new Date(s.paused_until) > new Date())
      return { pass: false, reason: 'الجلسة موقوفة', retryAt: s.paused_until };
    if (s.connection !== 'open')
      return { pass: false, reason: 'الجلسة مش متصلة', retryIn: 120_000 };
    return { pass: true };
  }

  // ── 3. التدفئة ──
  async gWarmup({ sessionId, segment }) {
    const r = await this.warmup.canSend(sessionId, segment);
    return r.allowed
      ? { pass: true, minDelay: r.minDelay }
      : { pass: false, reason: r.reason, retryIn: 3600_000 };
  }

  // ── 4. الحد اليومي/الساعي ──
  async gQuota({ sessionId }) {
    const [day, hour] = await Promise.all([
      this.redis.get(`q:d:${sessionId}:${today()}`),
      this.redis.get(`q:h:${sessionId}:${thisHour()}`),
    ]);
    const s = await this.db.one(
      `SELECT daily_quota, hourly_quota, rate_multiplier
       FROM sessions WHERE id = $1`, [sessionId]
    );
    const dQuota = Math.floor(s.daily_quota * s.rate_multiplier);
    const hQuota = Math.floor(s.hourly_quota * s.rate_multiplier);

    if (+(day||0) >= dQuota)
      return { pass: false, reason: `الحد اليومي ${dQuota}`, retryAt: tomorrow() };
    if (+(hour||0) >= hQuota)
      return { pass: false, reason: `الحد الساعي ${hQuota}`, retryIn: 3600_000 };
    return { pass: true };
  }

  // ── 5. نافذة الوقت ──
  async gCircadian() {
    const w = this.delayEngine.isSendWindow();
    return w.ok
      ? { pass: true, slow: w.slow }
      : { pass: false, reason: w.reason, retryAt: nextWindowStart() };
  }

  // ── 6. تقييم المخاطر ──
  async gRisk({ sessionId }) {
    const r = await this.scorer.score(sessionId);
    if (r.level === 'critical')
      return { pass: false, reason: `🔴 risk ${r.score}`, stopSession: true };
    if (r.level === 'high')
      return { pass: false, reason: `🟠 risk ${r.score}`, pauseSession: 24*3600_000 };
    return { pass: true, riskScore: r.score };
  }

  // ── 7. نسبة الرد ──
  async gReplyRatio({ sessionId }) {
    const s = await this.db.one(
      `SELECT reply_ratio_48h FROM sessions WHERE id = $1`, [sessionId]
    );
    if (s.reply_ratio_48h !== null && s.reply_ratio_48h < 0.03)
      return { pass: false, reason: `🔴 reply ratio ${s.reply_ratio_48h}`, stopSession: true };
    return { pass: true };
  }

  // ── 8. الحد العام للـ IP (كل الجلسات على نفس البروكسي) ──
  async gGlobalPool({ sessionId }) {
    const proxyId = await this.db.oneValue(
      `SELECT proxy_id FROM sessions WHERE id = $1`, [sessionId]
    );
    const key = `pool:${proxyId}:${thisMinute()}`;
    const count = await this.redis.incr(key);
    await this.redis.expire(key, 120);

    const MAX_PER_PROXY_MIN = 3;   // 🔑 حد صارم
    if (count > MAX_PER_PROXY_MIN) {
      await this.redis.decr(key);
      return { pass: false, reason: 'حد البروكسي/دقيقة', retryIn: 60_000 };
    }
    return { pass: true };
  }

  // ── 9. منع التكرار النصي ──
  async gDuplicate({ sessionId }) {
    // هنا بنفحص إن النص اللي هيتولد مش مكرر — يتم في ContentGenerator
    return { pass: true };
  }

  async getContactState(phone, sessionId) {
    const c = await this.db.oneOrNone(`
      SELECT
        COUNT(*) FILTER (WHERE direction='in')  AS ins,
        COUNT(*) FILTER (WHERE direction='out') AS outs,
        MAX(created_at) FILTER (WHERE direction='in') AS last_in
      FROM message_log WHERE phone = $1 AND session_id = $2
    `, [phone, sessionId]);

    if (!c || (+c.ins === 0 && +c.outs === 0)) return 'stranger';
    if (+c.ins === 0) return 'handshake_sent';
    if (c.last_in && Date.now() - new Date(c.last_in) < 3600_000)
      return 'active_conversation';
    if (+c.ins >= 3) return 'known';
    return 'handshake_complete';
  }
}
```

### الـ Worker الرئيسي

```javascript
import { Worker } from 'bullmq';

const campaignWorker = new Worker('campaign', async (job) => {
  const { customerId, campaignId, sessionId: preferred } = job.data;

  const customer = await db.one(`SELECT * FROM customers WHERE id=$1`, [customerId]);
  const campaign = await db.one(`SELECT * FROM campaigns WHERE id=$1`, [campaignId]);

  // ⏸️ الحملة موقوفة؟
  if (campaign.status !== 'running') {
    throw new UnrecoverableError(`الحملة ${campaign.status}`);
  }

  // 1️⃣ حدّد الجلسة (Sticky أولاً)
  const sessionId = await resolveSession(customer, preferred);
  if (!sessionId) {
    // مفيش جلسة متاحة → أعد المحاولة بعد ساعة
    throw new DelayedError(3600_000);
  }

  // 2️⃣ البوابات
  const gate = await gateChain.evaluate({
    phone: customer.phone,
    sessionId,
    segment: customer.segment,
    campaignId,
  });

  if (!gate.allowed) {
    // معالجة نتيجة البوابة
    if (gate.drop)          return { skipped: gate.reason };
    if (gate.stopSession)   await sessionMgr.stop(sessionId, gate.reason);
    if (gate.pauseSession)  await sessionMgr.pause(sessionId, gate.pauseSession);
    if (gate.reassign)      { job.data.sessionId = null; throw new DelayedError(60_000); }
    throw new DelayedError(gate.retryIn ?? 1800_000);
  }

  // 3️⃣ ولّد النص (فريد)
  const content = await contentGen.generate({
    template: campaign.template,
    customer,
    campaign,
  });

  // 4️⃣ احسب التأخير
  const delay = delayEngine.compute({
    contactState: gate.contactState,
    messageLength: content.text.length,
    sentInHour: await getSentThisHour(sessionId),
  });

  // 5️⃣ اقفل الجلسة (جلسة واحدة = رسالة واحدة في المرة)
  const lock = await acquireLock(`session:${sessionId}`, delay + 60_000);
  if (!lock) throw new DelayedError(15_000);

  try {
    // 6️⃣ انتظر التأخير
    await sleep(delay);

    // 7️⃣ سجّل قبل الإرسال
    const logId = await db.oneValue(`
      INSERT INTO message_log
        (campaign_id, customer_id, session_id, phone, direction,
         msg_type, content, content_hash, status, delay_used_ms)
      VALUES ($1,$2,$3,$4,'out',$5,$6,$7,'sending',$8)
      RETURNING id
    `, [campaignId, customerId, sessionId, customer.phone,
        content.type, content.text, content.hash, delay]);

    // 8️⃣ ابعت (مع typing simulation)
    const result = await sender.send({
      sessionId,
      phone: customer.phone,
      content,
      withTyping: true,
      withReadReceipt: true,
    });

    // 9️⃣ حدّث
    await db.query(`
      UPDATE message_log
      SET status='sent', wa_message_id=$1, sent_at=NOW()
      WHERE id=$2
    `, [result.messageId, logId]);

    await redis.incr(`q:d:${sessionId}:${today()}`);
    await redis.incr(`q:h:${sessionId}:${thisHour()}`);
    await db.query(
      `UPDATE campaigns SET sent = sent + 1 WHERE id = $1`, [campaignId]
    );
    await db.query(
      `UPDATE customers SET assigned_session = $1 WHERE id = $2`,
      [sessionId, customerId]
    );

    // 🔟 استراحة الدفعة
    const rest = batchScheduler.onSent(sessionId);
    if (rest) {
      await sessionMgr.pause(sessionId, rest.rest,
        `استراحة ${rest.type}`);
    }

    return { ok: true, messageId: result.messageId, delay };

  } finally {
    await releaseLock(lock);
  }
}, {
  connection,
  concurrency: 8,        // = عدد الجلسات
  limiter: { max: 8, duration: 60_000 },  // حد عام
});
```

---

## 4. تدوير الأرقام (Session Rotation)

```javascript
/**
 * الاستراتيجية: Sticky أولاً، ثم Least-Loaded، ثم Round-Robin
 */
async function resolveSession(customer, preferred = null) {
  // 1️⃣ Sticky: نفس الرقم اللي كلّم العميل قبل كده
  const sticky = preferred ?? customer.assigned_session;
  if (sticky) {
    const ok = await isSessionUsable(sticky);
    if (ok) return sticky;
    // الجلسة مش متاحة → لو موقوفة مؤقتاً استنى، لو محظورة انتقل
    const s = await db.one(`SELECT status FROM sessions WHERE id=$1`, [sticky]);
    if (['paused','cooldown'].includes(s.status)) {
      return null;  // استنى، متغيّرش الرقم
    }
    // banned/retired → لازم نغيّر
    await notifySessionSwitch(customer, sticky);
  }

  // 2️⃣ اختار الأقل حمولة
  const candidates = await db.many(`
    SELECT
      s.id,
      s.daily_quota * s.rate_multiplier AS quota,
      COALESCE(m.sent_today, 0) AS sent_today,
      s.risk_score
    FROM sessions s
    LEFT JOIN (
      SELECT session_id, COUNT(*) AS sent_today
      FROM message_log
      WHERE direction='out' AND created_at >= CURRENT_DATE
      GROUP BY session_id
    ) m ON m.session_id = s.id
    WHERE s.status = 'active'
      AND s.connection = 'open'
      AND (s.paused_until IS NULL OR s.paused_until < NOW())
      AND s.risk_level IN ('low','medium')
    ORDER BY
      (COALESCE(m.sent_today,0)::float / NULLIF(s.daily_quota * s.rate_multiplier,0)) ASC,
      s.risk_score ASC
    LIMIT 5
  `).catch(() => []);

  const available = candidates.filter(c => c.sent_today < c.quota);
  if (!available.length) return null;

  // 3️⃣ عشوائية بسيطة بين الأفضل 3 (تجنب النمط المتوقع)
  const top = available.slice(0, 3);
  return top[Math.floor(Math.random() * top.length)].id;
}


async function isSessionUsable(sessionId) {
  const s = await db.oneOrNone(`
    SELECT status, connection, paused_until, risk_level,
           daily_quota * rate_multiplier AS quota,
           (SELECT COUNT(*) FROM message_log
            WHERE session_id = $1 AND direction='out'
              AND created_at >= CURRENT_DATE) AS sent_today
    FROM sessions WHERE id = $1
  `, [sessionId]);

  if (!s) return false;
  if (s.status !== 'active') return false;
  if (s.connection !== 'open') return false;
  if (s.paused_until && new Date(s.paused_until) > new Date()) return false;
  if (['high','critical'].includes(s.risk_level)) return false;
  if (+s.sent_today >= +s.quota) return false;
  return true;
}


/**
 * ⚠️ لما نضطر نغيّر رقم لعميل — نبّه فريق الخدمة
 * (تشتت المحادثات مشكلة حقيقية)
 */
async function notifySessionSwitch(customer, oldSession) {
  await alert(`
🔄 تغيير رقم لعميل
العميل: ${customer.name} (${customer.phone})
الرقم القديم: ${oldSession} (غير متاح)
⚠️ المحادثة السابقة على الرقم القديم — راجع الـ Chatwoot
  `);
  await db.query(`
    INSERT INTO session_events (session_id, event_type, severity, payload)
    VALUES ($1, 'customer_reassigned', 'warn', $2)
  `, [oldSession, JSON.stringify({ customer_id: customer.id })]);
}
```

---

## 5. مراقبة الصحة والإنعاش (Health Check & Auto-Recovery)

```javascript
class HealthMonitor {
  constructor(deps) {
    Object.assign(this, deps);
  }

  /** كل دقيقة */
  async tick() {
    const sessions = await this.db.many(
      `SELECT id, evo_base_url, evo_instance, status FROM sessions
       WHERE status NOT IN ('retired','banned')`
    );

    await Promise.allSettled(sessions.map(s => this.checkOne(s)));
  }

  async checkOne(s) {
    let state;
    try {
      const res = await fetch(
        `${s.evo_base_url}/instance/connectionState/${s.evo_instance}`,
        { headers: { apikey: process.env.EVO_KEY }, timeout: 10_000 }
      );
      state = (await res.json())?.instance?.state;
    } catch (e) {
      state = 'unreachable';
    }

    await this.db.query(
      `UPDATE sessions SET connection=$1, last_seen_at=NOW() WHERE id=$2`,
      [state, s.id]
    );

    switch (state) {
      case 'open':
        await this.onHealthy(s);
        break;

      case 'connecting':
        await this.onConnecting(s);
        break;

      case 'close':
        await this.onClosed(s);
        break;

      case 'unreachable':
        await this.onUnreachable(s);
        break;
    }
  }

  async onHealthy(s) {
    const prev = await this.redis.get(`down:${s.id}`);
    if (prev) {
      await this.redis.del(`down:${s.id}`);
      await this.alert(`✅ ${s.id} رجع للعمل`);
      // 🔑 مهم: بعد الرجوع، ابدأ بـ 10% وارفع تدريجياً
      await this.startReconnectRamp(s.id);
    }
  }

  async onConnecting(s) {
    const since = await this.redis.get(`connecting:${s.id}`);
    if (!since) {
      await this.redis.setex(`connecting:${s.id}`, 900, Date.now());
      return;
    }
    // أكتر من 5 دقايق في connecting = مشكلة
    if (Date.now() - +since > 300_000) {
      await this.alert(`🟡 ${s.id} في connecting لأكثر من 5 دقايق`);
      await this.restartInstance(s);
    }
  }

  async onClosed(s) {
    // اقرأ آخر كود انقطاع
    const lastEvent = await this.db.oneOrNone(`
      SELECT payload FROM session_events
      WHERE session_id=$1 AND event_type='disconnect'
      ORDER BY created_at DESC LIMIT 1
    `, [s.id]);

    const code = lastEvent?.payload?.statusCode;
    const info = DISCONNECT_MAP[code];

    if (code === 403) {
      // 🔴 حظر
      await this.handleBan(s, code);
      return;
    }

    if (code === 429) {
      // 🟠 rate limited
      await this.handleRateLimit(s);
      return;
    }

    if (code === 401 || code === 440) {
      // 🔑 محتاج QR جديد
      await this.requestQR(s);
      return;
    }

    // انقطاع عادي → أعد الاتصال بـ backoff
    const attempts = +(await this.redis.incr(`reconn:${s.id}`));
    await this.redis.expire(`reconn:${s.id}`, 3600);

    if (attempts > 6) {
      await this.alert(`🟠 ${s.id} فشل الاتصال ${attempts} مرات`);
      await this.db.query(
        `UPDATE sessions SET status='degraded' WHERE id=$1`, [s.id]
      );
      return;
    }

    const backoff = Math.min(300_000, 5000 * Math.pow(2, attempts));
    setTimeout(() => this.reconnect(s), backoff);
  }

  async onUnreachable(s) {
    await this.alert(`🔴 ${s.id} — الـ container مش شغال؟ تحقق من Docker`);
  }

  // ═══ معالجة الحظر ═══
  async handleBan(s, code) {
    await this.db.query(`
      UPDATE sessions
      SET status='banned', banned_at=NOW(), ban_code=$1
      WHERE id=$2
    `, [code, s.id]);

    // 1. أعد توزيع العملاء المعلقين
    const orphans = await this.db.many(`
      SELECT id, phone, name FROM customers WHERE assigned_session=$1
    `, [s.id]).catch(() => []);

    // 2. شيل الجلسة من الطوابير
    await this.reassignPendingJobs(s.id);

    // 3. شغّل الاحتياطي
    const standby = await this.db.oneOrNone(`
      SELECT id FROM sessions
      WHERE status='ready' AND is_mature=TRUE
      ORDER BY registered_at ASC LIMIT 1
    `);

    if (standby) {
      await this.db.query(
        `UPDATE sessions SET status='active' WHERE id=$1`, [standby.id]
      );
      await this.alert(`
🔴 حظر: ${s.id} (${s.phone})
كود: ${code}
عملاء متأثرين: ${orphans.length}
✅ تم تشغيل الاحتياطي: ${standby.id}

⚠️ الإجراءات المطلوبة:
1. راجع آخر 100 رسالة من ${s.id} — إيه اللي سبب الحظر؟
2. راجع الـ Chatwoot لأي محادثات نشطة كانت على الرقم
3. ابدأ تدفئة رقم جديد بدل الاحتياطي اللي استخدمناه
      `);
    } else {
      await this.alert(`
🚨🚨 حظر بدون احتياطي 🚨🚨
${s.id} (${s.phone}) اتحظر
مفيش أرقام احتياطي مدفأة!

السعة اليومية نزلت. لازم:
1. ابدأ تدفئة 2-3 أرقام جديدة فوراً (21 يوم!)
2. قلّل حجم الحملة الحالية
3. راجع الـ Anti-Ban checklist
      `);
    }

    // 4. Kill switch check
    const bannedToday = await this.db.oneValue(`
      SELECT COUNT(*) FROM sessions
      WHERE banned_at >= CURRENT_DATE
    `);
    if (+bannedToday >= 2) {
      await killSwitch.trigger(
        `🚨 ${bannedToday} أرقام اتحظرت اليوم — إيقاف شامل`
      );
    }
  }

  // ═══ معالجة Rate Limit ═══
  async handleRateLimit(s) {
    const until = new Date(Date.now() + 4 * 3600_000);
    await this.db.query(`
      UPDATE sessions
      SET status='cooldown', paused_until=$1,
          pause_reason='WhatsApp 429 Rate Limited',
          rate_multiplier=0.25
      WHERE id=$2
    `, [until, s.id]);

    await this.alert(`
🟠 Rate Limit (429) على ${s.id}
ده تحذير رسمي من واتساب!

الإجراء التلقائي:
• إيقاف 4 ساعات
• استئناف بـ 25% من السرعة
• زيادة 25% كل أسبوع

⚠️ راجع: هل بتبعت أسرع من اللازم؟
    `);

    // خطة الرفع التدريجي
    await this.scheduleRamp(s.id, { start: 0.25, weekly: 0.25 });
  }

  // ═══ طلب QR ═══
  async requestQR(s) {
    // 🔑 عدد مرات مسح QR = إشارة حظر! لا تكرره كثيراً
    const scans = +(await this.redis.get(`qr:${s.id}`) ?? 0);
    if (scans >= 3) {
      await this.alert(`
🔴 ${s.id} طلب QR ${scans} مرات في 24 ساعة
❌ متمسحش تاني! ده بيزوّد خطر الحظر جداً.
الإجراء: سيب الرقم 48 ساعة، تحقق من التليفون الأصلي.
      `);
      await this.db.query(
        `UPDATE sessions SET status='degraded' WHERE id=$1`, [s.id]
      );
      return;
    }

    await this.redis.incr(`qr:${s.id}`);
    await this.redis.expire(`qr:${s.id}`, 86400);

    const qr = await this.fetchQR(s);
    await this.alert(`
🔑 ${s.id} (${s.phone}) محتاج QR جديد
المحاولة: ${scans + 1}/3
امسح من الموبايل الأصلي فوراً 👇
    `, { image: qr });
  }

  // ═══ الرفع التدريجي بعد الرجوع ═══
  async startReconnectRamp(sessionId) {
    const steps = [0.10, 0.25, 0.50, 0.75, 0.90, 1.00];
    for (let i = 0; i < steps.length; i++) {
      setTimeout(async () => {
        await this.db.query(
          `UPDATE sessions SET rate_multiplier=$1 WHERE id=$2`,
          [steps[i], sessionId]
        );
      }, i * 10_000);  // كل 10 ثواني خطوة → دقيقة كاملة
    }
  }
}
```

---

## 6. صندوق الوارد الموحد (Unified Inbox)

### المشكلة

```
❌ بدون Unified Inbox:
   8 أرقام × محادثات نشطة
   → فريق الخدمة لازم يفتح 8 واتساب ويب
   → محادثات ضايعة، ردود متأخرة، عملاء زعلانين
   → أول خطوة للبلاغات
```

### الحل: Chatwoot + Evolution

Evolution API عنده تكامل مدمج:

```javascript
// اربط كل جلسة بنفس الـ Chatwoot Inbox
await fetch(`${EVO_URL}/chatwoot/set/${instanceName}`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json', apikey: EVO_KEY },
  body: JSON.stringify({
    enabled: true,
    accountId: '1',
    token: CHATWOOT_TOKEN,
    url: CHATWOOT_URL,
    signMsg: false,             // متضيفش توقيع
    reopenConversation: true,
    conversationPending: false,
    nameInbox: `wa-${instanceName}`,
    importContacts: false,      // ⚠️ متستوردش كل الكونتاكتس
    importMessages: false,
    daysLimitImportMessages: 0,
  }),
});
```

### تنظيم Chatwoot

```
┌──────────────────────────────────────────────┐
│  Chatwoot Account                            │
│  ├── Inbox: wa-official      (رقم الشركة)    │
│  ├── Inbox: wa-campaign-1    (رقم حملات 1)   │
│  ├── Inbox: wa-campaign-2    (رقم حملات 2)   │
│  └── ...                                     │
│                                              │
│  Labels (تلقائية):                            │
│  🏷️ segment:champions                        │
│  🏷️ campaign:eid-2026                        │
│  🏷️ bot:active / bot:handoff                 │
│  🏷️ order:pending / order:confirmed          │
│  🏷️ intent:price / intent:complaint          │
│                                              │
│  Teams:                                      │
│  👥 Sales    → أوردرات وأسعار                 │
│  👥 Support  → شكاوى وتتبع                    │
└──────────────────────────────────────────────┘
```

### تسليم البوت للبشر (Handoff)

```javascript
const HANDOFF_TRIGGERS = {
  // كلمات صريحة
  keywords: [
    'عايز اتكلم مع حد', 'مدير', 'موظف', 'انسان', 'بشر',
    'مش فاهم', 'البوت', 'روبوت', 'شكوى', 'اشتكي',
    'مشكلة', 'غلط', 'استرجاع', 'ارجاع', 'فلوسي',
    'محامي', 'شرطة', 'حماية المستهلك', 'اتنصب',
  ],

  // شروط سلوكية
  conditions: [
    { name: 'bot_confused',    check: c => c.context.failedParses >= 2 },
    { name: 'long_convo',      check: c => c.msg_in_count > 12 },
    { name: 'high_value',      check: c => c.cart_total > 3000 },
    { name: 'negative_tone',   check: c => c.context.sentiment === 'negative' },
    { name: 'repeated_same',   check: c => c.context.sameMsgCount >= 3 },
  ],
};

async function checkHandoff(conv, incomingText) {
  const t = normalizeArabic(incomingText);

  // 1. كلمات مفتاحية
  const kw = HANDOFF_TRIGGERS.keywords.find(k => t.includes(k));
  if (kw) return { handoff: true, reason: `keyword:${kw}`, urgent: true };

  // 2. شروط
  for (const c of HANDOFF_TRIGGERS.conditions) {
    if (c.check(conv)) return { handoff: true, reason: c.name };
  }

  return { handoff: false };
}

async function doHandoff(conv, reason, urgent = false) {
  await db.query(`
    UPDATE conversations
    SET is_bot_active=FALSE, handoff_at=NOW(),
        context = context || $1
    WHERE id=$2
  `, [JSON.stringify({ handoff_reason: reason }), conv.id]);

  // رسالة انتقالية مهذبة
  await sendMessage(conv.session_id, conv.phone,
    urgent
      ? 'أنا آسف على أي إزعاج 🙏 حولت المحادثة لموظف من الفريق، هيرد عليك خلال دقائق.'
      : 'خليني أوصلك بحد من الفريق يساعدك أحسن 👍 لحظات...'
  );

  // Chatwoot: افتح + اسند
  if (conv.chatwoot_conv_id) {
    await chatwoot.assignConversation(conv.chatwoot_conv_id, {
      team: urgent ? 'Support' : 'Sales',
      priority: urgent ? 'urgent' : 'medium',
      labels: [`handoff:${reason}`],
      status: 'open',
    });
  }

  // تنبيه للفريق
  if (urgent) {
    await alert(`🔔 تسليم عاجل\n${conv.phone}\nالسبب: ${reason}`);
  }
}
```

---

## 7. n8n Workflows

### Workflow 1: إشعارات حالة الأوردر

```
┌─────────────────────────────────────────────────┐
│  Webhook (POST /order-status)                   │
│  ← من نظام إدارة الأوردرات                       │
└──────────────────┬──────────────────────────────┘
                   ▼
        ┌──────────────────────┐
        │  Postgres: هات بيانات │
        │  الأوردر + العميل +   │
        │  الجلسة المخصصة       │
        └──────────┬───────────┘
                   ▼
        ┌──────────────────────┐
        │  IF: في suppression?  │──── نعم ──→ [توقف]
        └──────────┬───────────┘
                   │ لا
                   ▼
        ┌──────────────────────┐
        │  Switch على status    │
        ├──────────────────────┤
        │ confirmed → قالب 1    │
        │ shipped   → قالب 2    │
        │ delivered → قالب 3    │
        │ cancelled → قالب 4    │
        └──────────┬───────────┘
                   ▼
        ┌──────────────────────┐
        │  Code: ولّد النص      │
        │  (spintax + متغيرات)  │
        └──────────┬───────────┘
                   ▼
        ┌──────────────────────┐
        │  Wait: 20-60 ثانية    │
        │  (عشوائي)             │
        └──────────┬───────────┘
                   ▼
        ┌──────────────────────┐
        │  HTTP: Evolution API  │
        │  POST sendText        │
        └──────────┬───────────┘
                   ▼
        ┌──────────────────────┐
        │  Postgres: سجّل        │
        └──────────────────────┘
```

> 💡 **رسائل حالة الأوردر (Transactional) أقل خطراً بكتير** من الترويجية — لأن العميل بيتوقعها وبيرحب بيها. تقدر تبعتها من الرقم الرسمي بأمان.

### Workflow 2: مراقبة دورية (كل 15 دقيقة)

```
Schedule Trigger (*/15 * * * *)
        ▼
Postgres: SELECT * FROM v_session_dashboard
        ▼
Code: احسب risk score لكل جلسة
        ▼
Filter: risk_level IN ('high','critical')
        ▼
Switch:
  ├─ critical → HTTP: stop session + Telegram 🔴
  ├─ high     → HTTP: pause 24h  + Telegram 🟠
  └─ medium   → SQL: rate_multiplier = 0.5 + Telegram 🟡
        ▼
IF: عدد critical >= 2
        ▼
HTTP: POST /kill-switch  +  Telegram 🚨🚨
```

---

## 8. المراقبة والتنبيهات (Monitoring)

```javascript
class Alerter {
  constructor(tgToken, chatId) {
    this.token = tgToken;
    this.chatId = chatId;
  }

  async send(text, { image, level = 'info' } = {}) {
    const icons = {
      info: 'ℹ️', ok: '✅', warn: '🟡',
      high: '🟠', critical: '🔴', emergency: '🚨',
    };
    const body = `${icons[level]} *${level.toUpperCase()}*\n\n${text}`;

    if (image) {
      await fetch(`https://api.telegram.org/bot${this.token}/sendPhoto`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          chat_id: this.chatId, photo: image,
          caption: body, parse_mode: 'Markdown',
        }),
      });
    } else {
      await fetch(`https://api.telegram.org/bot${this.token}/sendMessage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          chat_id: this.chatId, text: body, parse_mode: 'Markdown',
        }),
      });
    }
  }

  critical(t, o) { return this.send(t, { ...o, level: 'critical' }); }
  emergency(t, o) { return this.send(t, { ...o, level: 'emergency' }); }
}
```

### التقرير اليومي

```javascript
async function dailyReport() {
  const r = await db.one(`
    SELECT
      (SELECT COUNT(*) FROM message_log
       WHERE direction='out' AND created_at >= CURRENT_DATE)        AS sent,
      (SELECT COUNT(*) FROM message_log
       WHERE direction='out' AND status IN ('delivered','read')
         AND created_at >= CURRENT_DATE)                            AS delivered,
      (SELECT COUNT(DISTINCT phone) FROM message_log
       WHERE direction='in' AND created_at >= CURRENT_DATE)         AS replied,
      (SELECT COUNT(*) FROM suppression_list
       WHERE reason='user_opt_out' AND added_at >= CURRENT_DATE)    AS optouts,
      (SELECT COUNT(*) FROM orders
       WHERE created_at >= CURRENT_DATE)                            AS orders,
      (SELECT COALESCE(SUM(total),0) FROM orders
       WHERE created_at >= CURRENT_DATE)                            AS revenue,
      (SELECT COUNT(*) FROM sessions WHERE banned_at >= CURRENT_DATE) AS banned
  `);

  const sessions = await db.many(`SELECT * FROM v_session_dashboard`);

  const replyRatio = r.sent > 0 ? (r.replied / r.sent * 100).toFixed(1) : '—';
  const deliveryRate = r.sent > 0 ? (r.delivered / r.sent * 100).toFixed(1) : '—';
  const convRate = r.sent > 0 ? (r.orders / r.sent * 100).toFixed(2) : '—';
  const optoutRate = r.sent > 0 ? (r.optouts / r.sent * 100).toFixed(2) : '—';

  const flag = (v, good, warn) =>
    v === '—' ? '⚪' : +v >= good ? '🟢' : +v >= warn ? '🟡' : '🔴';

  await alerter.send(`
📊 *تقرير يومي — ${new Date().toLocaleDateString('ar-EG')}*

*الإرسال*
📤 مُرسل: ${r.sent}
📬 مُوصّل: ${r.delivered} (${deliveryRate}%) ${flag(deliveryRate, 90, 75)}
💬 ردود: ${r.replied} (${replyRatio}%) ${flag(replyRatio, 15, 8)}
🚫 إلغاءات: ${r.optouts} (${optoutRate}%) ${+optoutRate <= 1.5 ? '🟢' : '🔴'}

*التحويل*
🛒 أوردرات: ${r.orders} (${convRate}%)
💰 إيرادات: ${(+r.revenue).toLocaleString('ar-EG')} ج

*الجلسات*
${sessions.map(s =>
  `${{low:'🟢',medium:'🟡',high:'🟠',critical:'🔴'}[s.risk_level]} \`${s.id}\` ` +
  `${s.sent_today}/${s.daily_quota} · risk ${s.risk_score} · ` +
  `rr ${s.reply_ratio_48h ? (s.reply_ratio_48h*100).toFixed(0)+'%' : '—'}`
).join('\n')}

${r.banned > 0 ? `\n🔴 *${r.banned} رقم اتحظر اليوم!*` : '✅ مفيش حظر'}
  `);
}
```

---

**التالي:** [`05-ORDER-FUNNEL.md`](./05-ORDER-FUNNEL.md) — مسار الطلب
