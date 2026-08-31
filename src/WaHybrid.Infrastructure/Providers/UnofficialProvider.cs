using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Infrastructure.Data;
using WaHybrid.Infrastructure.Options;

namespace WaHybrid.Infrastructure.Providers;

/// <summary>
/// المزوّد غير الرسمي — Evolution API / Baileys. docs/09 §2.3 + docs/06.
///
/// خصائصه:
///   ✅ تكلفة صفر لكل رسالة
///   ✅ مفيش قوالب ولا اعتماد — حرية كاملة في النص
///   ✅ مفيش سقف تكرار من Meta
///   ❌ 🔴 خطر حظر دائم — ده **مش** خطر نظري
///   ❌ مش قانوني حسب شروط واتساب
///   ❌ محتاج warmup وتأخير بشري وجلسات متعددة
///
/// 🔑 القاعدة الذهبية (docs/08 §7): القناة دي **للمحادثات بس** —
/// العميل كلّمنا وإحنا بنرد. أبداً أبداً للتسويق البارد.
/// المخالفة = حظر مضمون + خسارة قاعدة عملاء.
/// </summary>
public sealed class UnofficialProvider : IMessageProvider
{
    private readonly HttpClient _http;
    private readonly UnofficialOptions _opt;
    private readonly HybridDbContext _db;
    private readonly IKillSwitch _kill;
    private readonly DelayEngine _delay;
    private readonly ILogger<UnofficialProvider> _log;

    public ChannelKind Channel => ChannelKind.Unofficial;

    public UnofficialProvider(HttpClient http, IOptions<HybridOptions> opt, HybridDbContext db,
        IKillSwitch kill, DelayEngine delay, ILogger<UnofficialProvider> log)
    {
        _http = http;
        _opt = opt.Value.Unofficial;
        _db = db;
        _kill = kill;
        _delay = delay;
        _log = log;

        if (!string.IsNullOrWhiteSpace(_opt.EvolutionApiKey))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", _opt.EvolutionApiKey);
    }

    // ══════════════════════════════════════════════════════════════════
    //  CanAsync
    // ══════════════════════════════════════════════════════════════════
    public async Task<CanSendResult> CanAsync(SendRequest request, CancellationToken ct = default)
    {
        // 1️⃣ مفتاح الطوارئ — أعلى أسبقية على أي حاجة
        if (await _kill.IsUnofficialKilledAsync(ct))
            return CanSendResult.Deny("🔴 القناة غير الرسمية موقوفة بمفتاح الطوارئ",
                "kill_switch", gate: "gKillSwitch");

        if (string.IsNullOrWhiteSpace(_opt.EvolutionBaseUrl))
            return CanSendResult.Deny("إعدادات Evolution ناقصة", "not_configured");

        // 2️⃣ القوالب مش مدعومة هنا — لو الـ Router بعت قالب فده bug
        if (request.Template is not null)
            return CanSendResult.Deny(
                "القناة غير الرسمية مش بتدعم القوالب المعتمدة — قرار توجيه غلط",
                "template_unsupported", drop: true, gate: "gRouterSanity");

        // 3️⃣ اختيار جلسة صحّية عندها حصة فاضلة
        var session = await PickSessionAsync(ct);
        if (session is null)
            return CanSendResult.Deny(
                "مفيش جلسة صحّية عندها حصة فاضلة النهاردة",
                "no_session", retryAt: DateTimeOffset.UtcNow.AddHours(1), gate: "gSessionPool");

        return CanSendResult.Allow(session.SessionId, contactState: "known");
    }

    /// <summary>
    /// اختيار الجلسة: أقل استهلاك أولاً (توزيع عادل) من الجلسات الصحّية فقط.
    /// الحصة اليومية بتيجي من جدول الـ warmup في docs/03.
    /// </summary>
    private async Task<Domain.Entities.WaSession?> PickSessionAsync(CancellationToken ct)
    {
        var candidates = await _db.WaSessions
            .Where(s => (s.Status == "active" || s.Status == "warming")
                        && s.RiskScore < 70
                        && s.SentToday < s.DailyQuota)
            .OrderBy(s => s.SentToday)
            .ToListAsync(ct);

        return candidates.FirstOrDefault();
    }

    // ══════════════════════════════════════════════════════════════════
    //  SendAsync
    // ══════════════════════════════════════════════════════════════════
    public async Task<SendResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        var can = await CanAsync(request, ct);
        if (!can.Ok)
            return SendResult.Fail(Channel, can.Code ?? "denied", can.Reason ?? "مرفوض");

        var sessionId = can.SessionId!;

        // ⏱️ التأخير البشري — أهم حماية ضد الحظر (docs/03)
        // في التطوير بنتخطّاه، وإلا كل اختبار هيستغرق ٤٥ ثانية.
        var delayMs = _delay.Next();
        if (!_opt.SkipDelayInDev && delayMs > 0)
            await Task.Delay(delayMs, ct);

        var url = $"{_opt.EvolutionBaseUrl!.TrimEnd('/')}/message/sendText/{sessionId}";
        var payload = new
        {
            number = request.To,
            text = request.Body ?? "",
            delay = Math.Min(delayMs, 20_000)   // Evolution بيدعم تأخير "بيكتب..."
        };

        try
        {
            using var res = await _http.PostAsJsonAsync(url, payload, ct);
            var text = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("❌ Evolution رفض ({Status}): {Body}", (int)res.StatusCode, text);

                // 🔴 401/403 = الجلسة اتقطعت أو اتحظرت — حالة خطيرة
                var disconnected = res.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                                  or System.Net.HttpStatusCode.Forbidden
                                                  or System.Net.HttpStatusCode.NotFound;
                if (disconnected)
                    await MarkSessionAsync(sessionId, "disconnected", ct);

                return SendResult.Fail(Channel, $"evolution_{(int)res.StatusCode}",
                    disconnected ? "الجلسة اتقطعت أو اتحظرت" : "Evolution رفض الطلب",
                    retryable: !disconnected, retryAfterMs: disconnected ? null : 30_000);
            }

            await BumpSessionCounterAsync(sessionId, ct);
            var id = ExtractKeyId(text) ?? $"evo.{Guid.NewGuid():N}"[..32];

            return SendResult.Success(Channel, id, cost: 0m, sessionId: sessionId, delayMs: delayMs);
        }
        catch (TaskCanceledException)
        {
            return SendResult.Fail(Channel, "timeout", "مهلة Evolution انتهت",
                retryable: true, retryAfterMs: 20_000);
        }
        catch (HttpRequestException ex)
        {
            return SendResult.Fail(Channel, "network", $"فشل الشبكة: {ex.Message}",
                retryable: true, retryAfterMs: 15_000);
        }
    }

    private static string? ExtractKeyId(string json)
    {
        try { return JsonNode.Parse(json)?["key"]?["id"]?.ToString(); }
        catch { return null; }
    }

    private async Task BumpSessionCounterAsync(string sessionId, CancellationToken ct)
    {
        var s = await _db.WaSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        if (s is null) return;
        s.SentToday += 1;
        s.LastSeenAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task MarkSessionAsync(string sessionId, string status, CancellationToken ct)
    {
        var s = await _db.WaSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        if (s is null) return;
        s.Status = status;
        s.RiskScore = Math.Max(s.RiskScore, 90);
        await _db.SaveChangesAsync(ct);
        _log.LogError("🔴 الجلسة {SessionId} بقت {Status}", sessionId, status);
    }

    // ══════════════════════════════════════════════════════════════════
    //  HealthAsync
    // ══════════════════════════════════════════════════════════════════
    public async Task<ProviderHealth> HealthAsync(CancellationToken ct = default)
    {
        if (await _kill.IsUnofficialKilledAsync(ct))
            return new ProviderHealth { Up = false, Note = "موقوفة بمفتاح الطوارئ" };

        var sessions = await _db.WaSessions.ToListAsync(ct);
        var healthy = sessions.Where(s => (s.Status == "active" || s.Status == "warming")
                                          && s.RiskScore < 70).ToList();

        if (healthy.Count == 0)
            return new ProviderHealth { Up = false, Note = "مفيش جلسات صحّية" };

        var quota = healthy.Sum(s => s.DailyQuota);
        var used = healthy.Sum(s => s.SentToday);
        var headroom = quota == 0 ? 0 : Math.Max(0, 1.0 - (double)used / quota);
        var avgRisk = healthy.Average(s => s.RiskScore);

        return new ProviderHealth
        {
            Up = true,
            Headroom = headroom,
            // 🔑 متدهور = الحصة قربت تخلص، أو متوسط الخطورة عالي
            Degraded = headroom < 0.15 || avgRisk >= 50,
            Quality = avgRisk >= 70 ? QualityRating.Red
                    : avgRisk >= 40 ? QualityRating.Yellow
                    : QualityRating.Green,
            Note = $"{healthy.Count} جلسة صحّية — {used}/{quota} | خطورة {avgRisk:F0}"
        };
    }
}

/// <summary>
/// محرّك التأخير البشري. docs/03.
///
/// 🔑 ليه توزيع Gaussian مش رقم ثابت؟
/// لأن الروبوت بيبعت كل ٤٥ ثانية بالظبط — والإنسان لأ. أنظمة كشف الآلية
/// عند واتساب بتشوف الانتظام ده وبتحظر. التوزيع الطبيعي بيخلّي التوقيت
/// "بشري" إحصائياً.
///
/// وبنقصّه بين ٢٥ و ٩٠ ثانية (حدود README) عشان الذيل الطويل ميطلّعش
/// تأخير ٥ دقايق أو نص ثانية.
/// </summary>
public sealed class DelayEngine
{
    private readonly UnofficialOptions _opt;
    private readonly Random _rng;

    public DelayEngine(IOptions<HybridOptions> opt, int? seed = null)
    {
        _opt = opt.Value.Unofficial;
        _rng = seed.HasValue ? new Random(seed.Value) : Random.Shared;
    }

    private const int MinMs = 25_000;
    private const int MaxMs = 90_000;

    public int Next()
    {
        if (_opt.SkipDelayInDev) return 0;

        // Box–Muller: تحويل عشوائي منتظم → توزيع طبيعي
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = 1.0 - _rng.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

        var ms = _opt.DelayMeanMs + z * _opt.DelayStdDevMs;
        return (int)Math.Clamp(ms, MinMs, MaxMs);
    }
}

/// <summary>سجل المزوّدين — الـ Router بينده منه بالقناة</summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<ChannelKind, IMessageProvider> _map;

    public ProviderRegistry(IEnumerable<IMessageProvider> providers)
        => _map = providers.ToDictionary(p => p.Channel);

    public IMessageProvider Get(ChannelKind channel)
        => _map.TryGetValue(channel, out var p)
            ? p
            : throw new InvalidOperationException($"مفيش مزوّد مسجّل للقناة {channel}");

    public IReadOnlyList<IMessageProvider> All => _map.Values.ToList();
}
