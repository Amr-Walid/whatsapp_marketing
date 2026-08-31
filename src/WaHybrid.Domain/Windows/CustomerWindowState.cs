using WaHybrid.Domain.Enums;

namespace WaHybrid.Domain.Windows;

/// <summary>
/// حالة نوافذ العميل لحظة القرار — أهم كائن في النظام.
/// docs/09 §3: الإجابة على "إيه النافذة المفتوحة دلوقتي؟" بتحدّد القناة والتكلفة.
/// </summary>
public sealed record CustomerWindowState(
    WindowState State,
    DateTimeOffset? FepUntil,
    DateTimeOffset? CswUntil)
{
    /// <summary>هل الرسالة الحرة مسموحة؟ (FEP أو CSW مفتوحة)</summary>
    public bool FreeFormAllowed => State is WindowState.FepOpen or WindowState.CswOpen;

    /// <summary>🎁 التسويق مجاني في FEP بس</summary>
    public bool MarketingFree => State == WindowState.FepOpen;

    /// <summary>الساعات الباقية في نافذة FEP — بتحدد لو نكمّل محادثة على الرسمي</summary>
    public double FepHoursLeft => FepUntil is null
        ? 0
        : Math.Max(0, (FepUntil.Value - DateTimeOffset.UtcNow).TotalHours);

    public double CswHoursLeft => CswUntil is null
        ? 0
        : Math.Max(0, (CswUntil.Value - DateTimeOffset.UtcNow).TotalHours);

    public static CustomerWindowState None => new(WindowState.NoWindow, null, null);

    /// <summary>بناء الحالة من تواريخ الانتهاء + قواعد الأسبقية (docs/09 §3.2)</summary>
    public static CustomerWindowState From(DateTimeOffset? fep, DateTimeOffset? csw, DateTimeOffset now)
    {
        var fepOpen = fep.HasValue && fep.Value > now;
        var cswOpen = csw.HasValue && csw.Value > now;

        // 🔑 الأسبقية: FEP تكسب دايماً (لأنها مجانية)، بعدها CSW، والافتراضي NO_WINDOW
        var state = fepOpen ? WindowState.FepOpen
                  : cswOpen ? WindowState.CswOpen
                  : WindowState.NoWindow;

        return new CustomerWindowState(state, fepOpen ? fep : null, cswOpen ? csw : null);
    }
}

/// <summary>مدد النوافذ — من كارت Meta (docs/08 §2)</summary>
public static class WindowDurations
{
    /// <summary>🎁 نافذة الدخول المجاني — 72 ساعة، وفاضلة مجانية بعد أكتوبر 2026</summary>
    public const int FepHours = 72;

    /// <summary>🟡 نافذة خدمة العميل — 24 ساعة، بتتجدّد مع كل رسالة داخلة</summary>
    public const int CswHours = 24;
}

/// <summary>إيه اللي فتح النافذة — مهم للتحليل والتدقيق</summary>
public static class WindowSources
{
    public const string CtwaAd = "ctwa_ad";
    public const string PageCta = "page_cta";
    public const string InboundMessage = "inbound_message";
    public const string InboundReply = "inbound_reply";
    public const string ManualSeed = "manual_seed";
}
