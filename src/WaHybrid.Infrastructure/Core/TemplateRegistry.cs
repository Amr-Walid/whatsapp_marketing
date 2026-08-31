using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Entities;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Intents;
using WaHybrid.Infrastructure.Data;

namespace WaHybrid.Infrastructure.Core;

/// <summary>
/// سجل القوالب. docs/10 §5.
///
/// 🔑 الفكرة الجوهرية: **الكود بيعرف نوايا، مش أسماء قوالب.**
///
/// لما قالب <c>order_confirmed_ar</c> يترفض من Meta، إنت مش بتعدّل كود —
/// بتعمل <c>order_confirmed_ar_v2</c> وتربطه بنفس النية
/// (<c>Intent = "order_confirmed"</c>) وتوقف القديم. النظام بياخد الأحدث
/// المعتمد أوتوماتيك. ده الفرق بين نظام صيانته دقيقة ونظام صيانته يوم.
/// </summary>
public sealed class TemplateRegistry : ITemplateRegistry
{
    private readonly HybridDbContext _db;
    private readonly ILogger<TemplateRegistry> _log;

    public TemplateRegistry(HybridDbContext db, ILogger<TemplateRegistry> log)
        => (_db, _log) = (db, log);

    /// <summary>
    /// أحدث قالب **معتمد وغير موقوف وغير أحمر** للنية دي.
    /// لو رجع null → مفيش قالب صالح → الرسالة بره النافذة مستحيلة.
    /// </summary>
    public async Task<WaTemplate?> ForIntentAsync(string intent, string lang = "ar",
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var candidates = await _db.WaTemplates
            .Where(t => t.Intent == intent
                        && t.Language == lang
                        && t.Status == TemplateStatus.Approved)
            .ToListAsync(ct);

        // ⚠️ الترتيب في الذاكرة مقصود، لسببين:
        //   ١. `ApprovedAt ?? CreatedAt` جوه ORDER BY على DateTimeOffset
        //      مش مدعوم في SQLite (بيرمي NotSupportedException). وإحنا
        //      عايزين **نفس الكود بالحرف** يشتغل على SQL Server و SQLite.
        //   ٢. مفيش فرق أداء: القوالب لكل نية عددها بالوحدات (٥ قوالب
        //      في النظام كله)، والـ WHERE فوق بيستخدم index (intent, status).
        //      فإحنا بنرتّب ٢-٣ صفوف — مش ملايين.
        //
        // 🔴 والفلترة النهائية (IsUsable) لازم تكون في الذاكرة برضه لأن
        //    فيها منطق مركّب: موقوف؟ أحمر؟ فترة الإيقاف خلصت؟
        return candidates
            .OrderByDescending(t => t.ApprovedAt ?? t.CreatedAt)
            .FirstOrDefault(t => t.IsUsable(now));
    }

    public Task<WaTemplate?> GetAsync(string name, CancellationToken ct = default)
        => _db.WaTemplates.FirstOrDefaultAsync(t => t.Name == name, ct);

    /// <summary>
    /// بناء payload القالب.
    ///
    /// ⚠️ أخطر جزء: **الترتيب**. Meta بتتعامل مع المتغيرات كأرقام
    /// ({{1}}, {{2}}) مش أسماء. لو عكست الترتيب، العميل هيستلم
    /// "أوردر رقم أحمد جاهز يا 12345". و Meta بترجّع 132000 لو العدد غلط.
    ///
    /// عشان كده <c>RequiredParamsJson</c> مصفوفة **مرتّبة** بأسماء المتغيرات،
    /// وإحنا بنملأ حسب ترتيبها بالظبط.
    /// </summary>
    public TemplatePayload Build(WaTemplate template, IReadOnlyDictionary<string, string> parameters)
    {
        var required = ParseParams(template.RequiredParamsJson);
        var ordered = new List<string>(required.Count);

        foreach (var name in required)
        {
            if (!parameters.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            {
                _log.LogWarning(
                    "⚠️ متغيّر ناقص '{Param}' في القالب {Template} — هيتبعت شرطة",
                    name, template.Name);
                value = "-";   // Meta ترفض القيم الفاضية تماماً
            }

            // 🔑 Meta بترفض أسطر جديدة و tabs جوه متغيّر القالب
            ordered.Add(value.Replace("\n", " ").Replace("\t", " ").Trim());
        }

        return new TemplatePayload
        {
            Name = template.Name,
            Language = template.Language,
            Category = template.Category,
            Parameters = ordered
        };
    }

    public static List<string> ParseParams(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    /// <summary>
    /// 🔴 قالب جودته بقت حمراء → إيقاف ٢٤ ساعة أوتوماتيك. docs/10 §5.4.
    /// بيتنده من مهمة المزامنة مع Meta.
    /// </summary>
    public async Task ApplySyncAsync(string name, TemplateStatus status, QualityRating? quality,
        string? rejectedReason, CancellationToken ct = default)
    {
        var t = await _db.WaTemplates.FirstOrDefaultAsync(x => x.Name == name, ct);
        if (t is null) return;

        t.Status = status;
        t.Quality = quality;
        t.RejectedReason = rejectedReason;
        t.LastSyncedAt = DateTimeOffset.UtcNow;

        if (status == TemplateStatus.Approved && t.ApprovedAt is null)
            t.ApprovedAt = DateTimeOffset.UtcNow;

        if (quality == QualityRating.Red)
        {
            t.PausedUntil = DateTimeOffset.UtcNow.AddHours(24);
            _log.LogWarning("🔴 القالب {Name} بقى أحمر — اتوقف ٢٤ ساعة أوتوماتيك", name);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// فاحص القوالب قبل التقديم لـ Meta. docs/10 §5.5.
    /// بيوفّر أيام انتظار: كل مخالفة هنا = رفض مؤكّد + ٢٤–٤٨ ساعة ضايعة.
    /// </summary>
    public static List<string> Lint(string bodyText, MetaCategory category)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(bodyText))
            issues.Add("النص فاضي");

        if (bodyText.Length > 1024)
            issues.Add($"النص أطول من ١٠٢٤ حرف ({bodyText.Length})");

        // متغيّر في أول أو آخر النص = رفض مؤكّد
        var trimmed = bodyText.Trim();
        if (trimmed.StartsWith("{{", StringComparison.Ordinal))
            issues.Add("🔴 النص بيبدأ بمتغيّر — Meta بترفض ده صريح");
        if (trimmed.EndsWith("}}", StringComparison.Ordinal))
            issues.Add("🔴 النص بينتهي بمتغيّر — Meta بترفض ده صريح");

        // متغيّرين ورا بعض
        if (bodyText.Contains("}}{{", StringComparison.Ordinal)
            || bodyText.Contains("}} {{", StringComparison.Ordinal))
            issues.Add("🔴 متغيّرين ملزوقين ({{1}} {{2}}) — Meta بترفض");

        // ترقيم المتغيرات لازم يكون متسلسل من ١
        var nums = System.Text.RegularExpressions.Regex
            .Matches(bodyText, @"\{\{(\d+)\}\}")
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct().OrderBy(x => x).ToList();

        for (var i = 0; i < nums.Count; i++)
            if (nums[i] != i + 1)
            {
                issues.Add($"🔴 ترقيم المتغيرات مش متسلسل — متوقع {{{{{i + 1}}}}} ولقيت {{{{{nums[i]}}}}}");
                break;
            }

        // كلمات بتودّي لتصنيف MARKETING إجباري
        string[] marketingWords = ["خصم", "عرض", "أوفر", "تخفيض", "مجاناً", "اشتري", "كوبون", "sale", "offer", "discount"];
        if (category != MetaCategory.Marketing)
        {
            var hit = marketingWords.FirstOrDefault(w =>
                bodyText.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                issues.Add($"⚠️ كلمة '{hit}' هتخلّي Meta تصنّفه MARKETING مش {category} — والسعر هيتغيّر");
        }

        if (bodyText.Contains("http://", StringComparison.OrdinalIgnoreCase))
            issues.Add("⚠️ لينك http غير آمن — استخدم https");

        return issues;
    }

    /// <summary>
    /// 🌱 القوالب العربية الأساسية. docs/10 §5.3.
    /// دي الأربعة اللي بيغطّوا ٩٠٪ من الحالات في متجر إلكتروني.
    /// </summary>
    public static IReadOnlyList<WaTemplate> SeedTemplates() =>
    [
        new WaTemplate
        {
            Name = "order_confirmed_ar",
            Language = "ar",
            Category = MetaCategory.Utility,
            Status = TemplateStatus.Approved,
            Quality = QualityRating.Green,
            Intent = IntentNames.OrderConfirmed,
            BodyText = "أهلاً {{1}} 👋\nأوردرك رقم {{2}} اتأكّد بنجاح.\nالإجمالي: {{3}} جنيه\nهنبعتلك تحديث أول ما يتشحن.",
            FooterText = "شكراً لتعاملك معانا",
            RequiredParamsJson = """["name","order_id","total"]""",
            ApprovedAt = DateTimeOffset.UtcNow.AddDays(-30)
        },
        new WaTemplate
        {
            Name = "order_shipped_ar",
            Language = "ar",
            Category = MetaCategory.Utility,
            Status = TemplateStatus.Approved,
            Quality = QualityRating.Green,
            Intent = IntentNames.OrderShipped,
            // ⚠️ لاحظ إن النص مابينتهيش بمتغيّر. النسخة الأولى كانت
            //    "...التسليم المتوقّع: {{3}}" والفاحص مسكها ورفضها،
            //    لأن Meta بترفض القوالب اللي بتنتهي بمتغيّر صريح
            //    (مش عارفة تتأكد إن الناتج النهائي هيبقى مفهوم).
            //    الاختبار مسك ده قبل ما نقدّم القالب لـ Meta ونستنى أسبوع رفض.
            BodyText = "أوردرك رقم {{1}} في الطريق 🚚\nرقم التتبّع: {{2}}\n"
                     + "التسليم المتوقّع: {{3}} — تابع شحنتك من اللينك تحت.",
            FooterText = "لأي استفسار ابعتلنا",
            RequiredParamsJson = """["order_id","tracking","eta"]""",
            ApprovedAt = DateTimeOffset.UtcNow.AddDays(-30)
        },
        new WaTemplate
        {
            Name = "order_cancelled_ar",
            Language = "ar",
            Category = MetaCategory.Utility,
            Status = TemplateStatus.Approved,
            Quality = QualityRating.Green,
            Intent = IntentNames.OrderCancelled,
            BodyText = "أوردرك رقم {{1}} اتلغى.\nالسبب: {{2}}\nلو ده مش صح كلّمنا فوراً.",
            RequiredParamsJson = """["order_id","reason"]""",
            ApprovedAt = DateTimeOffset.UtcNow.AddDays(-30)
        },
        new WaTemplate
        {
            Name = "promo_generic_ar",
            Language = "ar",
            Category = MetaCategory.Marketing,
            Status = TemplateStatus.Approved,
            Quality = QualityRating.Green,
            Intent = IntentNames.CampaignPromo,
            BodyText = "أهلاً {{1}} 🎉\nعندنا {{2}} لفترة محدودة.\nاكتب \"عايز\" وهنبعتلك التفاصيل.",
            FooterText = "اكتب \"إلغاء\" لوقف الرسايل",
            RequiredParamsJson = """["name","offer"]""",
            ApprovedAt = DateTimeOffset.UtcNow.AddDays(-20)
        },
        new WaTemplate
        {
            Name = "abandoned_cart_ar",
            Language = "ar",
            // ⚠️ مهم: Meta بتصنّف السلة المتروكة MARKETING مش UTILITY
            Category = MetaCategory.Marketing,
            Status = TemplateStatus.Approved,
            Quality = QualityRating.Green,
            Intent = IntentNames.AbandonedCart,
            BodyText = "أهلاً {{1}} 🛒\nسيبت {{2}} في السلة.\nلسه موجود — تحب تكمّل؟",
            FooterText = "اكتب \"إلغاء\" لوقف الرسايل",
            RequiredParamsJson = """["name","item"]""",
            ApprovedAt = DateTimeOffset.UtcNow.AddDays(-15)
        }
    ];
}
