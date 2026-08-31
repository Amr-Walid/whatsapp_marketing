/* ═══════════════════════════════════════════════════════════════════════
   لوحة تحكم النظام الهجين لواتساب
   ─────────────────────────────────────────────────────────────────────
   فلسفة الملف ده:
   مفيش ولا رقم واحد هنا متكتوب بالإيد. كل حاجة على الشاشة جاية من
   نداء HTTP حقيقي على الـ API اللي شغّال دلوقتي، واللي بدوره بينادي
   نفس الـ ChannelRouter و نفس الـ MessageSender اللي بيشتغلوا في الإنتاج.
   يعني اللي المدير بيشوفه = اللي النظام بيعمله فعلاً، مش موك ولا صورة.

   ملاحظة تقنية: الـ API بيرجّع JSON بـ camelCase (متظبّط في Program.cs)
   والـ enums بترجع نصوص (JsonStringEnumConverter) — فمقارناتنا بالنص.
   ═══════════════════════════════════════════════════════════════════════ */

'use strict';

// ═══════════════════════════════════════════════════════════════════════
//  ١. طبقة الاتصال
// ═══════════════════════════════════════════════════════════════════════

/**
 * مساعد fetch واحد لكل النداءات.
 * السبب: عايزين رسالة خطأ عربية موحّدة في مكان واحد. لو السيرفر وقع
 * أو رجّع 400، المستخدم لازم يشوف سبب مفهوم مش "undefined" في الشاشة.
 */
async function api(path, options) {
  const res = await fetch(path, Object.assign({
    headers: { 'Accept': 'application/json' }
  }, options || {}));

  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = { raw: text }; }

  if (!res.ok) {
    const msg = (data && (data.error || data.title || data.detail)) || ('HTTP ' + res.status);
    const err = new Error(msg);
    err.status = res.status;
    err.payload = data;
    throw err;
  }
  return data;
}

const get  = (path) => api(path);
const post = (path, body) => api(path, {
  method: 'POST',
  headers: body ? { 'Content-Type': 'application/json', 'Accept': 'application/json' }
                : { 'Accept': 'application/json' },
  body: body ? JSON.stringify(body) : undefined
});

// ═══════════════════════════════════════════════════════════════════════
//  ٢. مساعدات العرض
// ═══════════════════════════════════════════════════════════════════════

const $$ = (sel) => Array.from(document.querySelectorAll(sel));
const el = (id) => document.getElementById(id);

/** منع حقن HTML — أي نص جاي من قاعدة البيانات بيعدّي من هنا */
function esc(v) {
  if (v === null || v === undefined) return '';
  return String(v)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

const setText = (id, v) => { const n = el(id); if (n) n.textContent = v; };
const setHtml = (id, v) => { const n = el(id); if (n) n.innerHTML = v; };

/** الدولار بأربع خانات — التسعير بالكسور الصغيرة (٠.٠٠٥ دولار للرسالة) */
const usd  = (n) => '$' + Number(n || 0).toFixed(4);
const usd2 = (n) => '$' + Number(n || 0).toFixed(2);
const pct  = (n) => Number(n || 0).toFixed(1) + '%';
const num  = (n) => Number(n || 0).toLocaleString('ar-EG');

/** الوقت بالتوقيت المحلي — بس فاكرين إن حدود Meta بتتصفّر على UTC */
function timeAr(iso) {
  if (!iso) return '—';
  const d = new Date(iso);
  return isNaN(d) ? '—' : d.toLocaleString('ar-EG', {
    month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit'
  });
}

// ── ترجمة المصطلحات التقنية للعربي ─────────────────────────────────────
// الأسماء دي بتتطابق مع الـ enums في WaHybrid.Domain.Enums
const AR_CHANNEL = { Official: 'رسمي', Unofficial: 'غير رسمي' };
const AR_MODE    = { Free: 'رسالة حرة (مجانية)', Template: 'قالب معتمد (مدفوع)' };
const AR_WINDOW  = { FepOpen: '🎁 FEP مفتوحة', CswOpen: '🟡 CSW مفتوحة', NoWindow: '🔴 مفيش نافذة' };
const AR_STATUS  = {
  Queued: 'في الطابور', Sending: 'بيتبعت', Sent: 'اتبعت', Delivered: 'اتسلّم',
  Read: 'اتقرأ', Failed: 'فشل', Blocked: 'اتحجب', Skipped: 'اتخطّى'
};
const AR_QUALITY = { Green: '🟢 أخضر', Yellow: '🟡 أصفر', Red: '🔴 أحمر' };

const arChannel = (c) => AR_CHANNEL[c] || c || '—';
const arMode    = (m) => AR_MODE[m] || m || '—';
const arWindow  = (w) => AR_WINDOW[w] || w || '—';
const arStatus  = (s) => AR_STATUS[s] || s || '—';

/** كلاس الـ chip للقناة — عشان اللون يبقى ثابت في كل الشاشة */
const chipChannel = (c) => c === 'Official' ? 'off' : c === 'Unofficial' ? 'un' : 'bad';
const chipMode    = (m) => m === 'Free' ? 'free' : m === 'Template' ? 'tpl' : 'bad';

/** كارت نتيجة موحّد في مخرجات المختبر/البوابات/التشغيل */
function resultCard(kind, title, bodyHtml) {
  const cls = kind === 'good' ? 'good' : kind === 'warn' ? 'warnb' : kind === 'bad' ? 'badb' : '';
  return '<div class="result ' + cls + '"><h4>' + title + '</h4>' + (bodyHtml || '') + '</div>';
}

function errorCard(e) {
  return resultCard('bad', '❌ حصل خطأ',
    '<div class="reason">' + esc(e && e.message ? e.message : e) + '</div>');
}

/** حالة "بيحمّل" — مهمة عشان المدير يعرف إن الضغطة وصلت */
function busy(id, label) {
  setHtml(id, resultCard('', '⏳ ' + esc(label || 'جاري التنفيذ…'), ''));
}

// ═══════════════════════════════════════════════════════════════════════
//  ٣. حالة الصفحة
// ═══════════════════════════════════════════════════════════════════════

const state = {
  customers: [],   // /api/dashboard/customers
  intents:   [],   // /api/dashboard/intents
  overview:  null  // /api/dashboard/overview
};

// ═══════════════════════════════════════════════════════════════════════
//  ٤. التبويبات
// ═══════════════════════════════════════════════════════════════════════

// التبويبات التقيلة بتتحمّل عند أول فتح بس — مش عند تحميل الصفحة كلها
const lazyLoaded = new Set();

function initTabs() {
  $$('.tab').forEach(btn => {
    btn.addEventListener('click', () => {
      const name = btn.dataset.tab;

      $$('.tab').forEach(b => b.classList.toggle('active', b === btn));
      $$('.panel').forEach(p => p.classList.toggle('active', p.id === 'tab-' + name));

      if (name === 'matrix' && !lazyLoaded.has('matrix')) {
        lazyLoaded.add('matrix');
        loadMatrix().catch(console.error);
      }
      // سجل الرسايل بيتغيّر مع كل إرسال — فنحدّثه في كل مرة تُفتح
      if (name === 'messages') {
        lazyLoaded.add('messages');
        loadMessages().catch(console.error);
      }
    });
  });
}

// ═══════════════════════════════════════════════════════════════════════
//  ٥. النظرة العامة — الأرقام اللي المدير بيسأل عليها
// ═══════════════════════════════════════════════════════════════════════

async function loadOverview() {
  const d = await get('/api/dashboard/overview');
  state.overview = d;

  // ── 🎯 المؤشر الأساسي: نسبة المجاني ──────────────────────────────
  // ده الرقم اللي النظام كله بيدور حوله. تحت ٧٥٪ يعني استراتيجية
  // النوافذ ضعيفة وإحنا بندفع لـ Meta أكتر من اللازم.
  const free = d.kpi.freePct;
  setText('kFree', pct(free));
  setText('kFreeVerdict', d.kpi.freePctOk
    ? '✅ فوق المستهدف — استراتيجية النوافذ شغّالة'
    : '⚠️ تحت المستهدف — محتاج FEP/CTWA أقوى');

  const bar = el('kFreeBar');
  if (bar) {
    bar.style.width = Math.min(100, Math.max(0, free)) + '%';
    bar.style.background = d.kpi.freePctOk ? 'var(--ok)' : 'var(--warn)';
  }

  // ── 💵 الفلوس ────────────────────────────────────────────────────
  setText('kSpent', usd(d.money.spentToday));
  setText('kSpentPct', pct(d.money.pct) + ' من حد ' + usd2(d.money.dailyLimit) + ' اليومي'
    + (d.money.hardStop ? ' — 🛑 الحد اتخطّى' : d.money.alert ? ' — ⚠️ قرّبنا على الحد' : ''));

  // الوفر = (لو بعتنا كل حاجة بقوالب رسمية) − (اللي دفعناه فعلاً)
  setText('kSaved', usd2(d.money.saved));

  // ── النوافذ والعملاء ────────────────────────────────────────────
  setText('wFep',   num(d.windows.fepOpen));
  setText('wCsw',   num(d.windows.cswOpen));
  setText('wNone',  num(d.windows.noWindow));
  setText('cTotal', num(d.customers.total));
  setText('cCtwa',  num(d.customers.fromCtwa));

  // ── توزيع القناتين ──────────────────────────────────────────────
  const off = d.byChannel.official, un = d.byChannel.unofficial;
  const tot = off + un;
  const splitEl = el('splitChannel');
  if (splitEl) {
    splitEl.innerHTML = tot === 0
      ? '<i style="width:100%;background:#21262d"></i>'
      : '<i style="width:' + (off / tot * 100) + '%;background:var(--off)" title="رسمي"></i>'
      + '<i style="width:' + (un  / tot * 100) + '%;background:var(--un)"  title="غير رسمي"></i>';
  }
  setText('mOff',  num(off) + (tot ? ' (' + pct(off / tot * 100) + ')' : ''));
  setText('mUn',   num(un)  + (tot ? ' (' + pct(un  / tot * 100) + ')' : ''));
  setText('mFree', num(d.byMode.free));
  setText('mTpl',  num(d.byMode.template));

  // ── حالة القناة الرسمية (Tier + الجودة) ────────────────────────
  setText('tTier',  d.tier.tier);
  setText('tLimit', num(d.tier.limit) + ' رسالة/يوم');
  setText('tUsed',  num(d.tier.usedToday));
  setText('tHead',  pct(d.tier.headroom));
  setText('tQual',  AR_QUALITY[d.tier.quality] || d.tier.quality);
  setHtml('tMkt',   d.tier.marketingPaused
    ? '<span class="chip bad">متوقّف</span> الجودة حمراء'
    : '<span class="chip ok">شغّال</span>');

  // ── صحة المزوّدين ───────────────────────────────────────────────
  const provs = el('providers');
  if (provs) {
    provs.innerHTML = d.providers.map(p => {
      const cls = !p.up ? 'down' : p.degraded ? 'deg' : 'up';
      const badge = !p.up ? '🔴 واقعة' : p.degraded ? '🟡 متدهورة' : '🟢 سليمة';
      return '<div class="prov ' + cls + '">'
        + '<h4>' + esc(arChannel(p.channel)) + ' — ' + badge + '</h4>'
        + '<p>المتاح: ' + pct(p.headroom) + ' · الجودة: '
        + esc(AR_QUALITY[p.quality] || p.quality || '—') + '</p>'
        + '<p>' + esc(p.note || '') + '</p></div>';
    }).join('');
  }

  // ── شريط الحالة فوق ─────────────────────────────────────────────
  const health = el('pHealth');
  if (health) {
    const killed = d.killSwitch.global || d.killSwitch.unofficial;
    health.className = 'pill ' + (killed ? 'bad' : 'ok');
    health.textContent = d.killSwitch.global ? '🛑 إيقاف عام'
      : d.killSwitch.unofficial ? '🛑 غير الرسمي متوقّف'
      : '🟢 النظام شغّال';
  }
}

// ═══════════════════════════════════════════════════════════════════════
//  ٦. مصفوفة القرار — بوابة القبول
// ═══════════════════════════════════════════════════════════════════════

/**
 * الجدول ده هو نفس الجدول اللي في docs/09 §4.2 — بس محسوب حياً.
 * كل خلية = نداء فعلي على ChannelRouter.DecideAsync.
 * ده اللي بيخلّي الشاشة إثبات، مش عرض تقديمي.
 */
async function loadMatrix() {
  const table = el('matrixTable');
  if (!table) return;
  table.innerHTML = '<tbody><tr><td>⏳ جاري حساب المصفوفة…</td></tr></tbody>';

  try {
    const d = await get('/api/routing/matrix');
    const rows = d.rows || [];
    if (rows.length === 0) {
      table.innerHTML = '<tbody><tr><td>مفيش بيانات</td></tr></tbody>';
      return;
    }

    // أسماء الأعمدة جاية من السيرفر نفسه (حالات النوافذ) — مش متكتوبة هنا
    const cols = rows[0].cells.map(c => c.window);

    const head = '<thead><tr><th>النية</th><th>النوع</th>'
      + cols.map(c => '<th>' + esc(c) + '</th>').join('')
      + '</tr></thead>';

    const body = rows.map(r => {
      const cells = r.cells.map(c => {
        if (!c.allowed) {
          return '<td><span class="chip bad">🚫 مرفوض</span>'
            + '<span class="why">' + esc(c.reason) + '</span></td>';
        }
        return '<td>'
          + '<span class="chip ' + chipChannel(c.channel) + '">' + esc(arChannel(c.channel)) + '</span> '
          + '<span class="chip ' + chipMode(c.mode) + '">' + (c.mode === 'Free' ? 'حرة' : 'قالب') + '</span>'
          + '<span class="why">' + esc(c.reason) + '</span></td>';
      }).join('');

      return '<tr><td><b>' + esc(r.label) + '</b><span class="why">' + esc(r.intent) + '</span></td>'
        + '<td>' + (r.critical
            ? '<span class="chip ok">حرج</span>'
            : '<span class="chip tpl">' + esc(r.intentClass) + '</span>') + '</td>'
        + cells + '</tr>';
    }).join('');

    table.innerHTML = head + '<tbody>' + body + '</tbody>';
  } catch (e) {
    table.innerHTML = '<tbody><tr><td>❌ ' + esc(e.message) + '</td></tr></tbody>';
  }
}

// ═══════════════════════════════════════════════════════════════════════
//  ٧. تعبئة القوائم المنسدلة
// ═══════════════════════════════════════════════════════════════════════

async function loadPickers() {
  const results = await Promise.all([
    get('/api/dashboard/customers'),
    get('/api/dashboard/intents')
  ]);
  state.customers = results[0];
  state.intents   = results[1];

  // العميل: بنكتب حالة نافذته جوه الاسم — عشان المستخدم يختار بذكاء
  // (لو عايز يشوف الفرق، يختار عميل "مفيش نافذة" ويضغط محاكاة إعلان)
  const custOpts = state.customers.map(c =>
    '<option value="' + esc(c.phone) + '">'
    + esc(arWindow(c.windowState)) + ' · ' + esc(c.name || c.phone) + ' · ' + esc(c.phone)
    + (c.optedOut ? ' · 🚫 ملغي' : '') + '</option>').join('');

  ['selPhone', 'selGatePhone'].forEach(id => {
    const n = el(id); if (n) n.innerHTML = custOpts;
  });

  const intentOpts = state.intents.map(i =>
    '<option value="' + esc(i.name) + '">' + esc(i.label)
    + (i.critical ? ' (حرج)' : '') + ' · ' + esc(i.name) + '</option>').join('');

  ['selIntent', 'selGateIntent', 'selCampIntent'].forEach(id => {
    const n = el(id); if (n) n.innerHTML = intentOpts;
  });

  // 🎯 الإعداد الافتراضي للعرض: عميل بره النافذة + حملة ترويجية.
  // ده أقوى سيناريو: القرار هيبقى "قالب مدفوع"، وبعد ضغطة الإعلان
  // هيتحوّل لـ "رسالة حرة مجانية". الفرق بيبان في ثانية.
  const outside = state.customers.filter(c => c.windowState === 'NoWindow' && !c.optedOut)[0];
  if (outside) {
    if (el('selPhone'))     el('selPhone').value = outside.phone;
    if (el('selGatePhone')) el('selGatePhone').value = outside.phone;
  }
  if (state.intents.some(i => i.name === 'campaign_promo')) {
    ['selIntent', 'selGateIntent', 'selCampIntent'].forEach(id => {
      const n = el(id); if (n) n.value = 'campaign_promo';
    });
  }
}

// ═══════════════════════════════════════════════════════════════════════
//  ٨. المختبر الحيّ — جوهر العرض
// ═══════════════════════════════════════════════════════════════════════

/** رسم قرار التوجيه في كارت واحد واضح */
function renderDecision(d, heading) {
  const dec = d.decision;
  const kind = !dec.allowed ? 'bad' : dec.mode === 'Free' ? 'good' : 'warn';

  const money = !dec.allowed ? '—'
    : dec.estimatedCostUsd > 0
      ? '<b style="color:var(--warn)">' + usd(dec.estimatedCostUsd) + '</b> للرسالة'
      : '<b style="color:var(--ok)">مجاناً ($0.0000)</b>';

  return resultCard(kind, esc(heading || ('القرار للعميل ' + (d.customer || d.phone))),
    '<table class="kv">'
    + '<tr><td>العميل</td><td>' + esc(d.customer || '—') + ' · ' + esc(d.phone) + '</td></tr>'
    + '<tr><td>النية</td><td>' + esc(d.intentLabel)
      + ' <span class="why">' + esc(d.intent) + '</span></td></tr>'
    + '<tr><td>فئة Meta</td><td>' + esc(d.metaCategory)
      + (d.critical ? ' · <span class="chip ok">حرج</span>' : '') + '</td></tr>'
    + '<tr><td>النافذة</td><td>' + esc(arWindow(d.window.state))
      + (d.window.fepHoursLeft > 0 ? ' · FEP فاضل ' + d.window.fepHoursLeft + ' ساعة' : '')
      + (d.window.cswHoursLeft > 0 ? ' · CSW فاضل ' + d.window.cswHoursLeft + ' ساعة' : '')
      + '</td></tr>'
    + '<tr><td>القناة</td><td>' + (dec.allowed
        ? '<span class="chip ' + chipChannel(dec.channel) + '">' + esc(arChannel(dec.channel)) + '</span>'
        : '<span class="chip bad">🚫 مرفوض</span>') + '</td></tr>'
    + '<tr><td>الوضع</td><td>' + (dec.allowed
        ? '<span class="chip ' + chipMode(dec.mode) + '">' + esc(arMode(dec.mode)) + '</span>'
        : '—') + '</td></tr>'
    + '<tr><td>القالب</td><td>' + esc(dec.templateName || '— (مش محتاج قالب)') + '</td></tr>'
    + '<tr><td>التكلفة المتوقّعة</td><td>' + money + '</td></tr>'
    + '</table>'
    + '<div class="reason">سبب التوجيه: ' + esc(dec.reason) + '</div>');
}

const currentPhone  = () => el('selPhone')  ? el('selPhone').value  : null;
const currentIntent = () => el('selIntent') ? el('selIntent').value : null;

const previewUrl = (phone, intent) =>
  '/api/routing/preview?phone=' + encodeURIComponent(phone)
  + '&intent=' + encodeURIComponent(intent);

async function doPreview() {
  busy('liveOut', 'بنسأل الموجّه…');
  try {
    const d = await get(previewUrl(currentPhone(), currentIntent()));
    setHtml('liveOut', renderDecision(d)
      + '<div class="note">🔍 ده <b>قراءة بس</b> — مفيش ولا رسالة اتبعتت ولا مليم اتصرف.</div>');
  } catch (e) { setHtml('liveOut', errorCard(e)); }
}

/**
 * 🎁 أقوى لقطة في العرض كله:
 * بناخد القرار قبل ضغطة الإعلان، نحاكي الضغطة، ناخد القرار بعدها،
 * ونعرض الاتنين جنب بعض. المدير يشوف بعينه "قالب مدفوع" بقى
 * "رسالة حرة مجانية" — نفس الرسالة، نفس العميل، فرق ٧٢ ساعة.
 */
async function doCtwa() {
  const phone = currentPhone(), intent = currentIntent();
  busy('liveOut', 'بنحاكي ضغطة إعلان…');
  try {
    // ١) القرار قبل — عشان يبقى عندنا مرجع للمقارنة
    const before = await get(previewUrl(phone, intent));

    // ٢) الضغطة نفسها — بتمرّ على نفس المطبّع والمعالج الحقيقيين
    const sim = await post('/webhooks/simulate/ctwa?phone=' + encodeURIComponent(phone)
      + '&headline=' + encodeURIComponent('خصم ٢٥٪ على أول أوردر'));

    // ٣) القرار بعد
    const after = await get(previewUrl(phone, intent));

    const savedPerMsg = (before.decision.estimatedCostUsd || 0)
                      - (after.decision.estimatedCostUsd || 0);

    const banner = resultCard('good', '🎁 نافذة FEP اتفتحت — ٧٢ ساعة',
      '<p class="lead">' + esc(sim.message) + '</p>'
      + '<ul class="deltas">'
        + (sim.whatChanged || []).map(x => '<li>' + esc(x) + '</li>').join('')
      + '</ul>'
      + (savedPerMsg > 0
        ? '<div class="note">💵 الوفر على كل رسالة للعميل ده: '
          + '<b style="color:var(--ok)">' + usd(savedPerMsg) + '</b> — '
          + 'على ١٠٠٠ عميل يبقى <b style="color:var(--ok)">'
          + usd2(savedPerMsg * 1000) + '</b>.</div>'
        : '')
      + '<div class="reason">FEP لحد: ' + esc(timeAr(sim.fepOpenedUntil))
        + ' · CSW لحد: ' + esc(timeAr(sim.cswUntil)) + '</div>');

    setHtml('liveOut', banner
      + renderDecision(before, '❌ قبل ضغطة الإعلان')
      + renderDecision(after,  '✅ بعد ضغطة الإعلان'));

    // الأرقام العامة اتغيّرت — نحدّث النظرة العامة والقوائم
    await Promise.all([loadOverview(), loadPickers()]);
    restoreSelection(phone, intent);
  } catch (e) { setHtml('liveOut', errorCard(e)); }
}

/** بعد إعادة تعبئة القوائم، نرجّع اختيار المستخدم زي ما كان */
function restoreSelection(phone, intent) {
  if (el('selPhone'))  el('selPhone').value  = phone;
  if (el('selIntent')) el('selIntent').value = intent;
}

/** 🟡 رسالة داخلة = تجديد نافذة الخدمة ٢٤ ساعة (مش ٧٢، ومش مجاني للتسويق) */
async function doInbound() {
  const phone = currentPhone(), intent = currentIntent();
  busy('liveOut', 'بنحاكي رسالة داخلة…');
  try {
    const sim = await post('/webhooks/simulate/inbound?phone=' + encodeURIComponent(phone)
      + '&text=' + encodeURIComponent('السلام عليكم، عايز أسأل عن الأوردر'));
    const after = await get(previewUrl(phone, intent));

    setHtml('liveOut', resultCard(sim.optedOut ? 'bad' : 'warn',
      '🟡 نافذة CSW اتجدّدت — ٢٤ ساعة',
      '<p class="lead">' + esc(sim.message) + '</p>'
      + '<div class="note">⚠️ فرق مهم: CSW بتسمح بالرسالة الحرة، بس '
        + '<b>مابتخلّيش التسويق مجاني</b>. الـ FEP بس هي اللي بتعمل كده. '
        + 'عشان كده الحملة الترويجية في CSW بتروح على القناة غير الرسمية.</div>'
      + '<div class="reason">CSW لحد: ' + esc(timeAr(sim.cswUntil))
        + ' · ' + esc(sim.note || '') + '</div>')
      + renderDecision(after, 'القرار بعد الرسالة الداخلة'));

    await Promise.all([loadOverview(), loadPickers()]);
    restoreSelection(phone, intent);
  } catch (e) { setHtml('liveOut', errorCard(e)); }
}

/** 📤 إرسال حقيقي عبر المزوّد الوهمي — بيمشي على السلسلة كلها */
async function doSend() {
  const phone = currentPhone(), intent = currentIntent();
  busy('liveOut', 'بنبعت…');
  try {
    const r = await post('/api/send', {
      phone: phone, intent: intent, body: null, campaignId: null, params: null
    });

    const kind = r.ok ? (r.mode === 'Free' ? 'good' : 'warn') : 'bad';
    const title = r.ok ? '📤 اتبعتت' : (r.deduped ? '🔁 اتمنعت — تكرار' : '🚫 اتحجبت');

    const tried = (r.tried || []).length
      ? '<div class="reason">المحاولات: '
        + r.tried.map(t => esc(arChannel(t.channel)) + ' → ' + esc(t.why)).join(' · ')
        + '</div>'
      : '';

    setHtml('liveOut', resultCard(kind, title,
      '<table class="kv">'
      + '<tr><td>رقم السجل</td><td>' + esc(r.logId || '—') + '</td></tr>'
      + '<tr><td>القناة</td><td>' + (r.channel
          ? '<span class="chip ' + chipChannel(r.channel) + '">' + esc(arChannel(r.channel)) + '</span>'
          : '—') + '</td></tr>'
      + '<tr><td>الوضع</td><td>' + (r.mode
          ? '<span class="chip ' + chipMode(r.mode) + '">' + esc(arMode(r.mode)) + '</span>'
          : '—') + '</td></tr>'
      + '<tr><td>النافذة وقت الإرسال</td><td>' + esc(arWindow(r.windowState)) + '</td></tr>'
      + '<tr><td>التكلفة</td><td>' + (r.estimatedCostUsd > 0
          ? '<b style="color:var(--warn)">' + usd(r.estimatedCostUsd) + '</b>'
          : '<b style="color:var(--ok)">مجاناً</b>') + '</td></tr>'
      + (r.blockedByGate
          ? '<tr><td>البوابة اللي حجبت</td><td><code>' + esc(r.blockedByGate) + '</code></td></tr>'
          : '')
      + (r.errorCode ? '<tr><td>كود الخطأ</td><td>' + esc(r.errorCode) + '</td></tr>' : '')
      + '<tr><td>معرّف المزوّد</td><td>' + esc(r.providerMessageId || '—') + '</td></tr>'
      + '</table>'
      + '<div class="reason">التوجيه: ' + esc(r.routeReason || '—') + '</div>'
      + '<div class="reason">النتيجة: ' + esc(r.reason || '—') + '</div>'
      + tried
      + (!r.ok && r.blockedByGate
          ? '<div class="note">✅ ده <b>مش عيب</b> — ده النظام بيحميك. '
            + 'البوابة وقفت الرسالة قبل ما توصل لـ Meta، فمفيش تكلفة '
            + 'ومفيش خطر على الرقم.</div>'
          : '')));

    await Promise.all([loadOverview(), loadMessages()]);
  } catch (e) { setHtml('liveOut', errorCard(e)); }
}

/** 🔁 برهان منع التكرار — بنبعت نفس النية مرتين ورا بعض */
async function doIdem() {
  const phone = currentPhone(), intent = currentIntent();
  busy('liveOut', 'بنبعت نفس الرسالة مرتين…');
  try {
    const r = await post('/api/send/prove-idempotency?phone=' + encodeURIComponent(phone)
      + '&intent=' + encodeURIComponent(intent));

    const okProof = r.verdict && r.verdict.indexOf('✅') === 0;

    setHtml('liveOut', resultCard(okProof ? 'good' : 'warn',
      '🔁 برهان منع التكرار بين القناتين',
      '<p class="lead">' + esc(r.explanation) + '</p>'
      + '<table class="kv">'
      + '<tr><td>المحاولة الأولى</td><td>' + (r.first.ok
          ? '<span class="chip ok">اتبعتت</span> على ' + esc(arChannel(r.first.channel))
          : '<span class="chip bad">اتحجبت</span> ' + esc(r.first.gate || '')) + '</td></tr>'
      + '<tr><td>المحاولة التانية</td><td>' + (r.second.ok
          ? '<span class="chip bad">اتبعتت — مشكلة!</span>'
          : '<span class="chip ok">اتمنعت</span> بواسطة <code>'
            + esc(r.second.gate || '') + '</code>') + '</td></tr>'
      + '<tr><td>الحكم</td><td><b>' + esc(r.verdict) + '</b></td></tr>'
      + '</table>'
      + '<div class="reason">مفتاح منع التكرار: ' + esc(r.idempotencyKey) + '</div>'
      + '<div class="note">المفتاح ده <b>حسابي مش عشوائي</b>: '
        + 'SHA256(عميل|نية|حملة|يوم). يعني أي سيرفر، في أي وقت، بيوصل لنفس '
        + 'المفتاح — فالحماية شغّالة حتى لو النظام موزّع على أكتر من ماكينة.</div>'));

    await Promise.all([loadOverview(), loadMessages()]);
  } catch (e) { setHtml('liveOut', errorCard(e)); }
}

// ═══════════════════════════════════════════════════════════════════════
//  ٩. تخطيط الحملة — التكلفة قبل الصرف
// ═══════════════════════════════════════════════════════════════════════

/** الأسباب بترجع كـ dictionary (سبب → عدد) — بنرتّبها تنازلي */
function renderReasons(title, dict) {
  const entries = Object.keys(dict || {}).map(k => [k, dict[k]]);
  if (entries.length === 0) return '';
  entries.sort((a, b) => b[1] - a[1]);
  return '<h4 style="margin-top:13px;font-size:13.5px">' + esc(title) + '</h4>'
    + '<ul class="deltas">'
    + entries.map(e => '<li><code>' + esc(e[0]) + '</code> — ' + num(e[1]) + '</li>').join('')
    + '</ul>';
}

async function loadPlan() {
  const intent  = el('selCampIntent') ? el('selCampIntent').value : 'campaign_promo';
  const segment = el('inpSegment') ? el('inpSegment').value.trim() : '';
  busy('planOut', 'بنحسب الخطة…');

  try {
    const q = '/api/campaigns/plan?intent=' + encodeURIComponent(intent)
      + (segment ? '&segment=' + encodeURIComponent(segment) : '');
    const p = await get(q);

    const kind = p.kpi.freePct >= p.kpi.target ? 'good' : 'warn';

    setHtml('planOut', resultCard(kind, '📋 خطة حملة «' + esc(p.intentLabel) + '»',
      '<div class="grid4">'
      + '<div class="mini"><span class="mini-label">مستهدف</span>'
        + '<span class="mini-value">' + num(p.totals.targeted) + '</span></div>'
      + '<div class="mini"><span class="mini-label">قابل للإرسال</span>'
        + '<span class="mini-value">' + num(p.totals.sendable) + '</span></div>'
      + '<div class="mini"><span class="mini-label">التكلفة المتوقّعة</span>'
        + '<span class="mini-value">' + usd2(p.money.estimatedCostUsd) + '</span></div>'
      + '<div class="mini"><span class="mini-label">الوفر</span>'
        + '<span class="mini-value good">' + usd2(p.money.savings) + '</span></div>'
      + '</div>'

      + '<table class="kv">'
      + '<tr><td>القالب</td><td>' + esc(p.templateName || '—') + ' '
        + (p.templateAvailable
            ? '<span class="chip ok">متاح</span>'
            : '<span class="chip bad">مش متاح</span>') + '</td></tr>'
      + '<tr><td>التوزيع على القناتين</td><td>'
        + '<span class="chip off">رسمي ' + num(p.byChannel.official) + '</span> '
        + '<span class="chip un">غير رسمي ' + num(p.byChannel.unofficial) + '</span></td></tr>'
      + '<tr><td>التوزيع على الأوضاع</td><td>'
        + '<span class="chip free">حرة ' + num(p.byMode.free) + '</span> '
        + '<span class="chip tpl">قالب ' + num(p.byMode.template) + '</span></td></tr>'
      + '<tr><td>التوزيع على النوافذ</td><td>🎁 FEP ' + num(p.byWindow.fep)
        + ' · 🟡 CSW ' + num(p.byWindow.csw)
        + ' · 🔴 مفيش ' + num(p.byWindow.none) + '</td></tr>'
      + '<tr><td>اتخطّى</td><td>' + num(p.totals.skipped) + ' عميل</td></tr>'
      + '<tr><td>تكلفة الرسالة الواحدة</td><td>' + usd(p.money.costPerMessage) + '</td></tr>'
      + '<tr><td>لو بعتنا الكل قوالب رسمية</td><td>'
        + usd2(p.money.ifAllOfficialTemplates) + '</td></tr>'
      + '<tr><td>نسبة المجاني</td><td><b>' + pct(p.kpi.freePct) + '</b> — '
        + esc(p.kpi.verdict) + '</td></tr>'
      + '</table>'

      + renderReasons('أسباب التوجيه', p.routeReasons)
      + renderReasons('أسباب التخطّي', p.skipReasons)

      + '<div class="note">🔍 التخطيط <b>قراءة بس</b> — ولا رسالة واحدة اتبعتت. '
        + 'الرقم ده بيوصل للمدير <b>قبل</b> الموافقة على الحملة، مش بعد الفاتورة.</div>'
      + '<pre class="ascii">' + esc(p.ascii) + '</pre>'));
  } catch (e) { setHtml('planOut', errorCard(e)); }
}

// ═══════════════════════════════════════════════════════════════════════
//  ١٠. البوابات — التتبّع الكامل
// ═══════════════════════════════════════════════════════════════════════

async function loadGates() {
  const phone  = el('selGatePhone')  ? el('selGatePhone').value  : null;
  const intent = el('selGateIntent') ? el('selGateIntent').value : null;
  busy('gatesOut', 'بنتتبّع البوابات…');

  try {
    const d = await get('/api/routing/gates?phone=' + encodeURIComponent(phone)
      + '&intent=' + encodeURIComponent(intent));

    const gates = d.gates || [];
    const blocked = gates.filter(g => !g.passed);

    const list = '<ul class="gatelist">' + gates.map(g =>
      '<li class="' + (g.passed ? 'pass' : 'block') + '">'
      + '<span><b>' + (g.passed ? '✅' : '🚫') + '</b> '
        + '<span class="gname">' + esc(g.gate) + '</span> '
        + '<span class="why">ترتيب ' + esc(g.order) + '</span></span>'
      + '<span style="color:var(--dim);font-size:12px">' + esc(g.reason || '') + '</span>'
      + '</li>').join('') + '</ul>';

    setHtml('gatesOut', resultCard(blocked.length ? 'bad' : 'good',
      blocked.length
        ? '🚫 اتحجبت عند <code>' + esc(blocked[0].gate) + '</code>'
        : '✅ عدّت كل الـ ' + num(gates.length) + ' بوابة',
      '<table class="kv">'
      + '<tr><td>القناة المختارة</td><td>' + (d.routeDecision.channel
          ? '<span class="chip ' + chipChannel(d.routeDecision.channel) + '">'
            + esc(arChannel(d.routeDecision.channel)) + '</span>'
          : '<span class="chip bad">مرفوض</span>') + '</td></tr>'
      + '<tr><td>الوضع</td><td>' + esc(arMode(d.routeDecision.mode)) + '</td></tr>'
      + '</table>'
      + '<div class="reason">' + esc(d.routeDecision.reason) + '</div>'
      + list
      + '<div class="note">📌 الترتيب مقصود: البوابات الرخيصة (قاعدة بيانات '
        + 'محلية) الأول، والغالية (نداء على Meta) الآخر. أول «لأ» بتوقّف '
        + 'السلسلة — فمابندفعش ثمن فحص إحنا أصلاً مش محتاجينه.</div>'));
  } catch (e) { setHtml('gatesOut', errorCard(e)); }
}

// ═══════════════════════════════════════════════════════════════════════
//  ١١. سجل الرسايل
// ═══════════════════════════════════════════════════════════════════════

async function loadMessages() {
  const table = el('msgTable');
  if (!table) return;
  table.innerHTML = '<tbody><tr><td>⏳ جاري التحميل…</td></tr></tbody>';

  try {
    const rows = await get('/api/dashboard/messages?take=80');
    if (!rows.length) {
      table.innerHTML = '<tbody><tr><td>مفيش رسايل — '
        + 'جرّب «ابعت فعلاً» في المختبر الحيّ</td></tr></tbody>';
      return;
    }

    const head = '<thead><tr>'
      + '<th>#</th><th>الوقت</th><th>الرقم</th><th>الاتجاه</th>'
      + '<th>القناة</th><th>النية</th><th>النافذة</th><th>الوضع</th>'
      + '<th>الحالة</th><th>التكلفة</th><th>السبب</th>'
      + '</tr></thead>';

    const body = rows.map(m => {
      const badCls = ['Failed', 'Blocked', 'Skipped'].indexOf(m.status) >= 0 ? 'bad' : 'ok';
      return '<tr>'
        + '<td>' + esc(m.id) + '</td>'
        + '<td>' + esc(timeAr(m.createdAt)) + '</td>'
        + '<td>' + esc(m.phone) + '</td>'
        + '<td>' + (m.direction === 'In' ? '⬅️ داخلة' : '➡️ خارجة') + '</td>'
        + '<td><span class="chip ' + chipChannel(m.channel) + '">'
          + esc(arChannel(m.channel)) + '</span></td>'
        + '<td>' + esc(m.intent || '—') + '</td>'
        + '<td>' + esc(arWindow(m.windowState)) + '</td>'
        + '<td><span class="chip ' + chipMode(m.mode) + '">'
          + (m.mode === 'Free' ? 'حرة' : 'قالب') + '</span>'
          + (m.templateName ? '<span class="why">' + esc(m.templateName) + '</span>' : '')
          + '</td>'
        + '<td><span class="chip ' + badCls + '">' + esc(arStatus(m.status)) + '</span>'
          + (m.fallbackFrom
              ? '<span class="why">تحويل من ' + esc(arChannel(m.fallbackFrom)) + '</span>'
              : '')
          + '</td>'
        + '<td>' + (m.costEstimated > 0 ? usd(m.costEstimated) : '—') + '</td>'
        + '<td><span class="why">' + esc(m.routeReason || m.errorMessage || '') + '</span></td>'
        + '</tr>';
    }).join('');

    table.innerHTML = head + '<tbody>' + body + '</tbody>';
  } catch (e) {
    table.innerHTML = '<tbody><tr><td>❌ ' + esc(e.message) + '</td></tr></tbody>';
  }
}

// ═══════════════════════════════════════════════════════════════════════
//  ١٢. التشغيل — مفاتيح الطوارئ والمحاكاة
// ═══════════════════════════════════════════════════════════════════════

function initOps() {
  // ── مفاتيح الإيقاف ──────────────────────────────────────────────
  $$('[data-kill]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const scope  = btn.dataset.kill;        // unofficial | global
      const killed = btn.dataset.on === '1';
      busy('opsOut', 'بنحدّث المفتاح…');
      try {
        const r = await post('/api/ops/kill-switch/' + scope + '?killed=' + killed
          + '&reason=' + encodeURIComponent('من لوحة التحكم'));

        const msg = r.message || (scope === 'global'
          ? (killed ? '🛑 كل الإرسال اتوقف — حتى الرسايل الحرجة'
                    : '🟢 الإرسال رجع طبيعي')
          : '');

        setHtml('opsOut', resultCard(killed ? 'bad' : 'good',
          killed ? '🛑 اتوقف' : '▶️ اتشغّل',
          '<p class="lead">' + esc(msg) + '</p>'
          + '<div class="note">⏱️ المفتاح بيشتغل <b>فوراً</b> بدون deploy ولا '
            + 'إعادة تشغيل. ده الفرق بين إنك توقّف مشكلة في ثانية، أو تقعد '
            + '١٥ دقيقة تعمل build وأنت بتخسر.</div>'));
        await loadOverview();
      } catch (e) { setHtml('opsOut', errorCard(e)); }
    });
  });

  // ── محاكاة الأعطال ──────────────────────────────────────────────
  $$('[data-sim]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const what = btn.dataset.sim;
      busy('opsOut', 'بنحاكي…');
      try {
        let html = '';

        if (what === 'official-down') {
          const r = await post('/api/ops/simulate/provider?channel=Official&down=true&degraded=false');
          html = resultCard('bad', '💥 القناة الرسمية وقعت',
            '<p class="lead">' + esc(r.message) + '</p>'
            + '<div class="note">🔴 جرّب دلوقتي في «المختبر الحيّ»: '
              + 'ابعت <b>حملة ترويجية</b> لعميل بره النافذة. النظام هيقول <b>لأ</b> '
              + 'ومش هيحوّلها لغير الرسمي. ده تسويق بارد، والتحويل ده = حظر مؤكد.'
              + '<br>بالمقابل، ابعت <b>OTP</b> أو <b>تأكيد أوردر</b> لعميل نافذته '
              + 'مفتوحة — هتلاقيه بيتحوّل لغير الرسمي عادي، لأن الموثوقية '
              + 'هنا أهم من التكلفة.</div>');
        }
        else if (what === 'unofficial-down') {
          const r = await post('/api/ops/simulate/provider?channel=Unofficial&down=true&degraded=false');
          html = resultCard('warn', '💥 القناة غير الرسمية وقعت',
            '<p class="lead">' + esc(r.message) + '</p>'
            + '<div class="note">هنا التدهور آمن: كل حاجة بترجع للقناة '
              + 'الرسمية. بندفع أكتر، بس مفيش رسالة بتضيع ومفيش رقم في خطر.</div>');
        }
        else if (what === 'red') {
          const r = await post('/api/ops/simulate/tier?tier=TIER_1K&quality=Red');
          html = resultCard('bad', '🔴 الجودة نزلت أحمر',
            '<p class="lead">' + esc(r.note) + '</p>'
            + '<div class="note">التسويق اتوقف <b>أوتوماتيك</b> — مش محتاج حد '
              + 'ياخد قرار. الرسايل الحرجة (OTP، تأكيد أوردر) فاضلة شغّالة، '
              + 'لأن إيقافها بيعمل ضرر أكبر من الاستمرار.</div>');
        }
        else { // reset
          await post('/api/ops/simulate/provider?channel=Official&down=false&degraded=false');
          await post('/api/ops/simulate/provider?channel=Unofficial&down=false&degraded=false');
          await post('/api/ops/simulate/tier?tier=TIER_1K&quality=Green');
          await post('/api/ops/kill-switch/unofficial?killed=false');
          await post('/api/ops/kill-switch/global?killed=false');
          html = resultCard('good', '♻️ كل حاجة رجعت طبيعي',
            '<p class="lead">المزوّدين سليمين، الجودة خضراء، المفاتيح مفتوحة.</p>');
        }

        setHtml('opsOut', html);
        await loadOverview();
      } catch (e) { setHtml('opsOut', errorCard(e)); }
    });
  });
}

// ═══════════════════════════════════════════════════════════════════════
//  ١٣. الإقلاع
// ═══════════════════════════════════════════════════════════════════════

function initButtons() {
  const bind = (id, fn) => { const n = el(id); if (n) n.addEventListener('click', fn); };

  bind('btnPreview', doPreview);
  bind('btnCtwa',    doCtwa);
  bind('btnInbound', doInbound);
  bind('btnSend',    doSend);
  bind('btnIdem',    doIdem);
  bind('btnPlan',    loadPlan);
  bind('btnGates',   loadGates);

  bind('btnRefresh', async () => {
    const b = el('btnRefresh');
    if (b) { b.disabled = true; b.textContent = '⏳ …'; }
    try {
      const phone = currentPhone(), intent = currentIntent();
      await Promise.all([loadOverview(), loadPickers()]);
      if (phone && intent) restoreSelection(phone, intent);
      if (lazyLoaded.has('matrix'))   await loadMatrix();
      if (lazyLoaded.has('messages')) await loadMessages();
    } catch (e) {
      console.error('فشل التحديث', e);
    } finally {
      if (b) { b.disabled = false; b.textContent = '↻ تحديث'; }
    }
  });

  // Enter في خانة القطاع = اعمل الخطة
  const seg = el('inpSegment');
  if (seg) seg.addEventListener('keydown', e => { if (e.key === 'Enter') loadPlan(); });
}

async function boot() {
  initTabs();
  initButtons();
  initOps();

  // شريط الحالة الأول — بيقول للمدير إحنا شغّالين على أنهي stack وأنهي قاعدة
  try {
    const h = await get('/health');
    setText('pStack', h.stack || 'ASP.NET Core 8');
    setText('pDb', h.dbProvider === 'SqlServer' ? '🗄️ SQL Server' : '🗄️ SQLite (تطوير)');
  } catch (e) {
    const p = el('pHealth');
    if (p) { p.className = 'pill bad'; p.textContent = '❌ السيرفر مش شغّال'; }
    return;
  }

  try {
    await Promise.all([loadOverview(), loadPickers()]);
  } catch (e) {
    console.error('فشل التحميل الأولي', e);
    const p = el('pHealth');
    if (p) { p.className = 'pill bad'; p.textContent = '⚠️ ' + e.message; }
  }
}

document.addEventListener('DOMContentLoaded', boot);
