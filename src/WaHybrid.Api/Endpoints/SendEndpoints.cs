using Microsoft.EntityFrameworkCore;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Intents;
using WaHybrid.Infrastructure.Data;
using WaHybrid.Infrastructure.Routing;

namespace WaHybrid.Api.Endpoints;

public sealed record SendRequestDto(
    string Phone,
    string Intent,
    string? Body,
    long? CampaignId,
    Dictionary<string, string>? Params);

/// <summary>مسارات الإرسال — نقطة الدخول الوحيدة</summary>
public static class SendEndpoints
{
    public static void MapSendEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/send").WithTags("الإرسال");

        g.MapPost("/", async (SendRequestDto dto, HybridDbContext db, IMessageSender sender) =>
        {
            if (!IntentRegistry.Exists(dto.Intent))
                return Results.BadRequest(new { error = $"نية مجهولة: {dto.Intent}" });

            var c = await db.Customers.FirstOrDefaultAsync(x => x.Phone == dto.Phone);
            if (c is null) return Results.NotFound(new { error = "العميل مش موجود" });

            var intent = new SendIntent
            {
                Name = dto.Intent,
                CustomerId = c.Id,
                Phone = dto.Phone,
                Body = dto.Body,
                CampaignId = dto.CampaignId,
                Segment = c.Segment,
                TemplateParams = dto.Params ?? DefaultParams(c.Name)
            };

            var outcome = await sender.SendAsync(intent);

            return Results.Ok(new
            {
                ok = outcome.Ok,
                logId = outcome.LogId,
                channel = outcome.Channel?.ToString(),
                mode = outcome.Mode?.ToString(),
                windowState = outcome.WindowState.ToString(),
                routeReason = outcome.RouteReason,
                reason = outcome.Reason,
                blockedByGate = outcome.BlockedByGate,
                errorCode = outcome.ErrorCode,
                estimatedCostUsd = outcome.EstimatedCostUsd,
                providerMessageId = outcome.ProviderMessageId,
                deduped = outcome.Deduped,
                fatal = outcome.Fatal,
                tried = outcome.Tried.Select(t => new { channel = t.Channel.ToString(), why = t.Why })
            });
        })
        .WithSummary("📤 إرسال رسالة (نقطة الدخول الوحيدة)");

        // 🔑 برهان منع التكرار: بنبعت نفس النية مرتين ورا بعض
        g.MapPost("/prove-idempotency", async (string phone, string intent,
            HybridDbContext db, IMessageSender sender) =>
        {
            var c = await db.Customers.FirstOrDefaultAsync(x => x.Phone == phone);
            if (c is null) return Results.NotFound(new { error = "العميل مش موجود" });

            var mk = () => new SendIntent
            {
                Name = intent, CustomerId = c.Id, Phone = phone,
                Body = "رسالة اختبار منع التكرار",
                TemplateParams = DefaultParams(c.Name)
            };

            var first = await sender.SendAsync(mk());
            var second = await sender.SendAsync(mk());

            var key = IdempotencyKeyFactory.Create(c.Id, intent, null,
                DateOnly.FromDateTime(DateTime.UtcNow));

            return Results.Ok(new
            {
                idempotencyKey = key,
                explanation =
                    "نفس (عميل + نية + حملة + يوم) = نفس المفتاح بالظبط. "
                    + "المحاولة التانية بترفض من بوابة gCrossChannelDedupe — "
                    + "وده اللي بيمنع العميل ياخد نفس الرسالة من القناتين.",
                first = new
                {
                    ok = first.Ok, channel = first.Channel?.ToString(),
                    gate = first.BlockedByGate, reason = first.Reason
                },
                second = new
                {
                    ok = second.Ok, channel = second.Channel?.ToString(),
                    gate = second.BlockedByGate, reason = second.Reason,
                    deduped = second.Deduped
                },
                verdict = !second.Ok && second.BlockedByGate == "gCrossChannelDedupe"
                    ? "✅ منع التكرار شغّال"
                    : "⚠️ راجع الإعداد"
            });
        })
        .WithSummary("🔁 برهان منع التكرار بين القناتين");
    }

    internal static Dictionary<string, string> DefaultParams(string? name) => new()
    {
        ["name"] = name ?? "عميلنا",
        ["order_id"] = Random.Shared.Next(10000, 99999).ToString(),
        ["total"] = Random.Shared.Next(200, 3000).ToString(),
        ["tracking"] = $"TRK{Random.Shared.Next(1000, 9999)}",
        ["eta"] = "٢-٣ أيام",
        ["reason"] = "بطلب العميل",
        ["offer"] = "خصم ٢٠٪ على كل المجموعة",
        ["item"] = "تيشيرت قطن"
    };
}

/// <summary>مسارات الحملات — التخطيط قبل التنفيذ</summary>
public static class CampaignEndpoints
{
    public static void MapCampaignEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/campaigns").WithTags("الحملات");

        // 🔍 أهم مسار للمدير: التكلفة قبل الصرف
        g.MapGet("/plan", async (string intent, string? segment, int? limit,
            CampaignPlanner planner) =>
        {
            if (!IntentRegistry.Exists(intent))
                return Results.BadRequest(new { error = $"نية مجهولة: {intent}" });

            var plan = await planner.PlanAsync(intent, segment, limit ?? 1000);

            return Results.Ok(new
            {
                intent = plan.IntentName,
                intentLabel = plan.IntentLabel,
                segment = plan.Segment,
                templateName = plan.TemplateName,
                templateAvailable = plan.TemplateAvailable,
                totals = new
                {
                    targeted = plan.TotalTargeted,
                    sendable = plan.Sendable,
                    skipped = plan.Skipped
                },
                byChannel = new { official = plan.Official, unofficial = plan.Unofficial },
                byMode = new { free = plan.FreeMessages, template = plan.TemplateMessages },
                byWindow = new { fep = plan.InFep, csw = plan.InCsw, none = plan.NoWindow },
                money = new
                {
                    estimatedCostUsd = Math.Round(plan.EstimatedCostUsd, 4),
                    costPerMessage = Math.Round(plan.CostPerSendable, 5),
                    ifAllOfficialTemplates = Math.Round(plan.CostIfAllOfficialTemplates(), 2),
                    savings = Math.Round(plan.SavingsVsAllOfficial(), 2)
                },
                kpi = new
                {
                    freePct = Math.Round(plan.FreePct, 1),
                    target = 75.0,
                    verdict = plan.FreePct >= 75
                        ? "✅ فوق المستهدف (٧٥٪)"
                        : "⚠️ تحت المستهدف — محتاج استراتيجية FEP/CTWA أقوى"
                },
                routeReasons = plan.RouteReasons,
                skipReasons = plan.SkipReasons,
                ascii = plan.ToAsciiBox()
            });
        })
        .WithSummary("🔍 تخطيط حملة (Dry Run) — التكلفة قبل الصرف");
    }
}
