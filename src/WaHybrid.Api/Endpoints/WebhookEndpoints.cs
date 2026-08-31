using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WaHybrid.Domain.Enums;
using WaHybrid.Infrastructure.Options;
using WaHybrid.Infrastructure.Webhooks;

namespace WaHybrid.Api.Endpoints;

/// <summary>
/// مسارات الـ Webhooks — باب الدخول للنظام. docs/09 §7 + docs/10 §6.
///
/// ═══════════════════════════════════════════════════════════════════
///  🔑 ليه المسارات دي أهم من مسارات الإرسال؟
/// ═══════════════════════════════════════════════════════════════════
/// عشان **النوافذ المجانية بتتفتح من هنا**.
///
/// كل رسالة داخلة بتفوتنا = نافذة CSW ٢٤ ساعة ضاعت = رسايل كنا هنبعتها
/// مجاناً بقت بقوالب مدفوعة.
/// وكل ضغطة إعلان (CTWA) بتفوتنا = نافذة FEP ٧٢ ساعة ضاعت — وده أغلى،
/// لأن الـ FEP بيخلّي **التسويق نفسه** مجاني، وهو أغلى بند في القائمة
/// ($0.0300 للرسالة في مصر).
///
/// ═══════════════════════════════════════════════════════════════════
///  ⚠️ ثلاث قواعد لازم تتحقق في أي webhook
/// ═══════════════════════════════════════════════════════════════════
/// ١. **رجّع 200 بسرعة.** Meta بتستنى أقل من ٥ ثواني. لو أبطأت،
///    بتعيد المحاولة، وبعد فشل متكرر بتوقّف الـ webhook خالص.
///    ⇒ يعني: مفيش شغل تقيل هنا. استقبل، طبّع، اكتب، ارجع.
///
/// ٢. **رجّع 200 حتى لو المعالجة فشلت** (لو التوقيع صح).
///    لأن إعادة المحاولة من Meta هتجيبلك نفس الحدث تاني وهتفشل تاني.
///    الأفضل: اقبل، ولوّغ الخطأ عندك.
///
/// ٣. **الـ raw body مقدّس.** لازم تتحقق من التوقيع على البايت الخام
///    قبل أي parsing. لو عملت deserialize و serialize تاني، التوقيع
///    مش هيطابق أبداً (ترتيب المفاتيح والمسافات بيتغيّروا).
/// </summary>
public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/webhooks").WithTags("الـ Webhooks");

        // ════════════════════════════════════════════════════════════
        //  ١. مصافحة التحقق (Meta)
        // ════════════════════════════════════════════════════════════
        // Meta بتنده على المسار ده بـ GET مرة واحدة وقت ما تسجّل
        // الـ webhook في لوحة التطبيق. لو رجّعت الـ challenge صح،
        // بتفعّل الاشتراك. لو غلط، بترفض.
        //
        // ⚠️ لازم ترجّع الـ challenge كـ **نص خام** — مش JSON.
        //    Meta بتقارن النص حرف بحرف. لو رجّعته كـ JSON string
        //    (بعلامات تنصيص) هترفض.
        g.MapGet("/official", (HttpRequest req, IOptions<HybridOptions> opt, ILoggerFactory lf) =>
        {
            var log = lf.CreateLogger("Webhook.Verify");

            var mode = req.Query["hub.mode"].ToString();
            var token = req.Query["hub.verify_token"].ToString();
            var challenge = req.Query["hub.challenge"].ToString();

            var expected = opt.Value.Official.WebhookVerifyToken;

            if (mode == "subscribe" && !string.IsNullOrEmpty(expected) && token == expected)
            {
                log.LogInformation("✅ مصافحة التحقق نجحت");
                return Results.Text(challenge, "text/plain");
            }

            log.LogWarning("❌ مصافحة تحقق مرفوضة — mode={Mode}", mode);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        })
        .WithSummary("مصافحة تحقق Meta (hub.challenge)")
        .WithDescription("Meta بتنده عليه مرة واحدة وقت تسجيل الـ webhook. "
                       + "بيرجّع الـ challenge كنص خام لو التوكن مطابق.");

        // ════════════════════════════════════════════════════════════
        //  ٢. أحداث القناة الرسمية
        // ════════════════════════════════════════════════════════════
        g.MapPost("/official", async (HttpRequest req, InboundHandler handler,
            IOptions<HybridOptions> opt, ILoggerFactory lf, CancellationToken ct) =>
        {
            var log = lf.CreateLogger("Webhook.Official");

            // 🔐 اقرا الـ raw body **الأول** — قبل أي parsing
            var raw = await ReadRawAsync(req, ct);

            var secret = opt.Value.Official.AppSecret;
            var sigHeader = req.Headers["X-Hub-Signature-256"].ToString();

            if (!string.IsNullOrWhiteSpace(secret))
            {
                if (!WebhookSignature.Verify(raw, sigHeader, secret))
                {
                    log.LogWarning("🔐 توقيع webhook مرفوض — الطلب اتجاهل");
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);
                }
            }
            else
            {
                // في العرض/التطوير مفيش AppSecret، فبنتخطّى التحقق.
                // ⚠️ في الإنتاج ده **ممنوع** — لازم AppSecret يكون موجود.
                log.LogWarning("⚠️ AppSecret مش مضبوط — التحقق من التوقيع متخطّى (تطوير بس)");
            }

            var json = Encoding.UTF8.GetString(raw);
            var messages = WebhookNormalizers.Official(json);

            var results = new List<object>();

            foreach (var m in messages)
            {
                try
                {
                    var r = await handler.HandleAsync(m, ct);
                    results.Add(new
                    {
                        phone = m.Phone,
                        isNew = r.IsNewCustomer,
                        fepOpened = r.FepOpenedUntil,
                        cswUntil = r.CswUntil,
                        optedOut = r.OptedOut,
                        fepSource = m.Fep?.Source
                    });
                }
                catch (Exception ex)
                {
                    // 🔑 نلوّغ ونكمّل — مش بنرمي، عشان مانخلّيش Meta
                    //    تعيد إرسال الحدث ده للأبد.
                    log.LogError(ex, "فشل معالجة رسالة داخلة من {Phone}", m.Phone);
                    results.Add(new { phone = m.Phone, error = ex.Message });
                }
            }

            // لو مفيش رسايل، الحدث كان تحديث حالة (sent/delivered/read)
            var statuses = CountStatuses(json);

            return Results.Ok(new
            {
                ok = true,
                received = messages.Count,
                statusUpdates = statuses,
                processed = results
            });
        })
        .WithSummary("أحداث القناة الرسمية (Meta Cloud API)")
        .WithDescription("بيتحقق من HMAC-SHA256 على الـ raw body، يطبّع، "
                       + "يفتح FEP لو فيه referral، ويجدّد CSW دايماً.");

        // ════════════════════════════════════════════════════════════
        //  ٣. أحداث القناة غير الرسمية
        // ════════════════════════════════════════════════════════════
        // Evolution API مش بيوقّع الأحداث بطريقة Meta، فالحماية هنا
        // بتكون بـ apikey في الهيدر + الـ URL مايكونش معروف.
        g.MapPost("/unofficial", async (HttpRequest req, InboundHandler handler,
            IOptions<HybridOptions> opt, ILoggerFactory lf, CancellationToken ct) =>
        {
            var log = lf.CreateLogger("Webhook.Unofficial");

            var expectedKey = opt.Value.Unofficial.EvolutionApiKey;
            if (!string.IsNullOrWhiteSpace(expectedKey))
            {
                var provided = req.Headers["apikey"].ToString();
                if (provided != expectedKey)
                {
                    log.LogWarning("🔐 apikey غير مطابق — الطلب اتجاهل");
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);
                }
            }

            var raw = await ReadRawAsync(req, ct);
            var json = Encoding.UTF8.GetString(raw);

            var messages = WebhookNormalizers.Unofficial(json);
            var results = new List<object>();

            foreach (var m in messages)
            {
                try
                {
                    var r = await handler.HandleAsync(m, ct);
                    results.Add(new
                    {
                        phone = m.Phone,
                        isNew = r.IsNewCustomer,
                        cswUntil = r.CswUntil,
                        optedOut = r.OptedOut
                    });
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "فشل معالجة رسالة داخلة من {Phone}", m.Phone);
                    results.Add(new { phone = m.Phone, error = ex.Message });
                }
            }

            return Results.Ok(new { ok = true, received = messages.Count, processed = results });
        })
        .WithSummary("أحداث القناة غير الرسمية (Evolution API)")
        .WithDescription("بيتخطّى الجروبات والـ broadcast والرسايل الطالعة (fromMe)، "
                       + "ويجدّد CSW على الرسايل الداخلة الحقيقية بس.");

        // ════════════════════════════════════════════════════════════
        //  ٤. محاكاة — للعرض على المدير
        // ════════════════════════════════════════════════════════════
        // دي مش موجودة في الإنتاج. الفكرة إن المدير يضغط زرار في الشاشة
        // فيشوف نافذة ٧٢ ساعة بتتفتح قصاده، وبعدين يشوف التوجيه اتغيّر
        // من "قالب مدفوع" لـ "رسالة حرة مجانية".
        var sim = g.MapGroup("/simulate").WithTags("المحاكاة");

        sim.MapPost("/ctwa", async (string phone, string? adId, string? headline,
            InboundHandler handler, ILoggerFactory lf, CancellationToken ct) =>
        {
            // بنبني payload رسمي حقيقي الشكل — بنفس الـ referral اللي
            // Meta بتبعته فعلاً لما العميل يضغط إعلان.
            var payload = BuildOfficialInboundJson(
                phone: phone,
                text: headline is null ? "مرحباً، شفت الإعلان" : $"مرحباً، شفت إعلان: {headline}",
                referral: new JsonObject
                {
                    ["source_type"] = "ad",
                    ["source_id"] = adId ?? "23851234567890123",
                    ["headline"] = headline ?? "خصم ٢٥٪ على أول أوردر",
                    ["ctwa_clid"] = "ctwa_" + Guid.NewGuid().ToString("N")[..18],
                    ["source_url"] = "https://fb.me/demo-ad"
                });

            var messages = WebhookNormalizers.Official(payload);
            if (messages.Count == 0)
                return Results.BadRequest(new { error = "فشل بناء الـ payload" });

            var r = await handler.HandleAsync(messages[0], ct);

            lf.CreateLogger("Webhook.Simulate").LogInformation("🎁 محاكاة CTWA لـ {Phone}", phone);

            return Results.Ok(new
            {
                ok = true,
                message = "🎁 ضغطة إعلان اتحاكت — نافذة FEP ٧٢ ساعة اتفتحت. "
                        + "كل حاجة للعميل ده مجانية دلوقتي، حتى التسويق.",
                phone,
                isNewCustomer = r.IsNewCustomer,
                fepOpenedUntil = r.FepOpenedUntil,
                cswUntil = r.CswUntil,
                whatChanged = new[]
                {
                    "التوجيه للتسويق: قالب مدفوع ($0.0300) → رسالة حرة مجانية ($0.0000)",
                    "المحادثات: هتفضل على القناة الرسمية طول ما فاضل من الـ FEP أكتر من ساعتين",
                    "الوفر: ١٠٠٪ على أي رسالة تروح للعميل ده خلال ٧٢ ساعة"
                },
                rawPayloadSent = JsonNode.Parse(payload)
            });
        })
        .WithSummary("🎁 محاكاة ضغطة إعلان CTWA (تفتح نافذة ٧٢ ساعة)")
        .WithDescription("بتبني payload رسمي حقيقي الشكل بـ referral.source_type=ad "
                       + "وبتمرّره على نفس المطبّع والمعالج بالظبط — مش shortcut.");

        sim.MapPost("/inbound", async (string phone, string? text, ChannelKind? channel,
            InboundHandler handler, CancellationToken ct) =>
        {
            var ch = channel ?? ChannelKind.Unofficial;
            var body = text ?? "السلام عليكم، عايز أسأل عن الأوردر";

            var messages = ch == ChannelKind.Official
                ? WebhookNormalizers.Official(BuildOfficialInboundJson(phone, body, null))
                : WebhookNormalizers.Unofficial(BuildUnofficialInboundJson(phone, body));

            if (messages.Count == 0)
                return Results.BadRequest(new { error = "فشل بناء الـ payload" });

            var r = await handler.HandleAsync(messages[0], ct);

            return Results.Ok(new
            {
                ok = true,
                message = "🟡 رسالة داخلة اتحاكت — نافذة CSW ٢٤ ساعة اتجدّدت",
                phone,
                channel = ch.ToString(),
                isNewCustomer = r.IsNewCustomer,
                cswUntil = r.CswUntil,
                optedOut = r.OptedOut,
                note = r.OptedOut
                    ? "🚫 النص اتفهم كإلغاء اشتراك — العميل اتحظر على القناتين"
                    : "الرسالة الحرة بقت مسموحة لمدة ٢٤ ساعة"
            });
        })
        .WithSummary("🟡 محاكاة رسالة داخلة (تجدّد نافذة ٢٤ ساعة)");
    }

    // ══════════════════════════════════════════════════════════════
    //  مساعدات
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔐 قراءة الـ body كبايت خام.
    /// لازم نقراه في الذاكرة عشان نحسب الـ HMAC عليه بالظبط زي ما وصل.
    /// (الأحداث صغيرة — أقل من ١٠ كيلوبايت عادة — فمفيش قلق ذاكرة).
    /// </summary>
    private static async Task<byte[]> ReadRawAsync(HttpRequest req, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>عدد تحديثات الحالة (sent/delivered/read) في الحدث</summary>
    private static int CountStatuses(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var entries = root?["entry"]?.AsArray();
            if (entries is null) return 0;

            var n = 0;
            foreach (var e in entries)
            {
                var changes = e?["changes"]?.AsArray();
                if (changes is null) continue;
                foreach (var c in changes)
                    n += c?["value"]?["statuses"]?.AsArray()?.Count ?? 0;
            }
            return n;
        }
        catch { return 0; }
    }

    /// <summary>
    /// بناء payload رسمي بنفس شكل Meta بالظبط.
    /// 🔑 مهم إننا نبني الشكل الحقيقي مش شكل مبسّط — عشان المطبّع
    ///    يتجرّب فعلاً على نفس البنية اللي هيشوفها في الإنتاج.
    /// </summary>
    private static string BuildOfficialInboundJson(string phone, string text, JsonObject? referral)
    {
        var msg = new JsonObject
        {
            ["from"] = phone,
            ["id"] = "wamid.demo" + Guid.NewGuid().ToString("N"),
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["type"] = "text",
            ["text"] = new JsonObject { ["body"] = text }
        };

        if (referral is not null) msg["referral"] = referral;

        var root = new JsonObject
        {
            ["object"] = "whatsapp_business_account",
            ["entry"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "DEMO_WABA_ID",
                    ["changes"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["field"] = "messages",
                            ["value"] = new JsonObject
                            {
                                ["messaging_product"] = "whatsapp",
                                ["metadata"] = new JsonObject
                                {
                                    ["display_phone_number"] = "201000000000",
                                    ["phone_number_id"] = "DEMO_PHONE_ID"
                                },
                                ["contacts"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["profile"] = new JsonObject { ["name"] = "عميل تجريبي" },
                                        ["wa_id"] = phone
                                    }
                                },
                                ["messages"] = new JsonArray { msg }
                            }
                        }
                    }
                }
            }
        };

        return root.ToJsonString();
    }

    /// <summary>بناء payload Evolution بنفس شكله الحقيقي</summary>
    private static string BuildUnofficialInboundJson(string phone, string text)
    {
        var root = new JsonObject
        {
            ["event"] = "messages.upsert",
            ["instance"] = "sess-main",
            ["data"] = new JsonObject
            {
                ["key"] = new JsonObject
                {
                    ["remoteJid"] = $"{phone}@s.whatsapp.net",
                    ["fromMe"] = false,
                    ["id"] = "DEMO" + Guid.NewGuid().ToString("N")[..20]
                },
                ["pushName"] = "عميل تجريبي",
                ["message"] = new JsonObject { ["conversation"] = text },
                ["messageTimestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        };

        return root.ToJsonString();
    }
}
