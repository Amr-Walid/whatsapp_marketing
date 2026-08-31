/* ═══════════════════════════════════════════════════════════════════════
   ٠٠٢ — عروض المتابعة (Views) لـ SQL Server
   ─────────────────────────────────────────────────────────────────────
   المصدر: docs/09-HYBRID-ARCHITECTURE.md §6

   ⚠️ ملاحظة مهمة عن الترجمة:
   العروض في التوثيق مكتوبة بصيغة PostgreSQL. الملف ده هو النسخة
   المترجمة لـ T-SQL، وفيه ٣ فروق جوهرية لازم تكون واضحة:

   ١) PostgreSQL بيستخدم COUNT(*) FILTER (WHERE ...)
      T-SQL مافيهاش FILTER — بنستخدم SUM(CASE WHEN ... THEN 1 ELSE 0 END)

   ٢) التوثيق بيقارن نصوص ('delivered', 'official', 'FEP_OPEN')
      بس EF Core بيخزّن الـ enums كأرقام (HasConversion<int>).
      فالمقارنات هنا بالأرقام، والأرقام دي **مربوطة**
      بـ src/WaHybrid.Domain/Enums/Enums.cs:
        MessageDirection: Out=1, In=2
        MessageStatus:    Sending=1, Sent=2, Delivered=3, Read=4,
                          Failed=5, Blocked=6, Skipped=7
        ChannelKind:      Official=1, Unofficial=2
        WindowState:      FepOpen=1, CswOpen=2, NoWindow=3
        MetaCategory:     Marketing=1, Utility=2, Authentication=3, Service=4
      🔴 لو غيّرت أي رقم في الـ enum، لازم تغيّره هنا. الأرقام مكتوبة
         جنبها الاسم في كل CASE عشان ماحدش يغلط.

   ٣) DATE(created_at) في PostgreSQL → CAST(created_at AS date) في T-SQL

   بنستخدم CREATE OR ALTER VIEW عشان الملف يبقى قابل لإعادة التشغيل
   (idempotent) — تشغّله مية مرة ومافيش مشكلة.
   ═══════════════════════════════════════════════════════════════════════ */

GO

/* ══════════════════════════════════════════════════════════════════════
   📊 v_hybrid_dashboard — التوزيع اليومي بالقناة وفئة Meta
   ────────────────────────────────────────────────────────────────────
   بتجاوب على: "بعتنا كام، على أنهي قناة، بأنهي فئة، وصل منهم كام،
   ودفعنا كام على كل رسالة اتسلّمت فعلاً؟"

   🔑 cost_per_delivered هو الرقم الصح للحكم على التكلفة — مش
   cost_per_sent. السبب إن Meta بتحاسبك على **التسليم** مش الإرسال
   (منذ ١ يوليو ٢٠٢٥). فرسالة اتبعتت ومنوصلتش = مادفعناش فيها،
   وحسابها في المتوسط بيوهمك إن التكلفة أقل من الحقيقة.
   ══════════════════════════════════════════════════════════════════════ */
CREATE OR ALTER VIEW v_hybrid_dashboard AS
SELECT
    CAST(created_at AS date)                                   AS [day],
    channel,
    meta_category,
    COUNT(*)                                                   AS sent,

    -- Delivered=3 أو Read=4 → دي اللي Meta بتحاسبنا عليها
    SUM(CASE WHEN status IN (3, 4) THEN 1 ELSE 0 END)           AS delivered,
    SUM(CASE WHEN status = 5 THEN 1 ELSE 0 END)                 AS failed,   -- Failed
    SUM(CASE WHEN status = 6 THEN 1 ELSE 0 END)                 AS blocked,  -- Blocked

    -- نسبة التسليم — NULLIF بتحمينا من القسمة على صفر
    CAST(ROUND(
        100.0 * SUM(CASE WHEN status IN (3, 4) THEN 1 ELSE 0 END)
        / NULLIF(COUNT(*), 0), 1) AS decimal(5, 1))             AS delivery_pct,

    -- cost_billed لو Meta رجّعت الرقم الحقيقي، وإلا التقدير بتاعنا
    CAST(ROUND(SUM(COALESCE(cost_billed, cost_estimated)), 2)
         AS decimal(14, 2))                                     AS cost_usd,

    -- 💵 التكلفة على كل رسالة اتسلّمت فعلاً (الرقم اللي يهم)
    CAST(ROUND(
        SUM(COALESCE(cost_billed, cost_estimated))
        / NULLIF(SUM(CASE WHEN status IN (3, 4) THEN 1 ELSE 0 END), 0), 5)
        AS decimal(14, 5))                                      AS cost_per_delivered

FROM message_log
WHERE direction = 1                                    -- Out (الخارجة بس)
  AND created_at > DATEADD(day, -30, SYSUTCDATETIME())  -- آخر ٣٠ يوم
GROUP BY CAST(created_at AS date), channel, meta_category;

GO

/* ══════════════════════════════════════════════════════════════════════
   🎯 v_hybrid_efficiency — المقياس اللي بيحكم على نجاح النظام كله
   ────────────────────────────────────────────────────────────────────
   free_pct = نسبة الرسايل اللي كلّفتنا **صفر**.

   المستهدف: > 75%
   لو أقل من كده، يبقى واحد من اتنين:
     • الـ Router مش شغّال صح (بيبعت رسمي وهو ممكن يبعت مجاني)
     • أو نسبة كبيرة من العملاء "باردة" (بره النوافذ) — والحل استراتيجي
       مش تقني: محتاج CTWA أكتر عشان تفتح نوافذ FEP.

   ملاحظة على free_official: دي الرسايل اللي مشيت على القناة **الرسمية**
   ومع ذلك كانت **مجانية**، لأن نافذة FEP كانت مفتوحة. الرقم ده هو
   الدليل الملموس على قيمة استراتيجية CTWA — رسايل رسمية بجودة رسمية
   بتكلفة صفر.
   ══════════════════════════════════════════════════════════════════════ */
CREATE OR ALTER VIEW v_hybrid_efficiency AS
SELECT
    CAST(created_at AS date)                                    AS [day],

    -- Unofficial=2 → القناة المجانية دايماً
    SUM(CASE WHEN channel = 2 THEN 1 ELSE 0 END)                AS free_msgs,
    -- Official=1 → مدفوعة بره النوافذ
    SUM(CASE WHEN channel = 1 THEN 1 ELSE 0 END)                AS paid_msgs,

    -- 🎁 رسمية + نافذة FEP مفتوحة (WindowState.FepOpen=1) = مجانية
    SUM(CASE WHEN channel = 1 AND window_state = 1 THEN 1 ELSE 0 END)
                                                                AS free_official,

    -- 🎯 المؤشر الأساسي: نسبة اللي تكلفته صفر
    CAST(ROUND(
        100.0 * SUM(CASE WHEN COALESCE(cost_billed, cost_estimated) = 0
                         THEN 1 ELSE 0 END)
        / NULLIF(COUNT(*), 0), 1) AS decimal(5, 1))             AS free_pct,

    CAST(ROUND(SUM(COALESCE(cost_billed, cost_estimated)), 2)
         AS decimal(14, 2))                                     AS spend_usd

FROM message_log
WHERE direction = 1                                             -- Out
GROUP BY CAST(created_at AS date);

GO

/* ══════════════════════════════════════════════════════════════════════
   📖 طريقة الاستخدام
   ────────────────────────────────────────────────────────────────────
   -- الرقم اللي المدير بيسأل عليه (آخر ١٤ يوم):
   SELECT TOP 14 * FROM v_hybrid_efficiency ORDER BY [day] DESC;

   -- التفصيل بالقناة والفئة:
   SELECT * FROM v_hybrid_dashboard
   WHERE [day] = CAST(GETUTCDATE() AS date)
   ORDER BY cost_usd DESC;

   ⚠️ تنبيه: الـ [day] هنا محسوب على **UTC** مش التوقيت المحلي.
   ده مقصود، لأن Meta بتصفّر حدودها اليومية على منتصف ليل UTC.
   لو حسبتها بالتوقيت المحلي، أرقامك مش هتطابق فاتورة Meta.
   ══════════════════════════════════════════════════════════════════════ */
