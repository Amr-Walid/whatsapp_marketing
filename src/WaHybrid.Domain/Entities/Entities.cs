using WaHybrid.Domain.Enums;

namespace WaHybrid.Domain.Entities;

/// <summary>
/// العميل — كائن واحد لكل شخص، مشترك بين القناتين.
/// 🔑 من docs/09 §0: <c>OptedOut</c> مرة واحدة يعني على القناتين.
/// </summary>
public class Customer
{
    public long Id { get; set; }

    /// <summary>E.164 بدون + — منظّف من ملف 01</summary>
    public string Phone { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>القطاع من تحليل RFM (ملف 01)</summary>
    public string? Segment { get; set; }

    // ── الموافقة (القانونية) ──
    public bool OptedIn { get; set; }
    public string? OptInSource { get; set; }
    public DateTimeOffset? OptedInAt { get; set; }

    /// <summary>🔴 لو true — ممنوع أي تسويق على أي قناة</summary>
    public bool OptedOut { get; set; }
    public DateTimeOffset? OptedOutAt { get; set; }

    // ── الهجين ──
    /// <summary>null = خلّي الـ Router يقرر</summary>
    public ChannelKind? PreferredChannel { get; set; }
    public bool OfficialOptIn { get; set; }
    public DateTimeOffset? OfficialOptInAt { get; set; }

    /// <summary>🔑 معرّف ضغطة إعلان CTWA — للربط بـ Ads Manager</summary>
    public string? CtwaClid { get; set; }
    public AcquisitionSource AcquisitionSource { get; set; } = AcquisitionSource.Import;
    public ChannelKind? LastChannelUsed { get; set; }

    // ── RFM ──
    public decimal Monetary { get; set; }
    public int Frequency { get; set; }
    public int RecencyDays { get; set; }
    public int Priority { get; set; } = 100;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<CustomerWindow> Windows { get; set; } = new();
}

/// <summary>
/// نافذة واحدة لكل (عميل، نوع). الأحدث هي الفعّالة.
/// docs/09 §3.3 — UNIQUE(customer_id, kind) وبنحدّثها مش بنضيف صف جديد.
/// </summary>
public class CustomerWindow
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string Phone { get; set; } = string.Empty;
    public WindowKind Kind { get; set; }

    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>ctwa_ad | page_cta | inbound_message | inbound_reply</summary>
    public string OpenedBy { get; set; } = string.Empty;

    /// <summary>ad_id / campaign_id / wa_message_id</summary>
    public string? SourceRef { get; set; }

    /// <summary>على أنهي قناة شفنا الحدث</summary>
    public ChannelKind? ChannelSeen { get; set; }

    public int RenewCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// سجل موحّد للقناتين. docs/09 §6.
/// كل إرسال بيتسجّل هنا **قبل** الإرسال الفعلي — عشان متضيّعش حاجة لو النظام وقع.
/// </summary>
public class MessageLog
{
    public long Id { get; set; }

    public long? CampaignId { get; set; }
    public long CustomerId { get; set; }
    public string Phone { get; set; } = string.Empty;

    public MessageDirection Direction { get; set; }

    /// <summary>🔑 القناة — الأرقام القديمة كلها unofficial</summary>
    public ChannelKind Channel { get; set; }

    public string Intent { get; set; } = string.Empty;

    /// <summary>حالة النافذة **لحظة الإرسال** — مش وقت الاستعلام</summary>
    public WindowState WindowState { get; set; }

    public SendMode SendMode { get; set; }
    public string? TemplateName { get; set; }
    public MetaCategory MetaCategory { get; set; }

    /// <summary>🔒 حزام أمان تاني ضد الإرسال المزدوج — UNIQUE index</summary>
    public string? IdempotencyKey { get; set; }

    public string? Content { get; set; }

    /// <summary>⚠️ تقديري — بيتحسب وقت الإرسال</summary>
    public decimal CostEstimated { get; set; }

    /// <summary>💰 الحقيقي — بيتملى من webhook التسليم مش الإرسال</summary>
    public decimal? CostBilled { get; set; }

    /// <summary>ليه الـ Router اختار كده — للتدقيق</summary>
    public string? RouteReason { get; set; }

    /// <summary>لو دي محاولة تانية بعد سقوط قناة</summary>
    public ChannelKind? FallbackFrom { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.Sending;
    public string? WaMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>الجلسة (غير رسمي بس)</summary>
    public string? SessionId { get; set; }

    /// <summary>التأخير البشري المستخدم (غير رسمي بس)</summary>
    public int? DelayUsedMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>التكلفة الفعلية لو موجودة، وإلا التقديرية</summary>
    public decimal EffectiveCost => CostBilled ?? CostEstimated;
}

/// <summary>
/// دفتر القوالب الرسمية. docs/09 §6 (5).
/// الجسر بين نوايانا الداخلية وقوالب Meta — كده لو قالب اترفض،
/// تعمل نسخة جديدة وتربطها بنفس النية **بدون تعديل كود**.
/// </summary>
public class WaTemplate
{
    public long Id { get; set; }

    /// <summary>اسم القالب عند Meta</summary>
    public string Name { get; set; } = string.Empty;

    public string Language { get; set; } = "ar";
    public MetaCategory Category { get; set; }
    public TemplateStatus Status { get; set; } = TemplateStatus.Pending;
    public QualityRating? Quality { get; set; }

    /// <summary>🔴 قالب بقى أحمر → بيتوقف 24 ساعة أوتوماتيك</summary>
    public DateTimeOffset? PausedUntil { get; set; }

    /// <summary>النص بمتغيرات {{1}} {{2}}</summary>
    public string BodyText { get; set; } = string.Empty;

    public string? HeaderKind { get; set; }
    public string? FooterText { get; set; }

    /// <summary>JSON array — ["name","order_id"] — الترتيب مهم! {{1}} = أول عنصر</summary>
    public string RequiredParamsJson { get; set; } = "[]";

    /// <summary>🔑 الربط بنياتنا الداخلية</summary>
    public string? Intent { get; set; }

    public string? MetaId { get; set; }
    public string? RejectedReason { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsUsable(DateTimeOffset now) =>
        Status == TemplateStatus.Approved
        && (PausedUntil is null || PausedUntil <= now)
        && Quality != QualityRating.Red;
}

/// <summary>
/// دفتر التكاليف — عشان تعرف بتدفع كام فعلاً. docs/09 §6 (6).
/// 🔑 الفاتورة على التسليم (delivered) مش الإرسال (sent).
/// </summary>
public class CostLedgerEntry
{
    public long Id { get; set; }
    public DateOnly Day { get; set; }
    public ChannelKind Channel { get; set; }
    public MetaCategory MetaCategory { get; set; }
    public string CountryCode { get; set; } = "EG";

    public int MsgCount { get; set; }
    public int Delivered { get; set; }
    public decimal CostUsd { get; set; }
    public decimal BspFeeUsd { get; set; }
}

/// <summary>
/// حالة الحساب الرسمي (tier + جودة). docs/09 §6 (7).
/// صف واحد بس (Id = 1).
/// </summary>
public class OfficialStatus
{
    public short Id { get; set; } = 1;
    public string? PhoneNumberId { get; set; }

    /// <summary>TIER_250 | TIER_1K | TIER_10K | TIER_100K | TIER_UNLIMITED</summary>
    public string Tier { get; set; } = "TIER_250";

    public int DailyLimit { get; set; } = 250;
    public int UsedToday { get; set; }
    public QualityRating QualityRating { get; set; } = QualityRating.Unknown;

    /// <summary>⚠️ Meta بتصفّر الحد على UTC — مش التوقيت المحلي</summary>
    public DateTimeOffset? ResetAt { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>marketing_paused_red = التسويق موقوف بسبب الجودة الحمراء</summary>
    public string? Notes { get; set; }

    public bool MarketingPaused => Notes == "marketing_paused_red";
}

/// <summary>الحملة</summary>
public class Campaign
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Segment { get; set; }
    public string IntentName { get; set; } = string.Empty;
    public string? BodyTemplate { get; set; }
    public string Status { get; set; } = "draft";
    public decimal EstimatedCostUsd { get; set; }
    public int PlannedOfficial { get; set; }
    public int PlannedUnofficial { get; set; }
    public int PlannedSkipped { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>جلسة غير رسمية (Evolution instance) — من ملف 04</summary>
public class WaSession
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string? Phone { get; set; }

    /// <summary>warming | active | paused | banned | disconnected</summary>
    public string Status { get; set; } = "warming";

    public int WarmupDay { get; set; } = 1;
    public int DailyQuota { get; set; }
    public int SentToday { get; set; }

    /// <summary>0..100 — من RiskScorer في ملف 03</summary>
    public int RiskScore { get; set; }

    public string? ProxyLabel { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsHealthy => Status is "active" or "warming" && RiskScore < 70;
}

/// <summary>قائمة الحظر (Suppression) — أعلى أسبقية على أي قناة</summary>
public class SuppressionEntry
{
    public long Id { get; set; }
    public string Phone { get; set; } = string.Empty;

    /// <summary>opt_out | complaint | invalid | manual | bounced</summary>
    public string Reason { get; set; } = "manual";

    public ChannelKind? SeenOnChannel { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
