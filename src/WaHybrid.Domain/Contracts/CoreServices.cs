using System.Security.Cryptography;
using System.Text;
using WaHybrid.Domain.Entities;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Windows;

namespace WaHybrid.Domain.Contracts;

/// <summary>
/// 🔑 مفتاح منع التكرار. docs/09 §2.1.
///
/// في نظام هجين، أخطر خطأ هو إن رسالة تتبعت مرتين من قناتين مختلفتين
/// (الـ Router أعاد المحاولة بعد timeout غامض). المفتاح لازم يكون
/// **حتمي (deterministic)** من محتوى النية — مش عشوائي.
/// </summary>
public static class IdempotencyKeyFactory
{
    public static string Create(long customerId, string intent, long? campaignId, DateOnly dayBucket)
    {
        var raw = $"{customerId}|{intent}|{campaignId?.ToString() ?? "-"}|{dayBucket:yyyy-MM-dd}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }
}

/// <summary>
/// متتبع النوافذ — أهم مكوّن في النظام. docs/09 §3.
/// </summary>
public interface IWindowTracker
{
    /// <summary>ضغط إعلان CTWA أو زرار الصفحة → نافذة 72 ساعة مجانية 🎁</summary>
    Task<DateTimeOffset> OpenFepAsync(long customerId, string phone, string source,
        string? sourceRef, ChannelKind channel, CancellationToken ct = default);

    /// <summary>أي رسالة داخلة من العميل → تفتح/تجدّد نافذة 24 ساعة</summary>
    Task<DateTimeOffset> TouchCswAsync(long customerId, string phone, string? messageId,
        ChannelKind channel, CancellationToken ct = default);

    /// <summary>المسار الحار — بيتنده على كل رسالة، لازم يكون سريع</summary>
    Task<CustomerWindowState> GetStateAsync(string phone, CancellationToken ct = default);

    Task InvalidateAsync(string phone, CancellationToken ct = default);
}

/// <summary>مُوجّه القناة — القرار. docs/09 §4</summary>
public interface IChannelRouter
{
    Task<RouteDecision> RouteAsync(SendIntent intent, CancellationToken ct = default);

    /// <summary>التوجيه + التدهور عند سقوط قناة. docs/09 §4.5</summary>
    Task<RoutingOutcome> ResolveWithFallbackAsync(SendIntent intent, int maxHops = 2,
        CancellationToken ct = default);
}

/// <summary>نقطة الدخول **الوحيدة** للإرسال. docs/10 §4</summary>
public interface IMessageSender
{
    Task<SendOutcome> SendAsync(SendIntent intent, CancellationToken ct = default);
}

/// <summary>نتيجة الإرسال الكاملة (بعد التوجيه + البوابات + السجل)</summary>
public sealed class SendOutcome
{
    public bool Ok { get; init; }
    public long? LogId { get; init; }
    public ChannelKind? Channel { get; init; }
    public SendMode? Mode { get; init; }
    public WindowState WindowState { get; init; }
    public string? RouteReason { get; init; }
    public string? Reason { get; init; }
    public string? ErrorCode { get; init; }
    public string? BlockedByGate { get; init; }
    public decimal EstimatedCostUsd { get; init; }
    public string? ProviderMessageId { get; init; }
    public bool Deduped { get; init; }
    public bool Fatal { get; init; }
    public bool Retryable { get; init; }
    public List<TriedChannel> Tried { get; init; } = new();
}

/// <summary>سجل القوالب. docs/10 §5.1</summary>
public interface ITemplateRegistry
{
    /// <summary>أحدث قالب معتمد وغير موقوف للنية دي</summary>
    Task<WaTemplate?> ForIntentAsync(string intent, string lang = "ar", CancellationToken ct = default);

    Task<WaTemplate?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>بناء payload القالب بترتيب المتغيرات الصحيح</summary>
    TemplatePayload Build(WaTemplate template, IReadOnlyDictionary<string, string> parameters);
}

/// <summary>متتبع الحد اليومي (Messaging Tier). docs/10 §3</summary>
public interface ITierStore
{
    Task<TierSnapshot> CurrentAsync(CancellationToken ct = default);
    Task<int> IncrementAsync(int n = 1, CancellationToken ct = default);
}

public sealed record TierSnapshot(
    string Tier,
    int Limit,
    int UsedToday,
    QualityRating Quality,
    bool MarketingPaused)
{
    public double Headroom => Limit == 0 ? 0 : Math.Max(0, 1.0 - (double)UsedToday / Limit);
    public double UsedPct => Limit == 0 ? 1 : (double)UsedToday / Limit;
}

/// <summary>سقف تكرار التسويق لكل عميل. docs/09 §5</summary>
public interface IFrequencyCap
{
    /// <summary>سقفنا الموحّد — بيحسب القناتين مع بعض (حماية السمعة)</summary>
    Task<int> GetGlobalMarketingCountAsync(string phone, CancellationToken ct = default);

    /// <summary>سقف Meta المتوقع — 131049، رسمي بس</summary>
    Task<int> GetMetaMarketingCountAsync(string phone, CancellationToken ct = default);

    Task RecordAsync(string phone, ChannelKind channel, CancellationToken ct = default);
}

/// <summary>💰 حزام الأمان المالي. docs/10 §4</summary>
public interface ICostGuard
{
    Task<BudgetSnapshot> CheckAsync(CancellationToken ct = default);
}

public sealed record BudgetSnapshot(
    decimal SpentToday,
    decimal SpentMonth,
    decimal DailyLimit,
    decimal MonthlyLimit,
    double Pct,
    bool HardStop,
    bool Alert);

/// <summary>جدول الأسعار — لازم تتحقق منه بنفسك من كارت Meta (docs/08 §4.2)</summary>
public interface ICostBook
{
    decimal Price(string phone, MetaCategory category);
    decimal BspFee { get; }
}

/// <summary>🔴 مفتاح الطوارئ — بيوقف القناة غير الرسمية أو الكل</summary>
public interface IKillSwitch
{
    Task<bool> IsUnofficialKilledAsync(CancellationToken ct = default);
    Task<bool> IsGlobalKilledAsync(CancellationToken ct = default);
    Task SetUnofficialAsync(bool killed, string? reason, CancellationToken ct = default);
    Task SetGlobalAsync(bool killed, string? reason, CancellationToken ct = default);
}

/// <summary>سلسلة البوابات</summary>
public interface IGateChain
{
    Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default);

    /// <summary>تشخيص: نتيجة كل بوابة على حدة — للداشبورد</summary>
    Task<IReadOnlyList<GateTrace>> TraceAsync(GateContext ctx, CancellationToken ct = default);
}

public sealed record GateTrace(string Gate, int Order, bool Passed, string? Reason);

/// <summary>مخزن مؤقت خفيف (Redis في الإنتاج، ذاكرة في التطوير)</summary>
public interface ICacheStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>SET NX — بيرجع true لو المفتاح كان مش موجود (الأساس للـ idempotency)</summary>
    Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task<long> IncrementAsync(string key, TimeSpan ttlOnFirst, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}

/// <summary>التنبيهات (تليجرام في الإنتاج)</summary>
public interface IAlerter
{
    Task SendAsync(string severity, string message, CancellationToken ct = default);
    IReadOnlyList<AlertRecord> Recent(int take = 50);
}

public sealed record AlertRecord(DateTimeOffset At, string Severity, string Message);
