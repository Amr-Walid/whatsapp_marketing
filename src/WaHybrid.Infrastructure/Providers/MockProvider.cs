using Microsoft.Extensions.Logging;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;

namespace WaHybrid.Infrastructure.Providers;

/// <summary>
/// مزوّد وهمي. docs/10 §8.1.
///
/// 🔑 ليه ده أهم حاجة في المشروع دلوقتي؟
/// لأنه بيخلّينا نختبر **كل** منطق النظام (النوافذ، التوجيه، البوابات،
/// منع التكرار، التكلفة، التدهور) بدون:
///   • حساب Meta معتمد
///   • أرقام شرائح حقيقية
///   • ولا مليم تكلفة
///   • ولا أي خطر حظر
///
/// وبيسجّل كل رسالة عشان نراجع القرارات. ده اللي يخلّينا نقدر نقول للمدير
/// "النظام شغّال ومختبر" قبل ما نصرف جنيه واحد.
/// </summary>
public sealed class MockProvider : IMessageProvider
{
    private readonly List<MockSentMessage> _sent = new();
    private readonly object _lock = new();
    private readonly Random _rng;
    private readonly ILogger _log;

    public ChannelKind Channel { get; }

    /// <summary>نسبة الفشل الصناعي 0..1 — لاختبار مسارات التدهور</summary>
    public double FailRate { get; set; }

    /// <summary>هل المزوّد "واقع"؟ — لاختبار fallback يدوياً</summary>
    public bool ForceDown { get; set; }

    /// <summary>هل المزوّد "متدهور"؟ (شغّال بس الـ Router يتجنّبه)</summary>
    public bool ForceDegraded { get; set; }

    public MockProvider(ChannelKind channel, ILogger log, double failRate = 0, int seed = 20250831)
    {
        Channel = channel;
        _log = log;
        FailRate = failRate;
        _rng = new Random(seed);   // 🔑 seed ثابت = اختبارات قابلة للتكرار
    }

    public Task<CanSendResult> CanAsync(SendRequest request, CancellationToken ct = default)
    {
        if (ForceDown)
            return Task.FromResult(CanSendResult.Deny("المزوّد الوهمي مطفي عمداً", "mock_down"));

        // نحاكي قاعدة الرسمي: رسالة حرة بره النافذة = ممنوعة (131047)
        if (Channel == ChannelKind.Official && request.Template is null && !request.Meta.WindowOpen)
            return Task.FromResult(CanSendResult.Deny(
                "رسالة حرة بره النافذة — الرسمي بيرفض", "131047"));

        return Task.FromResult(CanSendResult.Allow(
            sessionId: Channel == ChannelKind.Unofficial ? "mock-session-01" : null,
            contactState: "known"));
    }

    public Task<SendResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        if (ForceDown)
            return Task.FromResult(SendResult.Fail(Channel, "mock_down",
                "المزوّد الوهمي مطفي", retryable: true, retryAfterMs: 30_000));

        // فشل صناعي عشوائي (بـ seed ثابت)
        double roll;
        lock (_lock) roll = _rng.NextDouble();

        if (roll < FailRate)
        {
            var rule = MetaErrorMap.Resolve("130429");
            return Task.FromResult(SendResult.Fail(Channel, rule.Code, rule.ArabicMeaning,
                rule.Retryable, rule.RetryAfterMs, rule.Fatal));
        }

        var id = $"mock.{Channel.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}"[..40];

        // 💰 تكلفة تقديرية: الرسمي بيتكلّف، غير الرسمي مجاني (بس فيه خطر)
        var cost = Channel == ChannelKind.Official
            ? EstimateOfficialCost(request)
            : 0m;

        var record = new MockSentMessage(
            DateTimeOffset.UtcNow, Channel, request.To, request.Meta.IntentName,
            request.Template?.Name, request.Body, request.IdempotencyKey,
            request.Meta.WindowState, cost, id);

        lock (_lock)
        {
            _sent.Add(record);
            if (_sent.Count > 5000) _sent.RemoveRange(0, 1000);
        }

        _log.LogInformation(
            "📤 [MOCK/{Channel}] → {To} | نية={Intent} | نافذة={Window} | {Mode} | ${Cost}",
            Channel, request.To, request.Meta.IntentName, request.Meta.WindowState,
            request.Template is null ? "حر" : $"قالب:{request.Template.Name}", cost);

        return Task.FromResult(SendResult.Success(Channel, id, cost,
            sessionId: Channel == ChannelKind.Unofficial ? "mock-session-01" : null,
            delayMs: Channel == ChannelKind.Unofficial ? 0 : null));
    }

    private static decimal EstimateOfficialCost(SendRequest r)
    {
        // داخل النافذة → مجاني (النهاردة). بره → سعر القالب حسب التصنيف.
        if (r.Meta.WindowState is WindowState.FepOpen) return 0m;
        if (r.Template is null) return 0m;

        return r.Template.Category switch
        {
            MetaCategory.Marketing => 0.0350m,
            MetaCategory.Utility => 0.0100m,
            MetaCategory.Authentication => 0.0110m,
            _ => 0m
        };
    }

    public Task<ProviderHealth> HealthAsync(CancellationToken ct = default)
        => Task.FromResult(new ProviderHealth
        {
            Up = !ForceDown,
            Degraded = ForceDegraded,
            Headroom = ForceDown ? 0 : 0.85,
            Quality = QualityRating.Green,
            Note = "مزوّد وهمي (mock) — مفيش إرسال حقيقي"
        });

    // ── للتشخيص والاختبار والداشبورد ──
    public IReadOnlyList<MockSentMessage> Sent
    {
        get { lock (_lock) return _sent.ToList(); }
    }

    public void Reset()
    {
        lock (_lock) { _sent.Clear(); ForceDown = false; ForceDegraded = false; }
    }
}

public sealed record MockSentMessage(
    DateTimeOffset At,
    ChannelKind Channel,
    string To,
    string Intent,
    string? TemplateName,
    string? Body,
    string IdempotencyKey,
    WindowState WindowState,
    decimal Cost,
    string ProviderMessageId);
