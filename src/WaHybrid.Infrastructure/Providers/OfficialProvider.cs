using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Infrastructure.Options;

namespace WaHybrid.Infrastructure.Providers;

/// <summary>
/// المزوّد الرسمي — Meta WhatsApp Cloud API. docs/09 §2.2 + docs/10 §3.
///
/// خصائصه اللي الـ Router بيعتمد عليها:
///   ✅ آمن قانونياً ومستقر — مفيش حظر مفاجئ
///   ✅ الوحيد اللي يقدر يبعت بره النافذة (بقالب معتمد)
///   ✅ الوحيد اللي بيفتح نافذة FEP (٧٢ ساعة مجانية) من إعلانات CTWA
///   ❌ بيتكلّف فلوس على الرسايل التسويقية
///   ❌ محدود بـ Messaging Tier يومي
///   ❌ مقيّد بسقف تكرار Meta (131049)
///
/// ⚠️ ملحوظة تشغيلية: الكلاس ده **مش** بيتنده في البيئة الحالية
/// (ProviderMode = mock)، لكنه مكتوب كامل عشان لما يجي التوكن الحقيقي
/// يبقى سطر واحد في الإعدادات — مش أسبوع شغل.
/// </summary>
public sealed class OfficialProvider : IMessageProvider
{
    private readonly HttpClient _http;
    private readonly OfficialOptions _opt;
    private readonly PolicyOptions _policy;
    private readonly TierOptions _tierOpt;
    private readonly ITierStore _tier;
    private readonly IFrequencyCap _freq;
    private readonly ICostBook _costBook;
    private readonly ILogger<OfficialProvider> _log;

    public ChannelKind Channel => ChannelKind.Official;

    public OfficialProvider(HttpClient http, IOptions<HybridOptions> opt, ITierStore tier,
        IFrequencyCap freq, ICostBook costBook, ILogger<OfficialProvider> log)
    {
        _http = http;
        _opt = opt.Value.Official;
        _policy = opt.Value.Policy;
        _tierOpt = opt.Value.Tier;
        _tier = tier;
        _freq = freq;
        _costBook = costBook;
        _log = log;

        if (!string.IsNullOrWhiteSpace(_opt.AccessToken))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _opt.AccessToken);
    }

    private string SendUrl =>
        $"https://graph.facebook.com/{_opt.GraphVersion}/{_opt.PhoneNumberId}/messages";

    // ══════════════════════════════════════════════════════════════════
    //  CanAsync — كل الفحوص **قبل** ما ندفع فلوس أو نحرق حصة
    // ══════════════════════════════════════════════════════════════════
    public async Task<CanSendResult> CanAsync(SendRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.AccessToken) || string.IsNullOrWhiteSpace(_opt.PhoneNumberId))
            return CanSendResult.Deny("إعدادات الحساب الرسمي ناقصة (توكن/رقم)", "not_configured");

        // 1️⃣ رسالة حرة بره النافذة = مستحيل. ده قانون Meta مش اختيار.
        if (request.Template is null && !request.Meta.WindowOpen)
            return CanSendResult.Deny(
                "رسالة حرة بره النافذة — لازم قالب معتمد", "131047", gate: "gWindow");

        // 2️⃣ الحد اليومي (Messaging Tier) + هامش أمان + حجز للمعاملات الحرجة
        var snap = await _tier.CurrentAsync(ct);
        var usable = (int)(snap.Limit * _tierOpt.SafetyMargin);
        var isMarketing = request.Template?.Category == MetaCategory.Marketing;

        if (isMarketing)
        {
            // 🔑 بنحجز نسبة من الحد للمعاملات الحرجة — التسويق مياخدهاش
            var marketingCeiling = (int)(usable * (1 - _tierOpt.ReserveForCritical));
            if (snap.UsedToday >= marketingCeiling)
                return CanSendResult.Deny(
                    $"حصة التسويق اليومية خلصت ({snap.UsedToday}/{marketingCeiling}) — الباقي محجوز للمعاملات",
                    "tier_marketing_reserved", retryAt: NextUtcMidnight(), gate: "gMessagingTier");
        }
        else if (snap.UsedToday >= usable)
        {
            return CanSendResult.Deny(
                $"الحد اليومي خلص ({snap.UsedToday}/{usable})", "133016",
                retryAt: NextUtcMidnight(), gate: "gMessagingTier");
        }

        // 3️⃣ الجودة حمراء → التسويق موقوف
        if (isMarketing && (snap.Quality == QualityRating.Red || snap.MarketingPaused))
            return CanSendResult.Deny(
                "🔴 جودة الرقم حمراء — التسويق موقوف أوتوماتيك لحماية الحساب",
                "quality_red", gate: "gQuality");

        // 4️⃣ سقف تكرار Meta المتوقع (131049) — تقدير محافظ قبل ما ناخد الخطأ
        if (isMarketing)
        {
            var used = await _freq.GetMetaMarketingCountAsync(request.To, ct);
            if (used >= _policy.MetaMarketingCapAssumed)
                return CanSendResult.Deny(
                    $"سقف تكرار Meta المتوقع ({used}/{_policy.MetaMarketingCapAssumed}) — أجّل لبكرة",
                    "131049", retryAt: NextUtcMidnight(), gate: "gMetaFrequencyCap");
        }

        return CanSendResult.Allow();
    }

    // ══════════════════════════════════════════════════════════════════
    //  SendAsync
    // ══════════════════════════════════════════════════════════════════
    public async Task<SendResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        var payload = BuildPayload(request);

        try
        {
            using var res = await _http.PostAsJsonAsync(SendUrl, payload, ct);
            var text = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                var (code, msg) = ExtractError(text);
                var rule = MetaErrorMap.Resolve(code);

                _log.LogWarning("❌ الرسمي رفض — كود {Code}: {Meaning} | الإجراء: {Action}",
                    code, rule.ArabicMeaning, rule.Action);

                return SendResult.Fail(Channel, code ?? "http_" + (int)res.StatusCode,
                    $"{rule.ArabicMeaning} — {msg}", rule.Retryable, rule.RetryAfterMs, rule.Fatal);
            }

            var id = ExtractMessageId(text);
            await _tier.IncrementAsync(1, ct);

            if (request.Template?.Category == MetaCategory.Marketing)
                await _freq.RecordAsync(request.To, Channel, ct);

            // 💰 تقديري بس. الفاتورة الحقيقية بتتأكد من webhook التسليم (docs/08 §4.1)
            var cost = request.Meta.WindowState == WindowState.FepOpen
                ? 0m
                : request.Template is null
                    ? 0m
                    : _costBook.Price(request.To, request.Template.Category);

            return SendResult.Success(Channel, id ?? "unknown", cost);
        }
        catch (TaskCanceledException)
        {
            // ⚠️ أخطر حالة في النظام: timeout غامض. الرسالة ممكن تكون وصلت!
            // عشان كده الـ IdempotencyKey إجباري — لو أعدنا المحاولة، مش هتتبعت مرتين.
            return SendResult.Fail(Channel, "timeout",
                "مهلة الاتصال انتهت — الحالة غامضة، الـ idempotency هو اللي بيحمينا",
                retryable: true, retryAfterMs: 20_000);
        }
        catch (HttpRequestException ex)
        {
            return SendResult.Fail(Channel, "network",
                $"فشل الشبكة: {ex.Message}", retryable: true, retryAfterMs: 15_000);
        }
    }

    /// <summary>بناء الـ payload حسب قواعد Cloud API</summary>
    private static object BuildPayload(SendRequest r)
    {
        if (r.Template is not null)
        {
            var components = r.Template.Parameters.Count == 0
                ? Array.Empty<object>()
                : new object[]
                {
                    new
                    {
                        type = "body",
                        parameters = r.Template.Parameters
                            .Select(p => new { type = "text", text = p }).ToArray()
                    }
                };

            return new
            {
                messaging_product = "whatsapp",
                to = r.To,
                type = "template",
                template = new
                {
                    name = r.Template.Name,
                    language = new { code = r.Template.Language },
                    components
                }
            };
        }

        return r.Type switch
        {
            "image" => new
            {
                messaging_product = "whatsapp", to = r.To, type = "image",
                image = new { link = r.MediaUrl, caption = r.Body }
            },
            "document" => new
            {
                messaging_product = "whatsapp", to = r.To, type = "document",
                document = new { link = r.MediaUrl, caption = r.Body }
            },
            _ => new
            {
                messaging_product = "whatsapp", to = r.To, type = "text",
                text = new { body = r.Body ?? "", preview_url = false }
            }
        };
    }

    private static (string? code, string message) ExtractError(string json)
    {
        try
        {
            var root = JsonNode.Parse(json)?["error"];
            var code = root?["code"]?.ToString();
            var sub = root?["error_subcode"]?.ToString();
            var msg = root?["message"]?.ToString() ?? "بدون رسالة";
            // 🔑 الـ subcode أدق من الـ code — 131049 بييجي كـ subcode
            return (string.IsNullOrEmpty(sub) || sub == "0" ? code : sub, msg);
        }
        catch { return (null, json.Length > 300 ? json[..300] : json); }
    }

    private static string? ExtractMessageId(string json)
    {
        try { return JsonNode.Parse(json)?["messages"]?[0]?["id"]?.ToString(); }
        catch { return null; }
    }

    /// <summary>⚠️ Meta بتصفّر الحد اليومي على UTC — مش التوقيت المحلي</summary>
    private static DateTimeOffset NextUtcMidnight()
        => new(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero);

    // ══════════════════════════════════════════════════════════════════
    //  HealthAsync — الـ Router بيستخدمها في التدهور
    // ══════════════════════════════════════════════════════════════════
    public async Task<ProviderHealth> HealthAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.AccessToken))
            return new ProviderHealth { Up = false, Note = "غير مُعَدّ (مفيش توكن)" };

        var snap = await _tier.CurrentAsync(ct);
        return new ProviderHealth
        {
            Up = true,
            Headroom = snap.Headroom,
            // 🔑 متدهور = فاضل أقل من ١٠٪ من الحد، أو الجودة أصفر/أحمر
            Degraded = snap.Headroom < 0.10 || snap.Quality is QualityRating.Red or QualityRating.Yellow,
            Quality = snap.Quality,
            Note = $"{snap.Tier} — {snap.UsedToday}/{snap.Limit}"
        };
    }
}
