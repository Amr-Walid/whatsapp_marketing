using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Intents;
using WaHybrid.Infrastructure.Data;
using WaHybrid.Infrastructure.Options;

namespace WaHybrid.Infrastructure.Gates;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════
///  البوابات الهجينة السبع. docs/09 §5.
///
///  🔑 مبدأ الترتيب: **الأرخص والأقطع أولاً.**
///  مفيش أي معنى إننا نروح نسأل Meta عن حالة الـ tier (استدعاء شبكة)
///  لعميل اسمه في قائمة الحظر (استعلام محلي رخيص). الترتيب بيوفّر
///  استدعاءات وفلوس ووقت.
///
///  الترتيب النهائي:
///   10 → gSuppression        قائمة الحظر (أعلى أسبقية مطلقة)
///   20 → gConsent            الموافقة القانونية
///   30 → gCrossChannelDedupe منع التكرار بين القناتين 🔑
///   40 → gGlobalFrequency    سقفنا الموحّد للتسويق
///   50 → gWindow             تطابق النافذة مع الوضع
///   60 → gMetaFrequencyCap   سقف Meta المتوقّع (131049)
///   70 → gMessagingTier      الحد اليومي
///   80 → gTemplateReady      القالب معتمد وصالح
/// ═══════════════════════════════════════════════════════════════════
/// </summary>
public static class GateOrder
{
    public const int Suppression = 10;
    public const int Consent = 20;
    public const int CrossChannelDedupe = 30;
    public const int GlobalFrequency = 40;
    public const int Window = 50;
    public const int MetaFrequencyCap = 60;
    public const int MessagingTier = 70;
    public const int TemplateReady = 80;
}

// ══════════════════════════════════════════════════════════════════════
//  ١٠ — قائمة الحظر
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// أعلى أسبقية مطلقة، ومفيش استثناءات — ولا حتى للمعاملات الحرجة.
///
/// ليه؟ لأن العميل اللي عمل شكوى أو قال "بلاش" لو استلم أي حاجة تاني،
/// هيعمل "Report" — و ٣–٥ شكاوى بتقتل الرقم (README). التكلفة القانونية
/// والسمعية أكبر من أي أوردر.
/// </summary>
public sealed class SuppressionGate : IGate
{
    private readonly HybridDbContext _db;
    public SuppressionGate(HybridDbContext db) => _db = db;

    public string Name => "gSuppression";
    public int Order => GateOrder.Suppression;

    public async Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default)
    {
        var entry = await _db.SuppressionList.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Phone == ctx.Phone, ct);

        return entry is null
            ? GateVerdict.Pass()
            // drop: true — اسقطها نهائياً، متعيدش المحاولة أبداً
            : GateVerdict.Block(Name, $"الرقم في قائمة الحظر (السبب: {entry.Reason})", drop: true);
    }
}

// ══════════════════════════════════════════════════════════════════════
//  ٢٠ — الموافقة القانونية
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// الموافقة مطلوبة للتسويق. المعاملات والمحادثات معفيّة —
/// لأن العميل هو اللي بادر بالأوردر أو بالرسالة (موافقة ضمنية سياقية).
///
/// ⚠️ من docs/07: <c>OptedOut</c> واحد بيمشي على القناتين. لو عملتها
/// منفصلة، العميل اللي عمل opt-out على الرسمي هيستلم على غير الرسمي
/// ويعمل شكوى — والشكوى بتضرب الرقمين.
/// </summary>
public sealed class ConsentGate : IGate
{
    private readonly HybridDbContext _db;
    public ConsentGate(HybridDbContext db) => _db = db;

    public string Name => "gConsent";
    public int Order => GateOrder.Consent;

    public async Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default)
    {
        var c = await _db.Customers.AsNoTracking()
            .Where(x => x.Phone == ctx.Phone)
            .Select(x => new { x.OptedIn, x.OptedOut })
            .FirstOrDefaultAsync(ct);

        if (c is null)
            return GateVerdict.Block(Name, "العميل مش موجود في قاعدة البيانات", drop: true);

        // 🔴 opt-out بيمشي على كل حاجة ما عدا تأكيد الإلغاء نفسه
        if (c.OptedOut && ctx.IntentName != IntentNames.OptOutAck)
            return GateVerdict.Block(Name, "العميل عمل opt-out — ممنوع على القناتين", drop: true);

        var spec = IntentRegistry.Get(ctx.IntentName);
        if (spec.Class == IntentClass.Marketing && !c.OptedIn)
            return GateVerdict.Block(Name, "مفيش موافقة صريحة (opt-in) للتسويق", drop: true);

        return GateVerdict.Pass();
    }
}

// ══════════════════════════════════════════════════════════════════════
//  ٣٠ — 🔑 منع التكرار بين القناتين (أهم بوابة في النظام الهجين)
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// 🔑 البوابة اللي **مش موجودة** في أي نظام أحادي القناة — ودي بالظبط
/// اللي بتفرّق نظام هجين محترم عن كارثة.
///
/// السيناريو الكارثي بالتفصيل:
///   ١. البوت بيبعت "أوردرك اتأكّد" على الرسمي
///   ٢. الرسمي بيعمل timeout بعد ٣٠ ثانية (الرسالة **فعلاً وصلت**)
///   ٣. الـ Router بيقول "الرسمي واقع، النية حرجة → حوّل لغير الرسمي"
///   ٤. العميل بياخد نفس الرسالة مرتين من رقمين مختلفين
///   ٥. العميل: "إيه ده؟ نصب؟" → شكوى → الرقم بيموت
///
/// الحل: مفتاح حتمي من محتوى النية نفسها
/// <c>SHA256(customerId|intent|campaignId|dayBucket)</c> — يعني نفس
/// النية لنفس العميل في نفس اليوم = نفس المفتاح بالظبط، مهما كانت القناة.
/// وبنستخدم <c>SET NX EX</c> (عملية ذرّية) عشان يبقى مستحيل اتنين
/// يمرّوا في نفس اللحظة.
///
/// TTL = ٤٨ ساعة: يغطّي أطول محاولة إعادة معقولة.
/// </summary>
public sealed class CrossChannelDedupeGate : IGate
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    private readonly ICacheStore _cache;
    private readonly ILogger<CrossChannelDedupeGate> _log;

    public CrossChannelDedupeGate(ICacheStore cache, ILogger<CrossChannelDedupeGate> log)
        => (_cache, _log) = (cache, log);

    public string Name => "gCrossChannelDedupe";
    public int Order => GateOrder.CrossChannelDedupe;

    public async Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default)
    {
        var key = $"idem:{ctx.IdempotencyKey}";

        // ⚛️ ذرّية: بيرجع true لو المفتاح كان مش موجود (يعني إحنا الأولانيين)
        var acquired = await _cache.SetIfNotExistsAsync(key, ctx.Channel?.ToString() ?? "?", Ttl, ct);

        if (acquired) return GateVerdict.Pass();

        var owner = await _cache.GetAsync(key, ct);
        _log.LogWarning(
            "🔁 منع تكرار: النية {Intent} للعميل {CustomerId} اتبعتت قبل كده على {Owner}",
            ctx.IntentName, ctx.CustomerId, owner);

        return GateVerdict.Block(Name,
            $"اتبعتت قبل كده (على {owner}) — منع تكرار بين القناتين", drop: true);
    }
}

// ══════════════════════════════════════════════════════════════════════
//  ٤٠ — سقفنا الموحّد للتسويق
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// 🔑 سقفنا **أشدّ** من سقف Meta، وده مقصود.
///
/// Meta بتسمح بـ ~٢ رسالة تسويقية في ٢٤ ساعة. إحنا بنسمح بواحدة.
/// ليه نضيّق على نفسنا؟ لأن سقف Meta بيحمي Meta، وسقفنا بيحمي **علاقتنا
/// بالعميل**. العميل اللي بياخد رسالتين تسويق في يوم بيحس بالإزعاج
/// حتى لو Meta سمحت.
///
/// والأهم: العدّاد ده بيحسب **القناتين مع بعض**. لأن العميل مش فارق عنده
/// إحنا بعتنا من أنهي رقم — هو بيشوف واتساب واحد.
/// </summary>
public sealed class GlobalFrequencyGate : IGate
{
    private readonly IFrequencyCap _freq;
    private readonly PolicyOptions _policy;

    public GlobalFrequencyGate(IFrequencyCap freq, IOptions<HybridOptions> opt)
        => (_freq, _policy) = (freq, opt.Value.Policy);

    public string Name => "gGlobalFrequency";
    public int Order => GateOrder.GlobalFrequency;

    public async Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default)
    {
        var spec = IntentRegistry.Get(ctx.IntentName);
        if (spec.Class != IntentClass.Marketing) return GateVerdict.Pass();

        var used = await _freq.GetGlobalMarketingCountAsync(ctx.Phone, ct);
        if (used < _policy.MarketingPerCustomerPer24h) return GateVerdict.Pass();

        return GateVerdict.Block(Name,
            $"سقفنا الموحّد للتسويق ({used}/{_policy.MarketingPerCustomerPer24h} خلال ٢٤س)",
            retryAt: DateTimeOffset.UtcNow.AddHours(24));
    }
}

// ══════════════════════════════════════════════════════════════════════
//  ٥٠ — تطابق النافذة مع الوضع
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// حزام أمان ضد أخطاء الـ Router.
///
/// لو الـ Router قرر "رسالة حرة على الرسمي" والنافذة مقفولة، Meta هترجّع
/// 131047. البوابة دي بتمسكها **قبل** الاستدعاء — فبنوفّر استدعاء API،
/// وبنوفّر ضربة في إحصائيات الفشل عند Meta، والأهم بنسجّل إن فيه bug
/// في <c>WindowTracker</c> محتاج تحقيق.
/// </summary>
public sealed class WindowGate : IGate
{
    private readonly IWindowTracker _windows;
    private readonly ILogger<WindowGate> _log;

    public WindowGate(IWindowTracker windows, ILogger<WindowGate> log)
        => (_windows, _log) = (windows, log);

    public string Name => "gWindow";
    public int Order => GateOrder.Window;

    public async Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default)
    {
        // القوالب شغّالة في أي وقت — مش محتاجة نافذة
        if (ctx.Mode == SendMode.Template) return GateVerdict.Pass();

        var win = await _windows.GetStateAsync(ctx.Phone, ct);
        if (win.FreeFormAllowed) return GateVerdict.Pass();

        // رسالة حرة + مفيش نافذة
        if (ctx.Channel == ChannelKind.Official)
        {
            _log.LogError(
                "🐞 الـ Router قرر رسالة حرة على الرسمي والنافذة مقفولة — فيه bug في WindowTracker! ({Phone})",
                ctx.Phone);

            // 🔑 مش بنسقطها — بنقترح التحويل لقالب
            return GateVerdict.Block(Name,
                "رسالة حرة بره النافذة على الرسمي (131047) — حوّل لقالب",
                switchTo: SendMode.Template);
        }

        // غير الرسمي: تقنياً بيقدر يبعت بره النافذة، بس ده تسويق بارد = خطر
        var spec = IntentRegistry.Get(ctx.IntentName);
        if (spec.Class is IntentClass.Marketing)
            return GateVerdict.Block(Name,
                "🔴 تسويق بارد على غير الرسمي بره النافذة = خطر حظر عالي", drop: true);

        return GateVerdict.Pass();
    }
}

// ══════════════════════════════════════════════════════════════════════
//  ٦٠ — سقف Meta المتوقّع (131049)
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// تقدير محافظ لسقف Meta. docs/08 §3.
///
/// ⚠️ حقيقة مهمة لازم المدير يفهمها: السقف ده **على العميل**، مش على
/// الراسل. يعني لو العميل خد رسايل تسويقية من ٣ شركات تانية النهاردة،
/// رسالتنا هتترفض بـ 131049 مهما عملنا. ومش بنقدر نلفّ عليه:
///   ❌ رقم تاني → نفس السقف
///   ❌ BSP تاني → نفس السقف
///   ❌ WABA تاني → نفس السقف
/// الحل الوحيد: نبعت أقل ونبعت أحسن.
/// </summary>
public sealed class MetaFrequencyCapGate : IGate
{
    private readonly IFrequencyCap _freq;
    private readonly PolicyOptions _policy;

    public MetaFrequencyCapGate(IFrequencyCap freq, IOptions<HybridOptions> opt)
        => (_freq, _policy) = (freq, opt.Value.Policy);

    public string Name => "gMetaFrequencyCap";
    public int Order => GateOrder.MetaFrequencyCap;

    public async Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default)
    {
        if (ctx.Channel != ChannelKind.Official) return GateVerdict.Pass();

        var spec = IntentRegistry.Get(ctx.IntentName);
        if (spec.MetaCategory != MetaCategory.Marketing) return GateVerdict.Pass();

        var used = await _freq.GetMetaMarketingCountAsync(ctx.Phone, ct);
        if (used < _policy.MetaMarketingCapAssumed) return GateVerdict.Pass();

        return GateVerdict.Block(Name,
            $"سقف Meta المتوقّع 131049 ({used}/{_policy.MetaMarketingCapAssumed}) — أجّل لبكرة",
            retryAt: new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero));
    }
}

// ══════════════════════════════════════════════════════════════════════
//  ٧٠ — الحد اليومي (Messaging Tier)
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// الحد اليومي + هامش أمان ٥٪ + حجز ١٠٪ للمعاملات الحرجة.
///
/// 🔑 الحجز ده هو الفرق بين نظام محترم ونظام بيفشل في أسوأ وقت:
/// لو حملة تسويقية استهلكت الحد اليومي كله الساعة ١١ الصبح، كل تأكيدات
/// الأوردرات لباقي اليوم هتفشل. الحجز بيمنع ده.
/// </summary>
public sealed class MessagingTierGate : IGate
{
    private readonly ITierStore _tier;
    private readonly TierOptions _opt;
    private readonly ILogger<MessagingTierGate> _log;

    public MessagingTierGate(ITierStore tier, IOptions<HybridOptions> opt,
        ILogger<MessagingTierGate> log)
        => (_tier, _opt, _log) = (tier, opt.Value.Tier, log);

    public string Name => "gMessagingTier";
    public int Order => GateOrder.MessagingTier;

    public async Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default)
    {
        if (ctx.Channel != ChannelKind.Official) return GateVerdict.Pass();

        var snap = await _tier.CurrentAsync(ct);
        var usable = (int)(snap.Limit * _opt.SafetyMargin);
        var spec = IntentRegistry.Get(ctx.IntentName);
        var nextMidnightUtc = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero);

        // المعاملات الحرجة بتستخدم الحد كامل (بالهامش)
        if (spec.Critical)
            return snap.UsedToday < usable
                ? GateVerdict.Pass()
                : GateVerdict.Block(Name,
                    $"الحد اليومي خلص تماماً ({snap.UsedToday}/{usable}) — حتى للحرج",
                    retryAt: nextMidnightUtc);

        // الباقي بيوقف عند سقف أقل — الفرق محجوز للحرج
        var ceiling = (int)(usable * (1 - _opt.ReserveForCritical));
        if (snap.UsedToday >= ceiling)
        {
            _log.LogInformation(
                "🛑 gMessagingTier وقّف {Intent}: {Used}/{Ceiling} (الباقي محجوز للحرج)",
                ctx.IntentName, snap.UsedToday, ceiling);

            return GateVerdict.Block(Name,
                $"حصة غير الحرج خلصت ({snap.UsedToday}/{ceiling}) — الباقي محجوز للمعاملات الحرجة",
                retryAt: nextMidnightUtc);
        }

        // 🔴 الجودة حمراء → التسويق موقوف
        if (spec.Class == IntentClass.Marketing
            && (snap.Quality == QualityRating.Red || snap.MarketingPaused))
            return GateVerdict.Block(Name,
                "🔴 جودة الرقم حمراء — التسويق موقوف أوتوماتيك");

        return GateVerdict.Pass();
    }
}

// ══════════════════════════════════════════════════════════════════════
//  ٨٠ — القالب معتمد وصالح
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// آخر بوابة — بتتأكد إن القالب اللي هنبعته:
///   • معتمد من Meta (مش pending ولا مرفوض)
///   • مش موقوف بسبب جودة حمراء
///   • عدد متغيراته مطابق للمعتمد (وإلا Meta بترجّع 132000)
/// </summary>
public sealed class TemplateReadyGate : IGate
{
    private readonly ITemplateRegistry _templates;
    public TemplateReadyGate(ITemplateRegistry templates) => _templates = templates;

    public string Name => "gTemplateReady";
    public int Order => GateOrder.TemplateReady;

    public async Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default)
    {
        if (ctx.Mode != SendMode.Template) return GateVerdict.Pass();

        if (ctx.Template is null)
            return GateVerdict.Block(Name, "وضع القالب بدون payload — bug في MessageSender", drop: true);

        var t = await _templates.GetAsync(ctx.Template.Name, ct);
        if (t is null)
            return GateVerdict.Block(Name, $"القالب {ctx.Template.Name} مش موجود في السجل", drop: true);

        if (!t.IsUsable(DateTimeOffset.UtcNow))
            return GateVerdict.Block(Name,
                $"القالب {t.Name} غير صالح (الحالة: {t.Status}، الجودة: {t.Quality}، موقوف لحد: {t.PausedUntil:u})");

        var expected = Core.TemplateRegistry.ParseParams(t.RequiredParamsJson).Count;
        if (ctx.Template.Parameters.Count != expected)
            return GateVerdict.Block(Name,
                $"عدد المتغيرات غلط ({ctx.Template.Parameters.Count} بدل {expected}) — Meta هترجّع 132000",
                drop: true);

        return GateVerdict.Pass();
    }
}

// ══════════════════════════════════════════════════════════════════════
//  السلسلة
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// منفّذ السلسلة — أول بوابة ترفض بتوقف الباقي (short-circuit).
/// و<c>TraceAsync</c> بتشغّل الكل عشان الداشبورد يشوف الصورة كاملة.
/// </summary>
public sealed class GateChain : IGateChain
{
    private readonly IReadOnlyList<IGate> _gates;
    private readonly ILogger<GateChain> _log;

    public GateChain(IEnumerable<IGate> gates, ILogger<GateChain> log)
    {
        _gates = gates.OrderBy(g => g.Order).ToList();
        _log = log;
    }

    public IReadOnlyList<IGate> Gates => _gates;

    public async Task<GateVerdict> EvaluateAsync(GateContext ctx, CancellationToken ct = default)
    {
        foreach (var gate in _gates)
        {
            var v = await gate.EvaluateAsync(ctx, ct);
            if (!v.Passed)
            {
                _log.LogInformation("🚧 {Gate} رفض: {Reason}", gate.Name, v.Reason);
                return v;
            }
        }
        return GateVerdict.Pass();
    }

    /// <summary>
    /// تشخيصي: بيشغّل **كل** البوابات ويرجّع نتيجة كل واحدة.
    ///
    /// ⚠️ تحذير: <c>gCrossChannelDedupe</c> بيكتب في الكاش (SET NX)، فالتشخيص
    /// هيحرق المفتاح. عشان كده بنستثنيه في وضع التشخيص وبنعرضه كـ "متخطّى".
    /// </summary>
    public async Task<IReadOnlyList<GateTrace>> TraceAsync(GateContext ctx, CancellationToken ct = default)
    {
        var result = new List<GateTrace>();

        foreach (var gate in _gates)
        {
            if (gate is CrossChannelDedupeGate)
            {
                result.Add(new GateTrace(gate.Name, gate.Order, true,
                    "متخطّى في التشخيص (بيكتب في الكاش)"));
                continue;
            }

            try
            {
                var v = await gate.EvaluateAsync(ctx, ct);
                result.Add(new GateTrace(gate.Name, gate.Order, v.Passed, v.Reason));
            }
            catch (Exception ex)
            {
                result.Add(new GateTrace(gate.Name, gate.Order, false, $"خطأ: {ex.Message}"));
            }
        }

        return result;
    }
}
