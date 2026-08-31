using WaHybrid.Domain.Enums;

namespace WaHybrid.Domain.Contracts;

/// <summary>
/// قرار الـ ChannelRouter. docs/09 §4.
/// <c>RouteReason</c> بيتسجّل في <c>message_log</c> — بدونه مش هتعرف تدبّج القرارات.
/// </summary>
public sealed class RouteDecision
{
    /// <summary>null = مرفوض (مفيش قناة صالحة)</summary>
    public ChannelKind? Channel { get; init; }

    public SendMode? Mode { get; init; }

    /// <summary>ليه اخترنا كده — إجباري للتدقيق</summary>
    public required string Reason { get; init; }

    /// <summary>لو دي محاولة تانية بعد سقوط قناة</summary>
    public ChannelKind? FallbackFrom { get; init; }

    public bool Allowed => Channel is not null;

    public static RouteDecision Pick(ChannelKind ch, SendMode mode, string reason,
        ChannelKind? fallbackFrom = null) =>
        new() { Channel = ch, Mode = mode, Reason = reason, FallbackFrom = fallbackFrom };

    public static RouteDecision Deny(string reason) => new() { Reason = reason };
}

/// <summary>نتيجة التوجيه بعد محاولات التدهور (docs/09 §4.5)</summary>
public sealed class RoutingOutcome
{
    public bool Ok { get; init; }
    public RouteDecision? Decision { get; init; }
    public string? Reason { get; init; }

    /// <summary>القنوات اللي جرّبناها وفشلت — للتشخيص</summary>
    public List<TriedChannel> Tried { get; init; } = new();

    public static RoutingOutcome Success(RouteDecision d, List<TriedChannel> tried) =>
        new() { Ok = true, Decision = d, Tried = tried };

    public static RoutingOutcome Failure(string reason, List<TriedChannel> tried) =>
        new() { Ok = false, Reason = reason, Tried = tried };
}

public sealed record TriedChannel(ChannelKind Channel, string Why);

/// <summary>نتيجة تنفيذ سلسلة البوابات (GateChain)</summary>
public sealed class GateVerdict
{
    public bool Passed { get; init; }
    public string? Gate { get; init; }
    public string? Reason { get; init; }

    /// <summary>اسقط الرسالة نهائياً — متعيدش المحاولة</summary>
    public bool Drop { get; init; }

    public DateTimeOffset? RetryAt { get; init; }

    /// <summary>البوابة بتقترح تغيير الوضع (مثال: حر → قالب)</summary>
    public SendMode? SwitchTo { get; init; }

    public static GateVerdict Pass() => new() { Passed = true };

    public static GateVerdict Block(string gate, string reason, bool drop = false,
        DateTimeOffset? retryAt = null, SendMode? switchTo = null) =>
        new() { Passed = false, Gate = gate, Reason = reason, Drop = drop, RetryAt = retryAt, SwitchTo = switchTo };
}

/// <summary>السياق اللي البوابات بتشوفه</summary>
public sealed class GateContext
{
    public required string Phone { get; init; }
    public required long CustomerId { get; init; }
    public required string IntentName { get; init; }
    public required string IdempotencyKey { get; init; }
    public ChannelKind? Channel { get; init; }
    public SendMode? Mode { get; init; }
    public TemplatePayload? Template { get; init; }
    public long? CampaignId { get; init; }
    public string? Segment { get; init; }
}

/// <summary>بوابة واحدة في السلسلة</summary>
public interface IGate
{
    /// <summary>اسم البوابة — بيظهر في السجل والتشخيص</summary>
    string Name { get; }

    /// <summary>ترتيب التنفيذ — الأرخص والأقطع أولاً (docs/09 §5)</summary>
    int Order { get; }

    Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default);
}
