using WaHybrid.Domain.Enums;

namespace WaHybrid.Infrastructure.Providers;

/// <summary>
/// خريطة أخطاء Meta Cloud API. docs/09 §2.2.
///
/// 🔑 ليه ده مهم لدرجة إنه ملف لوحده؟
/// لأن الفرق بين "أعيد المحاولة بعد دقيقة" و"متبعتش لحد ده تاني أبداً"
/// هو الفرق بين نظام محترم ونظام بيحرق سمعة الرقم. كود الخطأ 131026
/// (الرقم مش على واتساب) لو أعدت المحاولة عليه ١٠ مرات، بتقول لـ Meta
/// "أنا بابعت لأرقام عشوائية" — والجودة بتنزل أحمر.
/// </summary>
public sealed record MetaErrorRule(
    string Code,
    bool Retryable,
    bool Fatal,
    int? RetryAfterMs,
    string ArabicMeaning,
    string Action);

public static class MetaErrorMap
{
    private static readonly Dictionary<string, MetaErrorRule> Rules = new(StringComparer.Ordinal)
    {
        // ── سقف تكرار التسويق (docs/08 §3) ──
        // مش خطأ فني! ده Meta بتقول "العميل ده خد رسايل تسويقية كفاية النهاردة".
        // ⚠️ Retryable=false و Fatal=false: يعني متعيدش دلوقتي، بس العميل سليم
        //    وينفع تبعتله بكرة. لو حسبتها Fatal هتخسر عميل بالغلط.
        ["131049"] = new("131049", false, false, null,
            "سقف رسايل التسويق للعميل ده خلص خلال ٢٤ ساعة (على مستوى كل الشركات، مش إحنا بس)",
            "أجّل لبكرة — ومتحاولش تلفّ عليه برقم تاني، السقف على العميل مش على الراسل"),

        // ── الرقم مش على واتساب ──
        ["131026"] = new("131026", false, true, null,
            "الرقم ده مش مسجّل على واتساب (أو الرسالة مش قابلة للتوصيل)",
            "🔴 Fatal — حوّله لقائمة الحظر فوراً، وأعِد المحاولة أبداً"),

        // ── محاولة رسالة حرة بره النافذة ──
        ["131047"] = new("131047", false, false, null,
            "مرّ أكتر من ٢٤ ساعة من آخر رسالة للعميل — الرسالة الحرة ممنوعة",
            "الـ Router غلط في القرار. حوّل لقالب معتمد وسجّل الحادثة (bug في WindowTracker)"),

        // ── العميل مبعتش رسالة ومحتاج قالب ──
        ["131048"] = new("131048", false, false, null,
            "حد الإرسال للعميل ده اتجاوز — Meta بتقيّد الأرقام اللي جودتها واطية",
            "أوقف الحملة، افحص الجودة، ونزّل السرعة"),

        // ── Rate limit ──
        ["130429"] = new("130429", true, false, 60_000,
            "معدل الإرسال أعلى من اللازم (rate limit)",
            "أعِد المحاولة بعد ٦٠ ثانية بـ backoff"),

        // ── حد الحساب اليومي (Messaging Tier) ──
        ["133016"] = new("133016", true, false, 300_000,
            "الحد اليومي للحساب (Messaging Tier) خلص",
            "أعِد بعد ٥ دقايق — أو استنى تصفير UTC. وقّف الحملات التسويقية"),

        // ── القالب مش موجود / مرفوض ──
        ["132000"] = new("132000", false, true, null,
            "عدد متغيرات القالب مش مطابق للمعتمد عند Meta",
            "🔴 Fatal — bug في TemplateRegistry.Build، صلّح الكود"),

        ["132001"] = new("132001", false, true, null,
            "القالب ده مش موجود (أو مرفوض/محذوف)",
            "🔴 Fatal — شغّل syncTemplates، واستخدم قالب بديل لنفس النية"),

        ["132005"] = new("132005", false, true, null,
            "نص القالب اتغيّر بعد الاعتماد — Meta رفضت",
            "🔴 Fatal — قدّم نسخة جديدة v2 واربطها بنفس النية"),

        // ── مشاكل التوكن / الصلاحيات ──
        ["190"] = new("190", false, false, null,
            "التوكن انتهى أو ملغي",
            "🚨 نبّه فوراً — استخدم System User token دايم مش توكن مؤقت"),

        ["80007"] = new("80007", true, false, 120_000,
            "تجاوز حد استدعاءات API (throttling على مستوى التطبيق)",
            "أعِد بعد دقيقتين"),

        // ── خطأ مؤقت عام ──
        ["4"] = new("4", true, false, 120_000,
            "خطأ مؤقت عند Meta (application request limit)",
            "أعِد بعد دقيقتين"),

        ["1"] = new("1", true, false, 30_000,
            "خطأ غير معروف عند Meta (API Unknown)",
            "أعِد بعد ٣٠ ثانية، ولو تكرر أكتر من ٣ مرات نبّه"),

        ["2"] = new("2", true, false, 60_000,
            "الخدمة مؤقتاً غير متاحة عند Meta",
            "أعِد بعد دقيقة"),
    };

    /// <summary>القاعدة الافتراضية لأي كود مش معروف — محافظة: متعيدش، ومتعتبرهاش نهائية</summary>
    private static readonly MetaErrorRule Unknown = new("unknown", false, false, null,
        "كود خطأ غير معروف", "سجّل الخطأ كامل ونبّه — محتاج تحليل بشري");

    public static MetaErrorRule Resolve(string? code)
        => code is not null && Rules.TryGetValue(code, out var r) ? r : Unknown;

    public static IReadOnlyCollection<MetaErrorRule> All => Rules.Values;
}

/// <summary>
/// حدود الـ Messaging Tier. docs/10 §3.
/// ⚠️ Meta بتعيد تقييم الـ tier كل ٦ ساعات، والتصفير على UTC مش التوقيت المحلي.
/// ومن أكتوبر ٢٠٢٥ الحد بقى على مستوى الـ portfolio كله مش الرقم الواحد.
/// </summary>
public static class TierLimits
{
    public static readonly Dictionary<string, int> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TIER_50"] = 50,
        ["TIER_250"] = 250,
        ["TIER_1K"] = 1_000,
        ["TIER_10K"] = 10_000,
        ["TIER_100K"] = 100_000,
        ["TIER_UNLIMITED"] = int.MaxValue,
        ["UNLIMITED"] = int.MaxValue
    };

    public static int For(string tier) => Map.TryGetValue(tier, out var v) ? v : 250;

    /// <summary>الترتيب التصاعدي — للعرض في الداشبورد</summary>
    public static readonly string[] Ladder =
        ["TIER_250", "TIER_1K", "TIER_10K", "TIER_100K", "TIER_UNLIMITED"];
}
