using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Intents;
using WaHybrid.Domain.Windows;
using WaHybrid.Infrastructure.Data;
using WaHybrid.Infrastructure.Options;

namespace WaHybrid.Infrastructure.Routing;

/// <summary>
/// 🧠 مُوجّه القناة — دماغ النظام الهجين. docs/09 §4.
///
/// ═══════════════════════════════════════════════════════════════════
///  القاعدة الحديدية (docs/09 §0):
///  مفيش كود فوق طبقة المزوّد يعرف إحنا بنستخدم أنهي قناة.
///  لو لقيت `if (channel == "official")` في البوت → التصميم مكسور.
///  الملف ده هو **المكان الوحيد** اللي بياخد القرار ده في النظام كله.
/// ═══════════════════════════════════════════════════════════════════
///
///  مصفوفة القرار (docs/09 §4.2):
///
///  النية            │ FEP مفتوحة      │ CSW مفتوحة       │ مفيش نافذة
///  ─────────────────┼─────────────────┼──────────────────┼──────────────────
///  تسويق            │ رسمي / حر 🎁    │ غير رسمي / حر    │ رسمي / قالب 💰
///  معاملات (حرج)    │ رسمي / حر       │ رسمي / حر        │ رسمي / قالب
///  معاملات (عادي)   │ رسمي / حر       │ غير رسمي / حر    │ رسمي / قالب
///  محادثة           │ رسمي / حر*      │ غير رسمي / حر    │ 🚫 مرفوض
///
///  * لو فاضل من FEP أكتر من ساعتين (قاعدة التسليم §4.4)
///
///  ليه كده بالظبط؟
///   • FEP = كل حاجة مجاناً على الرسمي → استغلّها لآخرها، خصوصاً التسويق
///   • CSW = المحادثة مفتوحة → غير الرسمي مجاني ومرن، والخطر واطي
///     (العميل هو اللي بادر، فمفيش شكوى "مين ده")
///   • مفيش نافذة = القوالب الرسمية بس. غير الرسمي هنا = تسويق بارد = حظر
/// </summary>
public sealed class ChannelRouter : IChannelRouter
{
    private readonly IWindowTracker _windows;
    private readonly IProviderRegistry _providers;
    private readonly ITemplateRegistry _templates;
    private readonly HybridDbContext _db;
    private readonly ChannelsOptions _channels;
    private readonly PolicyOptions _policy;
    private readonly ILogger<ChannelRouter> _log;

    public ChannelRouter(IWindowTracker windows, IProviderRegistry providers,
        ITemplateRegistry templates, HybridDbContext db,
        IOptions<HybridOptions> opt, ILogger<ChannelRouter> log)
    {
        _windows = windows;
        _providers = providers;
        _templates = templates;
        _db = db;
        _channels = opt.Value.Channels;
        _policy = opt.Value.Policy;
        _log = log;
    }

    // ══════════════════════════════════════════════════════════════════
    //  RouteAsync — القرار الأساسي، ٧ قواعد بالترتيب
    // ══════════════════════════════════════════════════════════════════
    public async Task<RouteDecision> RouteAsync(SendIntent intent, CancellationToken ct = default)
    {
        var spec = IntentRegistry.Get(intent.Name);
        var win = await _windows.GetStateAsync(intent.Phone, ct);
        return await DecideAsync(spec, win, intent, ct);
    }

    /// <summary>
    /// المنطق الصافي — منفصل عشان يبقى قابل للاختبار بدون قاعدة بيانات
    /// (اختبار المصفوفة الـ ١٢ حالة بينده الميثود دي مباشرة).
    /// </summary>
    public async Task<RouteDecision> DecideAsync(IntentSpec spec, CustomerWindowState win,
        SendIntent intent, CancellationToken ct = default)
    {
        var officialOn = _channels.OfficialEnabled;
        var unofficialOn = _channels.UnofficialEnabled;

        // ────────────────────────────────────────────────────────────
        // قاعدة ١: تفضيل العميل الصريح يكسب — لو ممكن
        // العميل قال "كلّمني على الرقم الرسمي بس"؟ نحترم ده.
        // ────────────────────────────────────────────────────────────
        var customerPref = await _db.Customers
            .Where(c => c.Phone == intent.Phone)
            .Select(c => c.PreferredChannel)
            .FirstOrDefaultAsync(ct);

        // ────────────────────────────────────────────────────────────
        // قاعدة ٢: 🎁 نافذة FEP مفتوحة = كل حاجة مجاناً على الرسمي
        // دي أحسن حالة ممكنة في النظام كله. حتى قوالب التسويق مجانية.
        // ────────────────────────────────────────────────────────────
        if (win.State == WindowState.FepOpen && officialOn)
        {
            // قاعدة ٢أ (§4.4): المحادثات جوه FEP تفضل على الرسمي
            // بس بشرط يفضل وقت كفاية — لو فاضل نص ساعة، منفعش نبدأ
            // محادثة هتتقطع في نصها.
            if (spec.Class == IntentClass.Conversational)
            {
                if (_policy.KeepFepConversationsOfficial
                    && win.FepHoursLeft >= _policy.FepMinHoursToKeepConversation)
                    return RouteDecision.Pick(ChannelKind.Official, SendMode.Free,
                        $"fep_open_conversation_free (فاضل {win.FepHoursLeft:F1}س)");

                // فاضل وقت قليل → كمّل على غير الرسمي
                if (unofficialOn)
                    return RouteDecision.Pick(ChannelKind.Unofficial, SendMode.Free,
                        $"fep_expiring_handoff_to_unofficial (فاضل {win.FepHoursLeft:F1}س)");
            }

            return RouteDecision.Pick(ChannelKind.Official, SendMode.Free,
                "fep_open_all_free 🎁");
        }

        // ────────────────────────────────────────────────────────────
        // قاعدة ٣: 🟡 نافذة CSW مفتوحة
        // العميل كلّمنا → الرسالة الحرة مسموحة على القناتين.
        // القرار هنا اقتصادي بحت:
        //   • حرج؟ → رسمي (الموثوقية أهم من التكلفة)
        //   • غير حرج؟ → غير رسمي (مجاني، والخطر واطي لأن العميل بادر)
        // ────────────────────────────────────────────────────────────
        if (win.State == WindowState.CswOpen)
        {
            // 🔑 الحرج دايماً رسمي — تأكيد أوردر لازم يوصل، نقطة.
            if (spec.Critical && officialOn)
                return RouteDecision.Pick(ChannelKind.Official, SendMode.Free,
                    "csw_open_critical_official");

            if (customerPref == ChannelKind.Official && officialOn)
                return RouteDecision.Pick(ChannelKind.Official, SendMode.Free,
                    "csw_open_customer_prefers_official");

            if (unofficialOn)
                return RouteDecision.Pick(ChannelKind.Unofficial, SendMode.Free,
                    "csw_open_free_via_unofficial 💰");

            if (officialOn)
                return RouteDecision.Pick(ChannelKind.Official, SendMode.Free,
                    "csw_open_unofficial_disabled_fallback_official");

            return RouteDecision.Deny("القناتين مطفيين");
        }

        // ────────────────────────────────────────────────────────────
        // قاعدة ٤: 🔴 مفيش نافذة — القوالب الرسمية بس
        // ────────────────────────────────────────────────────────────

        // قاعدة ٤أ: المحادثات ملهاش قالب — بره النافذة مستحيلة.
        // ⚠️ ده مش خطأ! ده الوضع الصح: رد بوت بعد ٢٥ ساعة ملوش معنى.
        // لازم العميل يبعت الأول.
        if (spec.Class == IntentClass.Conversational)
            return RouteDecision.Deny("no_window_conversational_not_allowed 🚫");

        if (!officialOn)
            return RouteDecision.Deny("no_window_official_disabled");

        // قاعدة ٥: لازم يكون فيه قالب معتمد للنية دي
        var template = await _templates.ForIntentAsync(intent.Name, "ar", ct);
        if (template is null)
            return RouteDecision.Deny($"no_window_no_approved_template ({intent.Name})");

        // قاعدة ٦: 🔴 التسويق بره النافذة على الرسمي حصراً — ومفيش fallback
        // لو حوّلنا تسويق بارد لغير الرسمي = حظر مضمون.
        if (spec.Class == IntentClass.Marketing
            && _policy.MarketingChannel != ChannelKind.Official)
            return RouteDecision.Deny("marketing_channel_policy_not_official");

        // قاعدة ٧: القالب المعتمد على الرسمي
        return RouteDecision.Pick(ChannelKind.Official, SendMode.Template,
            $"no_window_template:{template.Name} 💰");
    }

    // ══════════════════════════════════════════════════════════════════
    //  ResolveWithFallbackAsync — التدهور عند سقوط قناة. docs/09 §4.5
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// جدول التدهور:
    ///
    ///  الحالة                          │ التصرّف
    ///  ────────────────────────────────┼─────────────────────────────────────
    ///  الرسمي واقع + نية حرجة          │ حوّل لغير الرسمي (الموثوقية > التكلفة)
    ///  الرسمي واقع + تسويق             │ 🔴 أجّل. متحوّلش أبداً.
    ///  غير الرسمي واقع + محادثة        │ حوّل للرسمي لو النافذة مفتوحة
    ///  الاتنين واقعين                  │ أجّل + نبّه
    ///  الحد اليومي خلص + حرج           │ غير رسمي
    ///  الحد اليومي خلص + تسويق         │ أجّل لبكرة
    ///
    /// 🔑 <c>maxHops = 2</c> — قناتين بس، فمستحيل يحصل loop.
    /// </summary>
    public async Task<RoutingOutcome> ResolveWithFallbackAsync(SendIntent intent, int maxHops = 2,
        CancellationToken ct = default)
    {
        var spec = IntentRegistry.Get(intent.Name);
        var tried = new List<TriedChannel>();

        var decision = await RouteAsync(intent, ct);
        if (!decision.Allowed)
            return RoutingOutcome.Failure(decision.Reason, tried);

        for (var hop = 0; hop < maxHops; hop++)
        {
            var channel = decision.Channel!.Value;
            var provider = _providers.Get(channel);
            var health = await provider.HealthAsync(ct);

            // القناة شغّالة وصحّية → خلاص
            if (health.Up && !health.Degraded)
                return RoutingOutcome.Success(decision, tried);

            var why = !health.Up
                ? $"واقعة: {health.Note}"
                : $"متدهورة: {health.Note} (مساحة {health.Headroom:P0})";

            tried.Add(new TriedChannel(channel, why));
            _log.LogWarning("⚠️ القناة {Channel} {Why} — بندوّر على بديل", channel, why);

            // 🔴 التسويق ملهوش fallback. أبداً.
            // لو الرسمي واقع، التسويق بيتأجّل — مش بيروح لغير الرسمي.
            if (spec.Class == IntentClass.Marketing && !_policy.AllowMarketingFallback)
                return RoutingOutcome.Failure(
                    "marketing_no_fallback_defer 🔴 (التسويق البارد على غير الرسمي = حظر)", tried);

            // القناة البديلة
            var alt = channel == ChannelKind.Official ? ChannelKind.Unofficial : ChannelKind.Official;

            if (!IsEnabled(alt))
                return RoutingOutcome.Failure($"القناة البديلة {alt} مطفية", tried);

            // ⚠️ غير الرسمي مش بيدعم القوالب — فلو كنا في وضع قالب،
            //    البديل الوحيد مفيد لو النافذة مفتوحة، وهي مش مفتوحة (عشان كده قالب).
            if (alt == ChannelKind.Unofficial && decision.Mode == SendMode.Template)
                return RoutingOutcome.Failure(
                    "template_mode_has_no_unofficial_fallback (غير الرسمي مش بيدعم القوالب)", tried);

            // ⚠️ والعكس: الرسمي مش بيدعم رسالة حرة بره النافذة
            if (alt == ChannelKind.Official && decision.Mode == SendMode.Free)
            {
                var win = await _windows.GetStateAsync(intent.Phone, ct);
                if (!win.FreeFormAllowed)
                    return RoutingOutcome.Failure(
                        "free_mode_needs_open_window_on_official", tried);
            }

            var altHealth = await _providers.Get(alt).HealthAsync(ct);
            if (!altHealth.Up)
            {
                tried.Add(new TriedChannel(alt, $"واقعة كذلك: {altHealth.Note}"));
                return RoutingOutcome.Failure("all_channels_down 🚨 — أجّل ونبّه", tried);
            }

            decision = RouteDecision.Pick(alt, decision.Mode!.Value,
                $"fallback_from_{channel}: {why}", fallbackFrom: channel);
        }

        return RoutingOutcome.Failure("max_hops_exceeded", tried);
    }

    private bool IsEnabled(ChannelKind c) => c == ChannelKind.Official
        ? _channels.OfficialEnabled
        : _channels.UnofficialEnabled;
}
