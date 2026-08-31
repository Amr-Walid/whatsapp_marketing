using WaHybrid.Domain.Enums;

namespace WaHybrid.Domain.Contracts;

/// <summary>
/// النية الداخلية اللي بتوصل لـ <c>IMessageSender.SendAsync</c>.
/// ده الشكل اللي بقية النظام (البوت، الحملة، الأوردرات) بيتكلم بيه —
/// 🔑 وملهوش أي علاقة بالقناة. القاعدة الحديدية من docs/09 §0.
/// </summary>
public sealed class SendIntent
{
    /// <summary>اسم النية من <c>IntentNames</c></summary>
    public required string Name { get; init; }

    public required long CustomerId { get; init; }

    /// <summary>رقم E.164 بدون + (مثال: 201012345678)</summary>
    public required string Phone { get; init; }

    /// <summary>نص الرسالة الحرة (لو الوضع Free)</summary>
    public string? Body { get; init; }

    public string? MediaUrl { get; init; }

    /// <summary>text | image | video | document | audio</summary>
    public string Type { get; init; } = "text";

    public long? CampaignId { get; init; }

    public string? Segment { get; init; }

    /// <summary>متغيرات القالب — بتُملأ لو الـ Router قرر Template</summary>
    public Dictionary<string, string> TemplateParams { get; init; } = new();
}

/// <summary>
/// الطلب اللي بيوصل لطبقة المزوّد. مطابق لـ SendRequest في docs/09 §2.1.
/// </summary>
public sealed class SendRequest
{
    public required string To { get; init; }
    public string Type { get; init; } = "text";
    public string? Body { get; init; }
    public string? MediaUrl { get; init; }

    /// <summary>payload القالب — للرسمي بس</summary>
    public TemplatePayload? Template { get; init; }

    /// <summary>🔑 إجباري — يمنع الإرسال المزدوج بين القناتين</summary>
    public required string IdempotencyKey { get; init; }

    public required SendRequestMeta Meta { get; init; }
}

public sealed class SendRequestMeta
{
    public required long CustomerId { get; init; }
    public required string IntentName { get; init; }
    public long? CampaignId { get; init; }
    public string? Segment { get; init; }

    /// <summary>هل فيه نافذة مفتوحة؟ الرسمي بيرفض الرسالة الحرة بدونها (131047)</summary>
    public bool WindowOpen { get; init; }

    public WindowState WindowState { get; init; }
}

/// <summary>payload القالب المعتمد</summary>
public sealed class TemplatePayload
{
    public required string Name { get; init; }
    public string Language { get; init; } = "ar";
    public MetaCategory Category { get; init; }

    /// <summary>القيم بالترتيب — {{1}} = أول عنصر</summary>
    public List<string> Parameters { get; init; } = new();
}

/// <summary>
/// نتيجة الإرسال. مطابق لـ SendResult في docs/09 §2.1.
/// </summary>
public sealed class SendResult
{
    public bool Ok { get; init; }
    public ChannelKind Channel { get; init; }
    public string? ProviderMessageId { get; init; }

    /// <summary>⚠️ تقديري — الفاتورة الحقيقية بتتأكد على التسليم في الـ webhook</summary>
    public decimal EstimatedCostUsd { get; init; }

    public string? ErrorCode { get; init; }
    public string? Reason { get; init; }

    /// <summary>ينفع نعيد المحاولة؟</summary>
    public bool Retryable { get; init; }

    public int? RetryAfterMs { get; init; }

    /// <summary>🔴 فشل نهائي — متبعتش تاني لنفس العميل</summary>
    public bool Fatal { get; init; }

    /// <summary>اتبعتت قبل كده — الـ idempotency قطعها</summary>
    public bool Deduped { get; init; }

    /// <summary>الجلسة اللي بعتت (غير رسمي بس)</summary>
    public string? SessionId { get; init; }

    /// <summary>التأخير البشري اللي استخدمناه (غير رسمي بس)</summary>
    public int? DelayUsedMs { get; init; }

    public static SendResult Success(ChannelKind ch, string providerMessageId, decimal cost = 0,
        string? sessionId = null, int? delayMs = null) => new()
    {
        Ok = true, Channel = ch, ProviderMessageId = providerMessageId,
        EstimatedCostUsd = cost, SessionId = sessionId, DelayUsedMs = delayMs
    };

    public static SendResult Fail(ChannelKind ch, string errorCode, string reason,
        bool retryable = false, int? retryAfterMs = null, bool fatal = false) => new()
    {
        Ok = false, Channel = ch, ErrorCode = errorCode, Reason = reason,
        Retryable = retryable, RetryAfterMs = retryAfterMs, Fatal = fatal
    };

    public static SendResult Dedup(ChannelKind ch) => new() { Ok = true, Channel = ch, Deduped = true };
}

/// <summary>هل المزوّد ده يقدر ينفّذ الطلب دلوقتي؟ (بدون إرسال)</summary>
public sealed class CanSendResult
{
    public bool Ok { get; init; }
    public string? Reason { get; init; }
    public string? Code { get; init; }

    /// <summary>الجلسة اللي هتُستخدم (غير رسمي)</summary>
    public string? SessionId { get; init; }

    /// <summary>حالة جهة الاتصال — بتغذّي DelayEngine</summary>
    public string? ContactState { get; init; }

    /// <summary>البوابة اللي رفضت</summary>
    public string? Gate { get; init; }

    /// <summary>اسقط الرسالة خلاص — متعيدش</summary>
    public bool Drop { get; init; }

    public DateTimeOffset? RetryAt { get; init; }

    public static CanSendResult Allow(string? sessionId = null, string? contactState = null) =>
        new() { Ok = true, SessionId = sessionId, ContactState = contactState };

    public static CanSendResult Deny(string reason, string? code = null, bool drop = false,
        DateTimeOffset? retryAt = null, string? gate = null) =>
        new() { Ok = false, Reason = reason, Code = code, Drop = drop, RetryAt = retryAt, Gate = gate };
}

/// <summary>صحة المزوّد — الـ Router بيستخدمها للتدهور (degradation)</summary>
public sealed class ProviderHealth
{
    public bool Up { get; init; }

    /// <summary>المساحة المتبقية 0..1</summary>
    public double Headroom { get; init; }

    /// <summary>شغّال بس مش بكفاءة — الـ Router بيحوّل</summary>
    public bool Degraded { get; init; }

    public QualityRating Quality { get; init; } = QualityRating.Unknown;

    public string? Note { get; init; }
}
