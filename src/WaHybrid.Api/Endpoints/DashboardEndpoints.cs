using Microsoft.EntityFrameworkCore;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Intents;
using WaHybrid.Infrastructure.Data;
using WaHybrid.Infrastructure.Providers;

namespace WaHybrid.Api.Endpoints;

/// <summary>مسارات الداشبورد — الأرقام اللي المدير بيشوفها</summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/dashboard").WithTags("الداشبورد");

        // ═══════════════ النظرة العامة ═══════════════
        g.MapGet("/overview", async (HybridDbContext db, ITierStore tier, ICostGuard cost,
            IProviderRegistry providers, IKillSwitch kill) =>
        {
            var now = DateTimeOffset.UtcNow;
            var todayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);

            var outbound = await db.MessageLogs.AsNoTracking()
                .Where(m => m.Direction == MessageDirection.Out)
                .Select(m => new
                {
                    m.Channel, m.SendMode, m.WindowState, m.Status,
                    m.CostEstimated, m.CostBilled, m.CreatedAt, m.Intent
                })
                .ToListAsync();

            var sent = outbound.Where(m => m.Status is MessageStatus.Sent
                or MessageStatus.Delivered or MessageStatus.Read).ToList();

            var freeCount = sent.Count(m => m.SendMode == SendMode.Free);
            var freePct = sent.Count == 0 ? 0 : (double)freeCount / sent.Count * 100;

            var tierSnap = await tier.CurrentAsync();
            var budget = await cost.CheckAsync();

            var health = new List<object>();
            foreach (var p in providers.All)
            {
                var h = await p.HealthAsync();
                health.Add(new
                {
                    channel = p.Channel.ToString(),
                    up = h.Up, degraded = h.Degraded,
                    headroom = Math.Round(h.Headroom * 100, 1),
                    quality = h.Quality.ToString(), note = h.Note
                });
            }

            // النوافذ المفتوحة دلوقتي
            var windows = await db.CustomerWindows.AsNoTracking()
                .Where(w => w.ExpiresAt > now)
                .Select(w => w.Kind)
                .ToListAsync();

            var totalCustomers = await db.Customers.CountAsync();
            var fepOpen = windows.Count(k => k == WindowKind.Fep);
            var cswOnly = await db.Customers.AsNoTracking()
                .CountAsync(c => c.Windows.Any(w => w.Kind == WindowKind.Csw && w.ExpiresAt > now)
                                 && !c.Windows.Any(w => w.Kind == WindowKind.Fep && w.ExpiresAt > now));

            return Results.Ok(new
            {
                at = now,
                customers = new
                {
                    total = totalCustomers,
                    optedIn = await db.Customers.CountAsync(c => c.OptedIn && !c.OptedOut),
                    optedOut = await db.Customers.CountAsync(c => c.OptedOut),
                    suppressed = await db.SuppressionList.CountAsync(),
                    fromCtwa = await db.Customers.CountAsync(c => c.AcquisitionSource == AcquisitionSource.Ctwa)
                },
                windows = new
                {
                    fepOpen,
                    cswOpen = cswOnly,
                    noWindow = Math.Max(0, totalCustomers - fepOpen - cswOnly)
                },
                messages = new
                {
                    total = outbound.Count,
                    sent = sent.Count,
                    failed = outbound.Count(m => m.Status == MessageStatus.Failed),
                    blocked = outbound.Count(m => m.Status == MessageStatus.Blocked),
                    skipped = outbound.Count(m => m.Status == MessageStatus.Skipped),
                    inbound = await db.MessageLogs.CountAsync(m => m.Direction == MessageDirection.In)
                },
                byChannel = new
                {
                    official = sent.Count(m => m.Channel == ChannelKind.Official),
                    unofficial = sent.Count(m => m.Channel == ChannelKind.Unofficial)
                },
                byMode = new
                {
                    free = freeCount,
                    template = sent.Count(m => m.SendMode == SendMode.Template)
                },
                kpi = new
                {
                    // 🎯 المؤشر الأساسي في النظام كله
                    freePct = Math.Round(freePct, 1),
                    freePctTarget = 75.0,
                    freePctOk = freePct >= 75
                },
                money = new
                {
                    spentToday = Math.Round(budget.SpentToday, 4),
                    spentMonth = Math.Round(budget.SpentMonth, 4),
                    dailyLimit = budget.DailyLimit,
                    monthlyLimit = budget.MonthlyLimit,
                    pct = Math.Round(budget.Pct, 1),
                    hardStop = budget.HardStop,
                    alert = budget.Alert,
                    // 💵 الرقم اللي بيوري قيمة النظام: لو كنا رسمي ١٠٠٪
                    wouldHaveCost = Math.Round(sent.Count * 0.035m, 2),
                    saved = Math.Round(sent.Count * 0.035m
                        - sent.Sum(m => m.CostBilled ?? m.CostEstimated), 2)
                },
                tier = new
                {
                    tierSnap.Tier, tierSnap.Limit, tierSnap.UsedToday,
                    quality = tierSnap.Quality.ToString(),
                    headroom = Math.Round(tierSnap.Headroom * 100, 1),
                    marketingPaused = tierSnap.MarketingPaused
                },
                providers = health,
                killSwitch = new
                {
                    unofficial = await kill.IsUnofficialKilledAsync(),
                    global = await kill.IsGlobalKilledAsync()
                }
            });
        })
        .WithSummary("📊 النظرة العامة (كل أرقام الداشبورد)");

        // ═══════════════ سجل الرسايل ═══════════════
        g.MapGet("/messages", async (HybridDbContext db, int? take) =>
        {
            var rows = await db.MessageLogs.AsNoTracking()
                .OrderByDescending(m => m.Id)
                .Take(Math.Clamp(take ?? 60, 1, 300))
                .Select(m => new
                {
                    m.Id, m.Phone,
                    direction = m.Direction.ToString(),
                    channel = m.Channel.ToString(),
                    m.Intent,
                    windowState = m.WindowState.ToString(),
                    mode = m.SendMode.ToString(),
                    m.TemplateName,
                    status = m.Status.ToString(),
                    m.RouteReason, m.ErrorCode, m.ErrorMessage,
                    m.CostEstimated, m.Content, m.CreatedAt,
                    fallbackFrom = m.FallbackFrom.HasValue ? m.FallbackFrom.Value.ToString() : null
                })
                .ToListAsync();

            return Results.Ok(rows);
        })
        .WithSummary("📜 سجل الرسايل الموحّد (القناتين مع بعض)");

        // ═══════════════ العملاء ═══════════════
        g.MapGet("/customers", async (HybridDbContext db) =>
        {
            var now = DateTimeOffset.UtcNow;

            var rows = await db.Customers.AsNoTracking()
                .Include(c => c.Windows)
                .OrderByDescending(c => c.Priority)
                .Select(c => new
                {
                    c.Id, c.Phone, c.Name, c.Segment, c.OptedIn, c.OptedOut,
                    acquisitionSource = c.AcquisitionSource.ToString(),
                    c.Monetary, c.Frequency, c.RecencyDays, c.Priority,
                    fepUntil = c.Windows.Where(w => w.Kind == WindowKind.Fep && w.ExpiresAt > now)
                        .Select(w => (DateTimeOffset?)w.ExpiresAt).FirstOrDefault(),
                    cswUntil = c.Windows.Where(w => w.Kind == WindowKind.Csw && w.ExpiresAt > now)
                        .Select(w => (DateTimeOffset?)w.ExpiresAt).FirstOrDefault(),
                    preferredChannel = c.PreferredChannel.HasValue
                        ? c.PreferredChannel.Value.ToString() : null
                })
                .ToListAsync();

            return Results.Ok(rows.Select(c => new
            {
                c.Id, c.Phone, c.Name, c.Segment, c.OptedIn, c.OptedOut,
                c.acquisitionSource, c.Monetary, c.Frequency, c.RecencyDays, c.Priority,
                c.fepUntil, c.cswUntil, c.preferredChannel,
                windowState = c.fepUntil is not null ? "FepOpen"
                            : c.cswUntil is not null ? "CswOpen"
                            : "NoWindow"
            }));
        })
        .WithSummary("👥 العملاء مع حالة نوافذهم");

        // ═══════════════ النوايا والقوالب ═══════════════
        g.MapGet("/intents", () => Results.Ok(
            IntentRegistry.All.Select(i => new
            {
                i.Name, label = i.ArabicLabel,
                intentClass = i.Class.ToString(),
                i.Critical,
                metaCategory = i.MetaCategory.ToString()
            })))
        .WithSummary("🎯 سجل النوايا (١٦ نية)");

        g.MapGet("/templates", async (HybridDbContext db) => Results.Ok(
            await db.WaTemplates.AsNoTracking().Select(t => new
            {
                t.Id, t.Name, t.Language,
                category = t.Category.ToString(),
                status = t.Status.ToString(),
                quality = t.Quality.HasValue ? t.Quality.Value.ToString() : null,
                t.Intent, t.BodyText, t.RequiredParamsJson, t.PausedUntil, t.ApprovedAt
            }).ToListAsync()))
        .WithSummary("📋 القوالب المعتمدة");

        g.MapGet("/sessions", async (HybridDbContext db) => Results.Ok(
            await db.WaSessions.AsNoTracking().Select(s => new
            {
                s.Id, s.SessionId, s.Phone, s.Status, s.WarmupDay,
                s.DailyQuota, s.SentToday, s.RiskScore, s.ProxyLabel, s.LastSeenAt
            }).ToListAsync()))
        .WithSummary("📱 جلسات القناة غير الرسمية");

        // ═══════════════ خريطة أخطاء Meta ═══════════════
        g.MapGet("/error-map", () => Results.Ok(
            MetaErrorMap.All.Select(r => new
            {
                r.Code, r.Retryable, r.Fatal, r.RetryAfterMs,
                meaning = r.ArabicMeaning, r.Action
            })))
        .WithSummary("🗺️ خريطة أخطاء Meta والتصرّف الصح لكل واحد");

        g.MapGet("/alerts", (IAlerter alerter) => Results.Ok(
            alerter.Recent(40).Select(a => new { a.At, a.Severity, a.Message })))
        .WithSummary("🔔 التنبيهات الأخيرة");

        // ═══════════════ رسايل المزوّد الوهمي ═══════════════
        g.MapGet("/mock-outbox", (IServiceProvider sp) =>
        {
            var mocks = sp.GetServices<MockProvider>().ToList();
            if (mocks.Count == 0)
                return Results.Ok(new { note = "المزوّد الوهمي مش مستخدم (وضع live)", items = Array.Empty<object>() });

            var items = mocks.SelectMany(m => m.Sent)
                .OrderByDescending(m => m.At)
                .Take(80)
                .Select(m => new
                {
                    m.At, channel = m.Channel.ToString(), m.To, m.Intent,
                    m.TemplateName, m.Body,
                    windowState = m.WindowState.ToString(),
                    m.Cost, m.ProviderMessageId
                });

            return Results.Ok(new { note = "رسايل المزوّد الوهمي (مفيش إرسال حقيقي)", items });
        })
        .WithSummary("📤 صندوق الصادر الوهمي");
    }
}

/// <summary>مسارات التشغيل — مفتاح الطوارئ والمحاكاة</summary>
public static class OpsEndpoints
{
    public static void MapOpsEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/ops").WithTags("التشغيل");

        g.MapPost("/kill-switch/unofficial", async (bool killed, string? reason, IKillSwitch k) =>
        {
            await k.SetUnofficialAsync(killed, reason);
            return Results.Ok(new
            {
                ok = true,
                unofficialKilled = killed,
                message = killed
                    ? "🔴 القناة غير الرسمية اتوقفت — كل الترافيك هيحاول يروح للرسمي"
                    : "🟢 القناة غير الرسمية اتشغّلت"
            });
        })
        .WithSummary("🔴 مفتاح طوارئ القناة غير الرسمية");

        g.MapPost("/kill-switch/global", async (bool killed, string? reason, IKillSwitch k) =>
        {
            await k.SetGlobalAsync(killed, reason);
            return Results.Ok(new { ok = true, globalKilled = killed });
        })
        .WithSummary("🚨 مفتاح طوارئ عام (يوقف القناتين)");

        // محاكاة سقوط مزوّد — لعرض التدهور والـ fallback حياً
        g.MapPost("/simulate/provider", (string channel, bool down, bool degraded,
            IServiceProvider sp) =>
        {
            var mocks = sp.GetServices<MockProvider>().ToList();
            if (mocks.Count == 0)
                return Results.BadRequest(new { error = "المحاكاة متاحة في وضع mock بس" });

            if (!Enum.TryParse<ChannelKind>(channel, true, out var ch))
                return Results.BadRequest(new { error = "القناة لازم تكون Official أو Unofficial" });

            var m = mocks.First(x => x.Channel == ch);
            m.ForceDown = down;
            m.ForceDegraded = degraded;

            return Results.Ok(new
            {
                ok = true,
                channel = ch.ToString(),
                down, degraded,
                message = down
                    ? $"⚠️ القناة {ch} بقت واقعة — جرّب /api/send وشوف الـ fallback"
                    : $"🟢 القناة {ch} رجعت"
            });
        })
        .WithSummary("🧪 محاكاة سقوط/تدهور مزوّد (لعرض الـ fallback)");

        // محاكاة تغيّر الـ tier والجودة
        g.MapPost("/simulate/tier", async (string tier, string quality,
            Infrastructure.Core.TierStore store) =>
        {
            if (!Enum.TryParse<QualityRating>(quality, true, out var q))
                return Results.BadRequest(new { error = "الجودة لازم Green/Yellow/Red" });

            await store.RefreshFromMetaAsync(tier, TierLimits.For(tier), q);

            return Results.Ok(new
            {
                ok = true, tier, quality = q.ToString(),
                limit = TierLimits.For(tier),
                note = q == QualityRating.Red
                    ? "🔴 الجودة حمراء — التسويق اتوقف أوتوماتيك"
                    : "الحالة اتحدّثت"
            });
        })
        .WithSummary("🧪 محاكاة تغيّر الـ Tier والجودة");

        g.MapPost("/reset-demo", async (HybridDbContext db, IServiceProvider sp,
            ILoggerFactory lf) =>
        {
            // نمسح السجلات بس — العملاء والنوافذ فاضلين
            db.MessageLogs.RemoveRange(db.MessageLogs);
            await db.SaveChangesAsync();

            foreach (var m in sp.GetServices<MockProvider>()) m.Reset();

            return Results.Ok(new { ok = true, message = "🔄 سجل الرسايل اتصفّر" });
        })
        .WithSummary("🔄 تصفير العرض التوضيحي");
    }
}
