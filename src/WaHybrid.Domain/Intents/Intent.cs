using WaHybrid.Domain.Enums;

namespace WaHybrid.Domain.Intents;

/// <summary>
/// أسماء النوايا — نسخة C# من <c>core/intents.js</c> في docs/09 §4.1.
/// كل رسالة في النظام ليها نية واحدة بالظبط.
/// </summary>
public static class IntentNames
{
    // ── تسويق (يبدأ من عندنا) ──
    public const string CampaignPromo = "campaign_promo";
    public const string Winback = "winback";
    /// <summary>⚠️ Meta بتصنّفها MARKETING مش UTILITY</summary>
    public const string AbandonedCart = "abandoned_cart";
    public const string NewArrival = "new_arrival";

    // ── معاملات (نتيجة فعل من العميل) ──
    public const string OrderConfirmed = "order_confirmed";
    public const string OrderShipped = "order_shipped";
    public const string OrderDelivered = "order_delivered";
    public const string OrderCancelled = "order_cancelled";
    public const string PaymentReminder = "payment_reminder";

    // ── محادثة (رد على العميل) ──
    public const string BotReply = "bot_reply";
    public const string AgentReply = "agent_reply";
    public const string FaqAnswer = "faq_answer";
    public const string CatalogBrowse = "catalog_browse";

    // ── نظام ──
    public const string OptOutAck = "opt_out_ack";
    public const string Otp = "otp";
}

/// <summary>
/// خصائص النية — الـ ChannelRouter بيقرأ منها القرار.
/// </summary>
/// <param name="Name">اسم النية</param>
/// <param name="Class">تصنيفنا الداخلي</param>
/// <param name="Critical">لازم توصل؟ لو أيوه → فيه fallback للقناة التانية</param>
/// <param name="MetaCategory">تصنيف Meta — بيحدد السعر</param>
/// <param name="ArabicLabel">وصف عربي للعرض في الداشبورد</param>
public sealed record IntentSpec(
    string Name,
    IntentClass Class,
    bool Critical,
    MetaCategory MetaCategory,
    string ArabicLabel);

/// <summary>
/// سجل النوايا — المصدر الوحيد للحقيقة. مطابق لـ INTENT_SPEC في docs/09 §4.1.
/// </summary>
public static class IntentRegistry
{
    private static readonly Dictionary<string, IntentSpec> Specs = new(StringComparer.Ordinal)
    {
        // ── تسويق: أعلى تكلفة، أعلى خطر، ومفيش fallback ──
        [IntentNames.CampaignPromo] =
            new(IntentNames.CampaignPromo, IntentClass.Marketing, false, MetaCategory.Marketing, "حملة ترويجية"),
        [IntentNames.Winback] =
            new(IntentNames.Winback, IntentClass.Marketing, false, MetaCategory.Marketing, "استرجاع عميل نايم"),
        [IntentNames.AbandonedCart] =
            new(IntentNames.AbandonedCart, IntentClass.Marketing, false, MetaCategory.Marketing, "سلة متروكة"),
        [IntentNames.NewArrival] =
            new(IntentNames.NewArrival, IntentClass.Marketing, false, MetaCategory.Marketing, "وصل جديد"),

        // ── معاملات: الموثوقية أهم من التكلفة ──
        [IntentNames.OrderConfirmed] =
            new(IntentNames.OrderConfirmed, IntentClass.Transactional, true, MetaCategory.Utility, "تأكيد أوردر"),
        [IntentNames.OrderShipped] =
            new(IntentNames.OrderShipped, IntentClass.Transactional, true, MetaCategory.Utility, "تحديث شحن"),
        [IntentNames.OrderDelivered] =
            new(IntentNames.OrderDelivered, IntentClass.Transactional, false, MetaCategory.Utility, "تم التوصيل"),
        [IntentNames.OrderCancelled] =
            new(IntentNames.OrderCancelled, IntentClass.Transactional, true, MetaCategory.Utility, "إلغاء أوردر"),
        [IntentNames.PaymentReminder] =
            new(IntentNames.PaymentReminder, IntentClass.Transactional, true, MetaCategory.Utility, "تذكير دفع"),

        // ── محادثة: تكلفة صفر + مرونة كاملة → غير رسمي ──
        [IntentNames.BotReply] =
            new(IntentNames.BotReply, IntentClass.Conversational, false, MetaCategory.Service, "رد البوت"),
        [IntentNames.AgentReply] =
            new(IntentNames.AgentReply, IntentClass.Conversational, false, MetaCategory.Service, "رد موظف"),
        [IntentNames.FaqAnswer] =
            new(IntentNames.FaqAnswer, IntentClass.Conversational, false, MetaCategory.Service, "إجابة سؤال شائع"),
        [IntentNames.CatalogBrowse] =
            new(IntentNames.CatalogBrowse, IntentClass.Conversational, false, MetaCategory.Service, "تصفّح كتالوج"),

        // ── نظام ──
        [IntentNames.OptOutAck] =
            new(IntentNames.OptOutAck, IntentClass.System, true, MetaCategory.Service, "تأكيد إلغاء الاشتراك"),
        [IntentNames.Otp] =
            new(IntentNames.Otp, IntentClass.System, true, MetaCategory.Authentication, "كود تحقق"),
    };

    public static IReadOnlyCollection<IntentSpec> All => Specs.Values;

    public static IntentSpec Get(string name) =>
        Specs.TryGetValue(name, out var s)
            ? s
            : throw new ArgumentException($"نية مجهولة: {name}", nameof(name));

    public static bool TryGet(string name, out IntentSpec? spec) => Specs.TryGetValue(name, out spec);

    public static bool Exists(string name) => Specs.ContainsKey(name);
}
