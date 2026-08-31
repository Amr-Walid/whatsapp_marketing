namespace WaHybrid.Domain.Enums;

/// <summary>القناة — رسمي (Cloud API) أو غير رسمي (Evolution/Baileys)</summary>
public enum ChannelKind
{
    Official = 1,
    Unofficial = 2
}

/// <summary>وضع الإرسال — قالب معتمد أو رسالة حرة</summary>
public enum SendMode
{
    /// <summary>رسالة حرة (non-template) — محتاجة نافذة مفتوحة</summary>
    Free = 1,

    /// <summary>قالب معتمد من Meta — الوحيد اللي ينفع بره النافذة</summary>
    Template = 2
}

/// <summary>حالة النافذة لحظة القرار — ده اللي بيحدد التكلفة والقناة</summary>
public enum WindowState
{
    /// <summary>🎁 نافذة الدخول المجاني 72 ساعة (CTWA/Page CTA) — كل حاجة مجاناً</summary>
    FepOpen = 1,

    /// <summary>🟡 نافذة خدمة العميل 24 ساعة — العميل كلّمنا</summary>
    CswOpen = 2,

    /// <summary>🔴 مفيش نافذة — قوالب معتمدة بس</summary>
    NoWindow = 3
}

/// <summary>نوع النافذة في قاعدة البيانات</summary>
public enum WindowKind
{
    Fep = 1,
    Csw = 2
}

/// <summary>تصنيف النية داخلياً</summary>
public enum IntentClass
{
    /// <summary>تسويق — بيبدأ من عندنا، أعلى خطر وأعلى تكلفة</summary>
    Marketing = 1,

    /// <summary>معاملات — نتيجة فعل عمله العميل</summary>
    Transactional = 2,

    /// <summary>محادثة — رد على العميل</summary>
    Conversational = 3,

    /// <summary>نظام — opt-out، OTP</summary>
    System = 4
}

/// <summary>تصنيف Meta الرسمي للقالب — Meta هي اللي بتعتمده مش إحنا</summary>
public enum MetaCategory
{
    Marketing = 1,
    Utility = 2,
    Authentication = 3,
    Service = 4
}

/// <summary>حالة الرسالة في السجل</summary>
public enum MessageStatus
{
    Sending = 1,
    Sent = 2,
    Delivered = 3,
    Read = 4,
    Failed = 5,
    Blocked = 6,
    Skipped = 7
}

/// <summary>اتجاه الرسالة</summary>
public enum MessageDirection
{
    Out = 1,
    In = 2
}

/// <summary>حالة القالب عند Meta</summary>
public enum TemplateStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Paused = 4,
    Disabled = 5
}

/// <summary>تقييم الجودة — نفس السلم في الرسمي وغير الرسمي</summary>
public enum QualityRating
{
    Green = 1,
    Yellow = 2,
    Red = 3,
    Unknown = 4
}

/// <summary>مصدر اكتساب العميل</summary>
public enum AcquisitionSource
{
    Import = 1,
    Organic = 2,
    Ctwa = 3,
    Qr = 4
}
