# 📊 المرحلة 1: تحليل وتقسيم العملاء (Data Analysis & Segmentation)

> **قبل أي رسالة:** كل رسالة بتبعتها لعميل غير مستهدف = زيادة في احتمال البلاغ = خطوة أقرب للحظر. التقسيم الصح هو **أول وأرخص طبقة حماية من الحظر** قبل أي حاجة تقنية.

---

## 1. تنظيف الأرقام (Phone Normalization) — الخطوة الأهم

### ليه دي حرجة؟
لو بعت لرقم **غلط أو غير موجود على واتساب**، الجلسة بتعمل lookup فاشل. لو عملت 50 lookup فاشل في ساعة → إشارة قوية جداً لـ Meta إنك بوت بيعمل Enumeration (تجميع أرقام). دي من أسرع طرق الحظر.

### القواعد
```
✅ الصيغة المطلوبة: 201012345678 (كود دولي بدون + وبدون 00)
❌ 01012345678       → ناقص كود الدولة
❌ +20 101 234 5678  → مسافات و+
❌ 0020101234567     → 00 بدل +
```

### كود التنظيف (Python)

```python
import re
import pandas as pd
import phonenumbers
from phonenumbers import NumberParseException

DEFAULT_REGION = "EG"  # مصر

def normalize_phone(raw, region=DEFAULT_REGION):
    """
    ترجع الرقم بصيغة E.164 بدون + ، أو None لو غلط
    """
    if pd.isna(raw):
        return None

    s = str(raw).strip()
    # شيل أي حروف عربية/مسافات/رموز
    s = re.sub(r'[^\d+]', '', s)

    # معالجة 00 -> +
    if s.startswith('00'):
        s = '+' + s[2:]

    try:
        num = phonenumbers.parse(s, region)
    except NumberParseException:
        return None

    if not phonenumbers.is_valid_number(num):
        return None

    # ✅ استبعد الأرقام الأرضية والـ VOIP — مش على واتساب
    ntype = phonenumbers.number_type(num)
    if ntype not in (
        phonenumbers.PhoneNumberType.MOBILE,
        phonenumbers.PhoneNumberType.FIXED_LINE_OR_MOBILE,
    ):
        return None

    e164 = phonenumbers.format_number(
        num, phonenumbers.PhoneNumberFormat.E164
    )
    return e164.lstrip('+')


# ── التطبيق ──
df = pd.read_csv('customers_raw.csv')

df['phone_clean'] = df['phone'].apply(normalize_phone)

# تقرير التنظيف
total   = len(df)
invalid = df['phone_clean'].isna().sum()
df = df.dropna(subset=['phone_clean'])
dupes   = df.duplicated(subset=['phone_clean']).sum()
df = df.drop_duplicates(subset=['phone_clean'], keep='last')

print(f"""
📋 تقرير تنظيف الداتا
─────────────────────
الإجمالي الأصلي:   {total}
أرقام غير صالحة:   {invalid}  ({invalid/total*100:.1f}%)
مكرر (تم حذفه):    {dupes}
✅ صالح للإرسال:    {len(df)}
""")

df.to_csv('customers_clean.csv', index=False)
```

### خطوة إضافية حرجة: التحقق من وجود الرقم على واتساب

قبل الحملة، اعمل **Batch Validation** بمعدل بطيء جداً (مش دفعة واحدة!):

```javascript
// Evolution API — تحقق من وجود أرقام على واتساب
// ⚠️ اعمل ده على مدى أيام، 200-300 رقم/يوم كحد أقصى
// وبرقم منفصل مخصص للـ validation (لو اتحظر مش هتخسر حاجة)

async function validateBatch(numbers, instanceName) {
  const results = [];
  for (const num of numbers) {
    try {
      const res = await fetch(
        `${EVO_URL}/chat/whatsappNumbers/${instanceName}`,
        {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'apikey': EVO_KEY,
          },
          body: JSON.stringify({ numbers: [num] }),
        }
      );
      const data = await res.json();
      results.push({ number: num, exists: data[0]?.exists ?? false });
    } catch (e) {
      results.push({ number: num, exists: null, error: e.message });
    }
    // ⏱️ تأخير عشوائي إجباري
    await sleep(3000 + Math.random() * 5000);
  }
  return results;
}
```

> 💡 **بديل أذكى:** متعملش validation منفصل خالص. خلي أول رسالة في الحملة هي الـ validation نفسها — لو فشلت، علّم الرقم `invalid` وكمّل. بكده مش بتعمل نشاط مشبوه زيادة.

---

## 2. تحليل RFM (Recency, Frequency, Monetary)

### الفكرة
تقسّم العملاء لـ 3 أبعاد وتدي كل بعد سكور من 1 لـ 5:

| البُعد | المعنى | السؤال |
|---|---|---|
| **R** ecency | آخر مرة اشترى | امتى آخر أوردر؟ |
| **F** requency | عدد المرات | كم أوردر عمل؟ |
| **M** onetary | القيمة | كم صرف إجمالاً؟ |

### الكود الكامل

```python
import pandas as pd
import numpy as np
from datetime import datetime

# ── جهّز داتا الأوردرات ──
# المطلوب: customer_id, order_date, order_value
orders = pd.read_csv('orders.csv', parse_dates=['order_date'])
snapshot = orders['order_date'].max() + pd.Timedelta(days=1)

rfm = orders.groupby('customer_id').agg(
    recency   = ('order_date',  lambda x: (snapshot - x.max()).days),
    frequency = ('order_date',  'count'),
    monetary  = ('order_value', 'sum'),
    avg_order = ('order_value', 'mean'),
    last_date = ('order_date',  'max'),
).reset_index()

# ── حساب السكورات (Quintiles) ──
# Recency: أقل = أحسن (عكسي)
rfm['R'] = pd.qcut(rfm['recency'], 5, labels=[5,4,3,2,1]).astype(int)
# Frequency & Monetary: أكتر = أحسن
rfm['F'] = pd.qcut(rfm['frequency'].rank(method='first'), 5,
                   labels=[1,2,3,4,5]).astype(int)
rfm['M'] = pd.qcut(rfm['monetary'].rank(method='first'), 5,
                   labels=[1,2,3,4,5]).astype(int)

rfm['RFM_Score'] = rfm['R'] + rfm['F'] + rfm['M']
rfm['RFM_Cell']  = rfm['R'].astype(str) + rfm['F'].astype(str) + rfm['M'].astype(str)
```

### التصنيف لسيجمنتس عملية

```python
def segment(row):
    R, F, M = row['R'], row['F'], row['M']

    # 🏆 الأبطال — أفضل عملاء
    if R >= 4 and F >= 4 and M >= 4:
        return 'champions'

    # 💎 مخلصون — بيشتروا كتير
    if R >= 3 and F >= 4:
        return 'loyal'

    # 🌱 جدد وواعدين — أول أوردر حديث
    if R >= 4 and F <= 2:
        return 'new_promising'

    # 💰 عالي القيمة لكن غايب
    if R <= 2 and M >= 4:
        return 'cant_lose'   # ⚠️ أولوية قصوى للاسترجاع

    # ⚠️ في خطر الفقدان
    if R <= 2 and F >= 3:
        return 'at_risk'

    # 😴 خامل — تنشيط
    if R <= 2 and F <= 2:
        return 'hibernating'

    # 🤔 يحتاج انتباه
    if R == 3:
        return 'need_attention'

    return 'others'

rfm['segment'] = rfm.apply(segment, axis=1)

# ── تقرير السيجمنتس ──
summary = rfm.groupby('segment').agg(
    count        = ('customer_id', 'count'),
    avg_monetary = ('monetary', 'mean'),
    total_value  = ('monetary', 'sum'),
    avg_recency  = ('recency', 'mean'),
).sort_values('total_value', ascending=False)

summary['pct_of_base']    = (summary['count'] / len(rfm) * 100).round(1)
summary['pct_of_revenue'] = (summary['total_value'] / rfm['monetary'].sum() * 100).round(1)

print(summary)
```

### مصفوفة الرسائل لكل سيجمنت

| السيجمنت | الأولوية | نوع الرسالة | العرض | التوقيت |
|---|---|---|---|---|
| **champions** | 🥇 1 | Early Access / VIP | منتج جديد قبل الجميع، مفيش خصم | أول دفعة |
| **loyal** | 🥇 2 | Cross-sell | منتج مكمل لشراياته | أول دفعة |
| **cant_lose** | 🥇 3 | Win-back قوي | خصم كبير + رسالة شخصية | أول دفعة |
| **at_risk** | 🥈 4 | Reactivation | كود خصم 15-20% | دفعة 2 |
| **new_promising** | 🥈 5 | Onboarding | تعريف بباقي المنتجات | دفعة 2 |
| **need_attention** | 🥉 6 | عرض عام | خصم متوسط | دفعة 3 |
| **hibernating** | 🥉 7 | آخر محاولة | أقوى عرض عندك | دفعة أخيرة |
| **others** | ⏸️ | متبعتلهمش | — | استبعاد |

> ⚠️ **قاعدة مهمة جداً:** ابدأ الحملة بالـ **champions و loyal** — الناس دي بترد وبتتفاعل. ده يبني **Reply Ratio عالي** لأرقامك من أول يوم، وبيرفع سكور الثقة عند Meta قبل ما تدخل على السيجمنتس اللي أقل تفاعلاً. لو بدأت بالـ hibernating، هتاخد 0% رد + بلاغات = موت الأرقام.

---

## 3. Cross-Selling: ترشيح المنتجات المكمّلة

### طريقة بسيطة وفعالة: Market Basket (Co-occurrence)

```python
from itertools import combinations
from collections import Counter

# order_items: order_id, product_id
baskets = order_items.groupby('order_id')['product_id'].apply(list)

# احسب المنتجات اللي بتتشرى مع بعض
pair_counts = Counter()
for items in baskets:
    for a, b in combinations(sorted(set(items)), 2):
        pair_counts[(a, b)] += 1

# ابني قاموس التوصيات
from collections import defaultdict
recommendations = defaultdict(list)
for (a, b), count in pair_counts.most_common():
    recommendations[a].append((b, count))
    recommendations[b].append((a, count))

def recommend_for_customer(cid, top_n=2):
    """رشح منتجات لعميل بناءً على شراياته"""
    bought = set(
        order_items[order_items.customer_id == cid]['product_id']
    )
    scores = Counter()
    for p in bought:
        for rec, cnt in recommendations.get(p, []):
            if rec not in bought:
                scores[rec] += cnt
    return [p for p, _ in scores.most_common(top_n)]
```

### طريقة أذكى: Lift Score
```python
def lift(a, b, total_orders):
    """
    Lift > 1 = ارتباط حقيقي
    Lift = 1 = عشوائي
    """
    p_a  = product_freq[a] / total_orders
    p_b  = product_freq[b] / total_orders
    p_ab = pair_counts[(min(a,b), max(a,b))] / total_orders
    return p_ab / (p_a * p_b) if p_a * p_b > 0 else 0

# خد بس الأزواج اللي lift > 1.5
strong_pairs = {
    pair: lift(*pair, len(baskets))
    for pair in pair_counts
    if lift(*pair, len(baskets)) > 1.5
}
```

---

## 4. Opt-in & Suppression Lists — الطبقة القانونية والأمنية

### جداول لازم تكون عندك

```sql
-- ✅ قائمة الموافقة
CREATE TABLE opt_in (
  customer_id   BIGINT PRIMARY KEY,
  phone         VARCHAR(20) NOT NULL,
  opted_in_at   TIMESTAMPTZ NOT NULL,
  source        VARCHAR(50),  -- 'checkout_checkbox' | 'purchase' | 'landing_form'
  proof         TEXT          -- IP / screenshot ref / order_id
);

-- ❌ قائمة الحظر المطلق (لا تُرسل نهائياً)
CREATE TABLE suppression_list (
  phone       VARCHAR(20) PRIMARY KEY,
  reason      VARCHAR(50) NOT NULL,
  -- 'user_opt_out' | 'reported_spam' | 'blocked_us'
  -- | 'invalid_number' | 'complaint' | 'no_wa_account'
  added_at    TIMESTAMPTZ DEFAULT NOW(),
  added_by    VARCHAR(50)
);

CREATE INDEX idx_supp_phone ON suppression_list(phone);
```

### كلمات الإلغاء اللي لازم البوت يرصدها

```javascript
const OPT_OUT_KEYWORDS = [
  // عربي
  'قف', 'وقف', 'ايقاف', 'إيقاف', 'الغاء', 'إلغاء',
  'الغي', 'مش عايز', 'متبعتلي', 'متبعتليش', 'بلاش',
  'كفاية', 'سيبني', 'شيلني', 'امسحني', 'ابعد',
  'حرام', 'مضايقني', 'ازعاج', 'بلوك',
  // إنجليزي
  'stop', 'unsubscribe', 'remove', 'optout', 'opt out',
  'cancel', 'quit', 'no more', 'leave me',
];

function isOptOut(text) {
  const t = text.toLowerCase().trim()
    .replace(/[أإآ]/g, 'ا')
    .replace(/ى/g, 'ي')
    .replace(/ة/g, 'ه');
  return OPT_OUT_KEYWORDS.some(k =>
    t === k || t.startsWith(k + ' ') || t.includes(k)
  );
}
```

### 🔴 قاعدة حرجة: التعامل الفوري مع الإلغاء

```javascript
async function handleOptOut(phone) {
  // 1. أضف للـ suppression فوراً (قبل أي حاجة)
  await db.query(
    `INSERT INTO suppression_list (phone, reason, added_by)
     VALUES ($1, 'user_opt_out', 'auto_bot')
     ON CONFLICT (phone) DO NOTHING`,
    [phone]
  );

  // 2. ألغِ كل الرسائل المجدولة للرقم ده من الطابور
  await queue.removeJobs(`*:${phone}:*`);

  // 3. رد تأكيد مهذب (مهم جداً — يمنع البلاغ!)
  await sendMessage(phone,
    'تم إيقاف الرسائل نهائياً ✅ نعتذر عن الإزعاج. ' +
    'شكراً لوقتك 🙏'
  );

  // 4. سجّل للتحليل
  await logEvent('opt_out', { phone, at: new Date() });
}
```

> 💡 **الرد المهذب ده هو أرخص تأمين ضد الحظر.** الشخص اللي كان ناوي يعمل Report، لما يشوف إنك احترمت طلبه فوراً، بنسبة كبيرة مش هيعمله.

---

## 5. تقسيم الحملة على الأرقام (Allocation Strategy)

### المبدأ
**متوزعش عشوائي!** لازم توزيع يراعي:
1. سيجمنت العميل (الأولوية)
2. سعة كل رقم (Quota)
3. ما إذا كان العميل كلّم رقم معين قبل كده

```python
def allocate_campaign(customers_df, sessions, prev_contacts):
    """
    customers_df: عملاء مرتبين بالأولوية
    sessions: [{'id': 's1', 'daily_quota': 60, 'sent_today': 0}, ...]
    prev_contacts: {phone: last_session_id}  ← مهم جداً!
    """
    allocation = []

    for _, cust in customers_df.iterrows():
        phone = cust['phone_clean']

        # 🔒 قاعدة ذهبية: لو العميل ده كلّم رقم قبل كده،
        #    ابعتله من نفس الرقم دايماً (Sticky Session)
        preferred = prev_contacts.get(phone)

        session = None
        if preferred:
            s = next((x for x in sessions if x['id'] == preferred), None)
            if s and s['sent_today'] < s['daily_quota']:
                session = s

        # مفيش رقم سابق → اختار الأقل استخداماً
        if session is None:
            available = [
                s for s in sessions
                if s['sent_today'] < s['daily_quota']
            ]
            if not available:
                break  # كل الأرقام وصلت الحد → باقي الحملة بكرة
            session = min(available, key=lambda s: s['sent_today'])

        session['sent_today'] += 1
        allocation.append({
            'phone':      phone,
            'session_id': session['id'],
            'segment':    cust['segment'],
            'priority':   cust['priority'],
        })
        prev_contacts[phone] = session['id']

    return allocation
```

### ليه الـ Sticky Session مهمة؟

```
❌ السيناريو الغلط:
   يوم 1: رقم A يبعت لأحمد "عرض خاص"
   يوم 3: رقم B يبعت لأحمد "متابعة العرض"
   → أحمد مرتبك: مين اللي بيكلمني؟ شركة ولا نصب؟
   → احتمال Report عالي 🔴

✅ السيناريو الصح:
   كل التواصل مع أحمد من رقم A دايماً
   → علاقة مستمرة، ثقة، رد أسهل
   → ولو أحمد رد بعد أسبوع، الـ Listener على A بيلاقيه
```

---

## 6. مخرجات المرحلة دي (Deliverables)

قبل ما تنتقل للمرحلة اللي بعدها، لازم يكون عندك:

```
✅ customers_clean.csv       — أرقام منظفة بصيغة E.164
✅ rfm_segments.csv          — كل عميل + سيجمنته + أولويته
✅ recommendations.json      — منتج مرشح لكل عميل
✅ suppression_list.csv      — مين متبعتلوش نهائياً
✅ opt_in_proof/             — دليل الموافقة لكل عميل (للحماية القانونية)
✅ campaign_allocation.csv   — أي رقم يبعت لأي عميل وإمتى
✅ message_variants.json     — نصوص الرسائل لكل سيجمنت (Spintax)
```

### تقرير جاهزية سريع

```python
def readiness_check(df, suppression, opt_in):
    checks = {
        'أرقام صالحة':          df['phone_clean'].notna().all(),
        'مفيش مكرر':            not df['phone_clean'].duplicated().any(),
        'suppression مطبقة':    not df['phone_clean'].isin(suppression).any(),
        'كل العملاء لهم opt-in': df['customer_id'].isin(opt_in).all(),
        'كل عميل له سيجمنت':    df['segment'].notna().all(),
        'كل عميل له رقم مرسل':  df['session_id'].notna().all(),
    }
    for k, v in checks.items():
        print(f"{'✅' if v else '❌'} {k}")
    return all(checks.values())

if readiness_check(campaign_df, supp_set, optin_set):
    print("\n🚀 جاهز للحملة")
else:
    print("\n🛑 وقّف — صلّح المشاكل الأول")
```

---

**التالي:** [`02-INFRASTRUCTURE.md`](./02-INFRASTRUCTURE.md) — بناء البنية التحتية
