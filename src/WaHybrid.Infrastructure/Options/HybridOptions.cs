using WaHybrid.Domain.Enums;

namespace WaHybrid.Infrastructure.Options;

/// <summary>
/// كل حدود الأمان في الإعدادات. docs/10 §2.
/// 🔑 المبدأ: لما تخاف بالليل، بتغيّر رقم وبتعمل restart — مش بتعدّل كود.
/// </summary>
public sealed class HybridOptions
{
    public const string SectionName = "Hybrid";

    public ChannelsOptions Channels { get; set; } = new();
    public OfficialOptions Official { get; set; } = new();
    public UnofficialOptions Unofficial { get; set; } = new();
    public PolicyOptions Policy { get; set; } = new();
    public CostOptions Cost { get; set; } = new();
    public TierOptions Tier { get; set; } = new();
    public PricingOptions Pricing { get; set; } = new();
}

public sealed class ChannelsOptions
{
    public bool OfficialEnabled { get; set; } = true;
    public bool UnofficialEnabled { get; set; } = true;

    /// <summary>live = مزوّد حقيقي، mock = وهمي بدون تكلفة ولا خطر</summary>
    public string ProviderMode { get; set; } = "mock";

    public double MockFailRate { get; set; } = 0.0;
}

public sealed class OfficialOptions
{
    public string GraphVersion { get; set; } = "v21.0";
    public string? PhoneNumberId { get; set; }
    public string? WabaId { get; set; }

    /// <summary>System User token — دايم، مش مؤقت</summary>
    public string? AccessToken { get; set; }

    public string? WebhookVerifyToken { get; set; }

    /// <summary>🔐 للتحقق من توقيع الـ webhook — بدونه أي حد يقدر يزوّر أحداث</summary>
    public string? AppSecret { get; set; }
}

public sealed class UnofficialOptions
{
    public string? EvolutionBaseUrl { get; set; }
    public string? EvolutionApiKey { get; set; }

    /// <summary>قائمة الجلسات — الموزّع يقرأها ويوزّع عليها</summary>
    public List<string> Instances { get; set; } = new();

    /// <summary>التأخير البشري (docs/03) — Gaussian mean/stdDev بالمللي ثانية</summary>
    public int DelayMeanMs { get; set; } = 45_000;
    public int DelayStdDevMs { get; set; } = 18_000;

    /// <summary>في التطوير خلّيه true — مش هنستنى 45 ثانية في الاختبار</summary>
    public bool SkipDelayInDev { get; set; } = true;
}

/// <summary>سياسة التوجيه — تغيّرها بدون deploy</summary>
public sealed class PolicyOptions
{
    /// <summary>official | unofficial — للتسويق البارد. 🔴 خلّيها official</summary>
    public ChannelKind MarketingChannel { get; set; } = ChannelKind.Official;

    /// <summary>محادثة داخل FEP تفضل على الرسمي (مجانية) — docs/09 §4.4</summary>
    public bool KeepFepConversationsOfficial { get; set; } = true;

    /// <summary>لازم يفضل من نافذة FEP كام ساعة عشان نكمّل محادثة عليها</summary>
    public double FepMinHoursToKeepConversation { get; set; } = 2.0;

    /// <summary>سقفنا الموحّد — أشدّ من سقف Meta. موصى به: 1</summary>
    public int MarketingPerCustomerPer24h { get; set; } = 1;

    /// <summary>تقدير محافظ لسقف 131049 عند Meta</summary>
    public int MetaMarketingCapAssumed { get; set; } = 2;

    /// <summary>🔴 خلّيها false — التسويق ملهوش fallback لغير الرسمي</summary>
    public bool AllowMarketingFallback { get; set; }

    /// <summary>نسبة الترافيك اللي بتمر على الـ Router الجديد (5 → 25 → 60 → 100)</summary>
    public int RouteSamplePct { get; set; } = 100;
}

/// <summary>💰 حدود التكلفة — حزام أمان مالي</summary>
public sealed class CostOptions
{
    public decimal DailyLimitUsd { get; set; } = 50m;
    public decimal MonthlyLimitUsd { get; set; } = 800m;
    public double AlertAtPct { get; set; } = 70;

    /// <summary>يوقف **التسويق بس** أوتوماتيك — المعاملات الحرجة تفضل ماشية</summary>
    public double HardStopAtPct { get; set; } = 100;
}

public sealed class TierOptions
{
    /// <summary>نستخدم 95% بس من الحد</summary>
    public double SafetyMargin { get; set; } = 0.95;

    /// <summary>نحجز 10% للمعاملات الحرجة</summary>
    public double ReserveForCritical { get; set; } = 0.10;
}

/// <summary>
/// ⚠️ الأسعار دي **افتراضية** — لازم تتحقق منها من كارت Meta بنفسك.
/// docs/08 §4.2: Meta بتغيّر الأسعار أول يوم في كل ربع سنة بإشعار شهر.
/// </summary>
public sealed class PricingOptions
{
    public string DefaultCountry { get; set; } = "EG";

    /// <summary>عمولة الـ BSP لكل رسالة — منفصلة عن سعر Meta</summary>
    public decimal BspFeePerMessage { get; set; } = 0.005m;

    /// <summary>مفتاح: "EG:Marketing" → السعر</summary>
    public Dictionary<string, decimal> Rates { get; set; } = new()
    {
        ["EG:Marketing"] = 0.0300m,
        ["EG:Utility"] = 0.0050m,
        ["EG:Authentication"] = 0.0060m,
        ["EG:Service"] = 0.0000m,   // 🔴 هيبقى مدفوع من 1 أكتوبر 2026
        ["US:Marketing"] = 0.0250m,
        ["US:Utility"] = 0.0034m,
        ["US:Authentication"] = 0.0034m,
        ["US:Service"] = 0.0000m,
    };
}
