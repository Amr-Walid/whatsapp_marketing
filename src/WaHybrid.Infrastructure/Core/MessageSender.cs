using Microsoft.Extensions.Logging;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Entities;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Intents;
using WaHybrid.Infrastructure.Data;

namespace WaHybrid.Infrastructure.Core;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════
///  📤 نقطة الدخول **الوحيدة** للإرسال في النظام كله. docs/10 §4.
///
///  كل حاجة بتبعت رسالة — البوت، الحملة، تأكيد الأوردر، الـ webhook —
///  بتنده <c>SendAsync</c> وبس. مفيش حد بينده المزوّد مباشرة.
///
///  ليه ده مهم لدرجة إنه شرط في التصميم؟ لأن كل الحمايات (منع التكرار،
///  السقوف، الميزانية، السجل، التوجيه) موجودة هنا. أي مسار بيلفّ حول
///  الميثود دي = ثقب في كل الحمايات في نفس الوقت.
///
///  الخطوات الثمانية بالترتيب:
///   1️⃣ فحص الميزانية       — أوقف التسويق لو الفلوس خلصت (الحرج بيكمّل)
///   2️⃣ بناء مفتاح التكرار   — حتمي من محتوى النية
///   3️⃣ التوجيه + التدهور    — أنهي قناة وأنهي وضع
///   4️⃣ بناء الطلب          — قالب أو نص حر
///   5️⃣ البوابات            — السلسلة السبع
///   6️⃣ السجل **قبل** الإرسال 🔑
///   7️⃣ الإرسال الفعلي
///   8️⃣ تحديث السجل + العدّادات
/// ═══════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MessageSender : IMessageSender
{
    private readonly HybridDbContext _db;
    private readonly IChannelRouter _router;
    private readonly IProviderRegistry _providers;
    private readonly IWindowTracker _windows;
    private readonly ITemplateRegistry _templates;
    private readonly IGateChain _gates;
    private readonly ICostGuard _cost;
    private readonly ICostBook _costBook;
    private readonly IFrequencyCap _freq;
    private readonly IKillSwitch _kill;
    private readonly IAlerter _alerter;
    private readonly ILogger<MessageSender> _log;

    public MessageSender(HybridDbContext db, IChannelRouter router, IProviderRegistry providers,
        IWindowTracker windows, ITemplateRegistry templates, IGateChain gates, ICostGuard cost,
        ICostBook costBook, IFrequencyCap freq, IKillSwitch kill, IAlerter alerter,
        ILogger<MessageSender> log)
    {
        _db = db; _router = router; _providers = providers; _windows = windows;
        _templates = templates; _gates = gates; _cost = cost; _costBook = costBook;
        _freq = freq; _kill = kill; _alerter = alerter; _log = log;
    }

    public async Task<SendOutcome> SendAsync(SendIntent intent, CancellationToken ct = default)
    {
        var spec = IntentRegistry.Get(intent.Name);

        // ══════════════════════════════════════════════════════════════
        //  0️⃣ مفتاح الطوارئ العام
        // ══════════════════════════════════════════════════════════════
        if (await _kill.IsGlobalKilledAsync(ct))
            return Blocked("kill_switch_global", "🚨 الإرسال موقوف بالكامل بمفتاح الطوارئ");

        // ══════════════════════════════════════════════════════════════
        //  1️⃣ فحص الميزانية
        //  🔑 الإيقاف بيضرب التسويق بس — المعاملات الحرجة بتكمّل.
        //     إيقاف تأكيد أوردر عشان الميزانية = خسارة أكبر من الفاتورة.
        // ══════════════════════════════════════════════════════════════
        var budget = await _cost.CheckAsync(ct);
        if (budget.HardStop && !spec.Critical)
            return Blocked("cost_hard_stop",
                $"💰 حد الميزانية اتجاوز ({budget.Pct:F0}%) — التسويق موقوف، الحرج بس شغّال");

        // ══════════════════════════════════════════════════════════════
        //  2️⃣ مفتاح منع التكرار (حتمي)
        //  نفس (عميل + نية + حملة + يوم) = نفس المفتاح، مهما كانت القناة
        // ══════════════════════════════════════════════════════════════
        var idemKey = IdempotencyKeyFactory.Create(
            intent.CustomerId, intent.Name, intent.CampaignId,
            DateOnly.FromDateTime(DateTime.UtcNow));

        // ══════════════════════════════════════════════════════════════
        //  3️⃣ التوجيه + التدهور
        // ══════════════════════════════════════════════════════════════
        var routing = await _router.ResolveWithFallbackAsync(intent, maxHops: 2, ct);
        if (!routing.Ok)
        {
            _log.LogInformation("🚫 التوجيه رفض {Intent} لـ {Phone}: {Reason}",
                intent.Name, intent.Phone, routing.Reason);

            return new SendOutcome
            {
                Ok = false,
                Reason = routing.Reason,
                BlockedByGate = "router",
                Tried = routing.Tried
            };
        }

        var decision = routing.Decision!;
        var channel = decision.Channel!.Value;
        var mode = decision.Mode!.Value;
        var win = await _windows.GetStateAsync(intent.Phone, ct);

        // ══════════════════════════════════════════════════════════════
        //  4️⃣ بناء الطلب
        // ══════════════════════════════════════════════════════════════
        TemplatePayload? templatePayload = null;
        WaTemplate? templateRow = null;

        if (mode == SendMode.Template)
        {
            templateRow = await _templates.ForIntentAsync(intent.Name, "ar", ct);
            if (templateRow is null)
                return Blocked("no_template", $"مفيش قالب معتمد للنية {intent.Name}");

            templatePayload = _templates.Build(templateRow, intent.TemplateParams);
        }

        var request = new SendRequest
        {
            To = intent.Phone,
            Type = intent.Type,
            Body = intent.Body,
            MediaUrl = intent.MediaUrl,
            Template = templatePayload,
            IdempotencyKey = idemKey,
            Meta = new SendRequestMeta
            {
                CustomerId = intent.CustomerId,
                IntentName = intent.Name,
                CampaignId = intent.CampaignId,
                Segment = intent.Segment,
                WindowOpen = win.FreeFormAllowed,
                WindowState = win.State
            }
        };

        // ══════════════════════════════════════════════════════════════
        //  5️⃣ البوابات
        // ══════════════════════════════════════════════════════════════
        var gateCtx = new GateContext
        {
            Phone = intent.Phone,
            CustomerId = intent.CustomerId,
            IntentName = intent.Name,
            IdempotencyKey = idemKey,
            Channel = channel,
            Mode = mode,
            Template = templatePayload,
            CampaignId = intent.CampaignId,
            Segment = intent.Segment
        };

        var verdict = await _gates.EvaluateAsync(gateCtx, ct);

        // 🔄 البوابة اقترحت التحويل لقالب (رسالة حرة والنافذة قفلت في اللحظة الأخيرة)
        if (!verdict.Passed && verdict.SwitchTo == SendMode.Template && mode == SendMode.Free)
        {
            templateRow = await _templates.ForIntentAsync(intent.Name, "ar", ct);
            if (templateRow is not null)
            {
                templatePayload = _templates.Build(templateRow, intent.TemplateParams);
                mode = SendMode.Template;
                request = CloneWithTemplate(request, templatePayload);
                gateCtx = CloneCtx(gateCtx, mode, templatePayload);

                // ⚠️ إعادة تقييم — بس بنتخطّى بوابة منع التكرار لأنها استهلكت المفتاح
                verdict = await ReEvaluateSkippingDedupeAsync(gateCtx, ct);
                _log.LogInformation("🔄 اتحوّل لوضع القالب بعد اقتراح gWindow");
            }
        }

        if (!verdict.Passed)
        {
            await LogBlockedAsync(intent, channel, mode, win.State, spec, idemKey, verdict, ct);

            return new SendOutcome
            {
                Ok = false,
                Channel = channel,
                Mode = mode,
                WindowState = win.State,
                RouteReason = decision.Reason,
                Reason = verdict.Reason,
                BlockedByGate = verdict.Gate,
                Deduped = verdict.Gate == "gCrossChannelDedupe",
                Fatal = verdict.Drop,
                Tried = routing.Tried
            };
        }

        // ══════════════════════════════════════════════════════════════
        //  6️⃣ 🔑 السجل **قبل** الإرسال
        //
        //  ليه؟ لو النظام وقع بعد الإرسال وقبل السجل، الرسالة اتبعتت
        //  ومحدّش يعرف — بندفع فلوس على حاجة مش مسجّلة، ولو أعدنا
        //  المحاولة العميل ياخدها مرتين.
        //  بالسجل الأول: أسوأ حالة صف status='sending' معلّق —
        //  ومهمة تنظيف بتشوفه وتحقّق منه. أهون بمراحل.
        // ══════════════════════════════════════════════════════════════
        var estimated = mode == SendMode.Template && win.State != WindowState.FepOpen
            ? _costBook.Price(intent.Phone, templateRow!.Category)
            : 0m;

        var logRow = new MessageLog
        {
            CampaignId = intent.CampaignId,
            CustomerId = intent.CustomerId,
            Phone = intent.Phone,
            Direction = MessageDirection.Out,
            Channel = channel,
            Intent = intent.Name,
            WindowState = win.State,
            SendMode = mode,
            TemplateName = templateRow?.Name,
            MetaCategory = templateRow?.Category ?? spec.MetaCategory,
            IdempotencyKey = idemKey,
            Content = Truncate(templatePayload is null ? intent.Body : templateRow!.BodyText, 900),
            CostEstimated = estimated,
            RouteReason = decision.Reason,
            FallbackFrom = decision.FallbackFrom,
            Status = MessageStatus.Sending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.MessageLogs.Add(logRow);
        await _db.SaveChangesAsync(ct);

        // ══════════════════════════════════════════════════════════════
        //  7️⃣ الإرسال الفعلي
        // ══════════════════════════════════════════════════════════════
        var provider = _providers.Get(channel);
        var result = await provider.SendAsync(request, ct);

        // ══════════════════════════════════════════════════════════════
        //  8️⃣ تحديث السجل + العدّادات
        // ══════════════════════════════════════════════════════════════
        if (result.Ok)
        {
            logRow.Status = MessageStatus.Sent;
            logRow.SentAt = DateTimeOffset.UtcNow;
            logRow.WaMessageId = result.ProviderMessageId;
            logRow.SessionId = result.SessionId;
            logRow.DelayUsedMs = result.DelayUsedMs;
            if (result.EstimatedCostUsd > 0) logRow.CostEstimated = result.EstimatedCostUsd;

            // عدّاد التسويق — بيحسب القناتين
            if (spec.Class == IntentClass.Marketing)
                await _freq.RecordAsync(intent.Phone, channel, ct);

            // آخر قناة استخدمناها — بيفيد في اتساق المحادثة
            var customer = await _db.Customers.FindAsync([intent.CustomerId], ct);
            if (customer is not null) customer.LastChannelUsed = channel;
        }
        else
        {
            logRow.Status = MessageStatus.Failed;
            logRow.ErrorCode = result.ErrorCode;
            logRow.ErrorMessage = Truncate(result.Reason, 400);

            // 🔴 فشل نهائي (الرقم مش على واتساب) → قائمة الحظر فوراً
            if (result.Fatal)
                await AddSuppressionAsync(intent.Phone, result.ErrorCode ?? "fatal", channel, ct);
        }

        await _db.SaveChangesAsync(ct);

        if (!result.Ok && result.ErrorCode is "190" or "quality_red")
            await _alerter.SendAsync("critical",
                $"🚨 خطأ حسّاس على القناة {channel}: {result.ErrorCode} — {result.Reason}", ct);

        return new SendOutcome
        {
            Ok = result.Ok,
            LogId = logRow.Id,
            Channel = channel,
            Mode = mode,
            WindowState = win.State,
            RouteReason = decision.Reason,
            Reason = result.Reason,
            ErrorCode = result.ErrorCode,
            EstimatedCostUsd = logRow.CostEstimated,
            ProviderMessageId = result.ProviderMessageId,
            Fatal = result.Fatal,
            Retryable = result.Retryable,
            Tried = routing.Tried
        };
    }

    // ══════════════════════════════════════════════════════════════════
    //  مساعدات
    // ══════════════════════════════════════════════════════════════════

    private async Task<GateVerdict> ReEvaluateSkippingDedupeAsync(GateContext ctx, CancellationToken ct)
    {
        if (_gates is not Gates.GateChain chain) return await _gates.EvaluateAsync(ctx, ct);

        foreach (var gate in chain.Gates)
        {
            if (gate is Gates.CrossChannelDedupeGate) continue;
            var v = await gate.EvaluateAsync(ctx, ct);
            if (!v.Passed) return v;
        }
        return GateVerdict.Pass();
    }

    private static SendRequest CloneWithTemplate(SendRequest r, TemplatePayload t) => new()
    {
        To = r.To, Type = r.Type, Body = r.Body, MediaUrl = r.MediaUrl,
        Template = t, IdempotencyKey = r.IdempotencyKey, Meta = r.Meta
    };

    private static GateContext CloneCtx(GateContext c, SendMode mode, TemplatePayload t) => new()
    {
        Phone = c.Phone, CustomerId = c.CustomerId, IntentName = c.IntentName,
        IdempotencyKey = c.IdempotencyKey, Channel = c.Channel, Mode = mode,
        Template = t, CampaignId = c.CampaignId, Segment = c.Segment
    };

    /// <summary>
    /// 🔑 حتى المرفوض بيتسجّل — بحالة Blocked/Skipped.
    /// بدون ده، الداشبورد هيقول "بعتنا ١٠٠ رسالة" وهو بعت ١٠٠٠ ورفض ٩٠٠،
    /// وإنت مش عارف ليه. السجل الكامل هو اللي بيخلّي التحسين ممكن.
    /// </summary>
    private async Task LogBlockedAsync(SendIntent intent, ChannelKind channel, SendMode mode,
        WindowState state, IntentSpec spec, string idemKey, GateVerdict v, CancellationToken ct)
    {
        // ⚠️ الـ idempotencyKey فيه UNIQUE index — والمرفوض بسبب التكرار
        //    معناه فيه صف موجود بنفس المفتاح. فبنسيبه null هنا.
        _db.MessageLogs.Add(new MessageLog
        {
            CampaignId = intent.CampaignId,
            CustomerId = intent.CustomerId,
            Phone = intent.Phone,
            Direction = MessageDirection.Out,
            Channel = channel,
            Intent = intent.Name,
            WindowState = state,
            SendMode = mode,
            MetaCategory = spec.MetaCategory,
            IdempotencyKey = null,
            Status = v.Drop ? MessageStatus.Blocked : MessageStatus.Skipped,
            ErrorCode = v.Gate,
            ErrorMessage = Truncate(v.Reason, 400),
            RouteReason = $"blocked_by:{v.Gate}",
            CostEstimated = 0m,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task AddSuppressionAsync(string phone, string reason, ChannelKind channel,
        CancellationToken ct)
    {
        var exists = await _db.SuppressionList.AnyAsync(s => s.Phone == phone, ct);
        if (exists) return;

        _db.SuppressionList.Add(new SuppressionEntry
        {
            Phone = phone,
            Reason = reason == "131026" ? "invalid" : reason,
            SeenOnChannel = channel
        });

        _log.LogWarning("🚫 {Phone} اتضاف لقائمة الحظر — السبب: {Reason}", phone, reason);
    }

    private static SendOutcome Blocked(string gate, string reason) => new()
    {
        Ok = false, BlockedByGate = gate, Reason = reason
    };

    private static string? Truncate(string? s, int max)
        => s is null ? null : s.Length <= max ? s : s[..max];
}

file static class DbExtensions
{
    public static Task<bool> AnyAsync<T>(this Microsoft.EntityFrameworkCore.DbSet<T> set,
        System.Linq.Expressions.Expression<Func<T, bool>> pred, CancellationToken ct) where T : class
        => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(set, pred, ct);
}
