using Microsoft.EntityFrameworkCore;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Intents;
using WaHybrid.Infrastructure.Data;
using WaHybrid.Infrastructure.Routing;

namespace WaHybrid.Api.Endpoints;

/// <summary>مسارات النوافذ — عرض وفتح وتجديد</summary>
public static class WindowEndpoints
{
    public static void MapWindowEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/windows").WithTags("النوافذ");

        // حالة نوافذ عميل — الاستعلام الأساسي في النظام
        g.MapGet("/{phone}", async (string phone, IWindowTracker tracker) =>
        {
            var s = await tracker.GetStateAsync(phone);
            return Results.Ok(new
            {
                phone,
                state = s.State.ToString(),
                stateArabic = s.State switch
                {
                    WindowState.FepOpen => "🎁 نافذة الدخول المجاني (٧٢ ساعة) — كل حاجة مجاناً",
                    WindowState.CswOpen => "🟡 نافذة خدمة العميل (٢٤ ساعة) — الرسالة الحرة مسموحة",
                    _ => "🔴 مفيش نافذة — قوالب معتمدة بس"
                },
                fepUntil = s.FepUntil,
                cswUntil = s.CswUntil,
                fepHoursLeft = Math.Round(s.FepHoursLeft, 2),
                cswHoursLeft = Math.Round(s.CswHoursLeft, 2),
                freeFormAllowed = s.FreeFormAllowed,
                marketingFree = s.MarketingFree
            });
        })
        .WithSummary("حالة نوافذ عميل");

        // فتح نافذة FEP يدوياً — لمحاكاة ضغطة إعلان في العرض
        g.MapPost("/{phone}/open-fep", async (string phone, HybridDbContext db,
            IWindowTracker tracker, string? source, string? sourceRef) =>
        {
            var c = await db.Customers.FirstOrDefaultAsync(x => x.Phone == phone);
            if (c is null) return Results.NotFound(new { error = "العميل مش موجود" });

            var until = await tracker.OpenFepAsync(c.Id, phone,
                source ?? Domain.Windows.WindowSources.CtwaAd, sourceRef, ChannelKind.Official);

            return Results.Ok(new
            {
                ok = true,
                message = "🎁 نافذة FEP اتفتحت — ٧٢ ساعة كل حاجة مجاناً",
                until
            });
        })
        .WithSummary("محاكاة ضغطة إعلان CTWA (تفتح ٧٢ ساعة)");

        // تجديد CSW يدوياً — لمحاكاة رسالة داخلة
        g.MapPost("/{phone}/touch-csw", async (string phone, HybridDbContext db,
            IWindowTracker tracker) =>
        {
            var c = await db.Customers.FirstOrDefaultAsync(x => x.Phone == phone);
            if (c is null) return Results.NotFound(new { error = "العميل مش موجود" });

            var until = await tracker.TouchCswAsync(c.Id, phone, null, ChannelKind.Unofficial);
            return Results.Ok(new
            {
                ok = true,
                message = "🟡 نافذة CSW اتجدّدت — ٢٤ ساعة",
                until
            });
        })
        .WithSummary("محاكاة رسالة داخلة (تجدّد ٢٤ ساعة)");
    }
}

/// <summary>مسارات التوجيه — معاينة القرار بدون إرسال</summary>
public static class RoutingEndpoints
{
    public static void MapRoutingEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/routing").WithTags("التوجيه");

        // 🔑 المسار ده هو جوهر العرض: بيوري القرار وسببه بدون إرسال ولا تكلفة
        g.MapGet("/preview", async (string phone, string intent, HybridDbContext db,
            ChannelRouter router, IWindowTracker windows, ICostBook costBook,
            ITemplateRegistry templates) =>
        {
            if (!IntentRegistry.Exists(intent))
                return Results.BadRequest(new { error = $"نية مجهولة: {intent}" });

            var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Phone == phone);
            if (c is null) return Results.NotFound(new { error = "العميل مش موجود" });

            var spec = IntentRegistry.Get(intent);
            var win = await windows.GetStateAsync(phone);
            var si = new SendIntent
            {
                Name = intent, CustomerId = c.Id, Phone = phone, Segment = c.Segment
            };

            var decision = await router.DecideAsync(spec, win, si);
            var template = decision.Mode == SendMode.Template
                ? await templates.ForIntentAsync(intent)
                : null;

            var cost = template is not null && win.State != WindowState.FepOpen
                ? costBook.Price(phone, template.Category)
                : 0m;

            return Results.Ok(new
            {
                phone,
                customer = c.Name,
                intent,
                intentLabel = spec.ArabicLabel,
                intentClass = spec.Class.ToString(),
                critical = spec.Critical,
                metaCategory = spec.MetaCategory.ToString(),
                window = new
                {
                    state = win.State.ToString(),
                    fepHoursLeft = Math.Round(win.FepHoursLeft, 1),
                    cswHoursLeft = Math.Round(win.CswHoursLeft, 1)
                },
                decision = new
                {
                    allowed = decision.Allowed,
                    channel = decision.Channel?.ToString(),
                    mode = decision.Mode?.ToString(),
                    reason = decision.Reason,
                    templateName = template?.Name,
                    estimatedCostUsd = cost
                }
            });
        })
        .WithSummary("🔍 معاينة قرار التوجيه (بدون إرسال ولا تكلفة)");

        // مصفوفة القرار كاملة — الجدول اللي في docs/09 §4.2 محسوب حياً
        g.MapGet("/matrix", async (HybridDbContext db, ChannelRouter router) =>
        {
            string[] intents =
            [
                IntentNames.CampaignPromo, IntentNames.AbandonedCart,
                IntentNames.OrderConfirmed, IntentNames.OrderDelivered,
                IntentNames.BotReply, IntentNames.FaqAnswer, IntentNames.Otp
            ];

            var states = new (string label, Domain.Windows.CustomerWindowState win)[]
            {
                ("🎁 FEP مفتوحة", new(WindowState.FepOpen,
                    DateTimeOffset.UtcNow.AddHours(60), DateTimeOffset.UtcNow.AddHours(20))),
                ("🟡 CSW مفتوحة", new(WindowState.CswOpen,
                    null, DateTimeOffset.UtcNow.AddHours(18))),
                ("🔴 مفيش نافذة", Domain.Windows.CustomerWindowState.None)
            };

            var anyCustomer = await db.Customers.AsNoTracking().FirstOrDefaultAsync();
            var rows = new List<object>();

            foreach (var intentName in intents)
            {
                var spec = IntentRegistry.Get(intentName);
                var cells = new List<object>();

                foreach (var (label, win) in states)
                {
                    var si = new SendIntent
                    {
                        Name = intentName,
                        CustomerId = anyCustomer?.Id ?? 0,
                        Phone = anyCustomer?.Phone ?? "201000000000"
                    };

                    var d = await router.DecideAsync(spec, win, si);
                    cells.Add(new
                    {
                        window = label,
                        channel = d.Channel?.ToString() ?? "—",
                        mode = d.Mode?.ToString() ?? "—",
                        allowed = d.Allowed,
                        reason = d.Reason
                    });
                }

                rows.Add(new
                {
                    intent = intentName,
                    label = spec.ArabicLabel,
                    intentClass = spec.Class.ToString(),
                    critical = spec.Critical,
                    cells
                });
            }

            return Results.Ok(new { rows });
        })
        .WithSummary("📊 مصفوفة القرار كاملة (محسوبة حياً)");

        // تشخيص البوابات — كل بوابة ونتيجتها
        g.MapGet("/gates", async (string phone, string intent, HybridDbContext db,
            IGateChain chain, ChannelRouter router, IWindowTracker windows,
            ITemplateRegistry templates) =>
        {
            var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Phone == phone);
            if (c is null) return Results.NotFound(new { error = "العميل مش موجود" });
            if (!IntentRegistry.Exists(intent))
                return Results.BadRequest(new { error = $"نية مجهولة: {intent}" });

            var spec = IntentRegistry.Get(intent);
            var win = await windows.GetStateAsync(phone);
            var si = new SendIntent { Name = intent, CustomerId = c.Id, Phone = phone };
            var decision = await router.DecideAsync(spec, win, si);

            TemplatePayload? payload = null;
            if (decision.Mode == SendMode.Template)
            {
                var t = await templates.ForIntentAsync(intent);
                if (t is not null)
                    payload = templates.Build(t, new Dictionary<string, string>
                    {
                        ["name"] = c.Name ?? "عميلنا",
                        ["order_id"] = "12345",
                        ["total"] = "499",
                        ["tracking"] = "TRK99",
                        ["eta"] = "بكرة",
                        ["reason"] = "بطلب العميل",
                        ["offer"] = "خصم ٢٠٪",
                        ["item"] = "تيشيرت"
                    });
            }

            var ctx = new GateContext
            {
                Phone = phone, CustomerId = c.Id, IntentName = intent,
                IdempotencyKey = IdempotencyKeyFactory.Create(c.Id, intent, null,
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                Channel = decision.Channel, Mode = decision.Mode, Template = payload
            };

            var trace = await chain.TraceAsync(ctx);

            return Results.Ok(new
            {
                phone, intent,
                routeDecision = new
                {
                    channel = decision.Channel?.ToString(),
                    mode = decision.Mode?.ToString(),
                    reason = decision.Reason
                },
                gates = trace.Select(t => new
                {
                    gate = t.Gate, order = t.Order, passed = t.Passed, reason = t.Reason
                })
            });
        })
        .WithSummary("🚧 تشخيص البوابات (كل بوابة ونتيجتها)");
    }
}
