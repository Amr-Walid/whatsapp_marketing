using Microsoft.EntityFrameworkCore;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Intents;
using WaHybrid.Domain.Windows;
using WaHybrid.Infrastructure.Data;

namespace WaHybrid.Infrastructure.Routing;

/// <summary>
/// 🔍 مخطّط الحملة (Dry Run). docs/10 §7.2.
///
/// ═══════════════════════════════════════════════════════════════════
///  ده **أهم مكوّن للمدير** في المشروع كله.
///
///  قبل أي حملة، بتشغّلها dry-run وبتشوف بالظبط:
///    • كام رسالة هتروح على الرسمي وكام على غير الرسمي
///    • كام واحدة هتترفض وليه (بالتفصيل، بوابة بوابة)
///    • 💰 التكلفة المتوقّعة بالدولار **قبل** ما تصرف مليم
///    • نسبة المجاني (المؤشر الأساسي: لازم > ٧٥٪)
///
///  يعني بدل "بعتنا وشوفنا إيه اللي حصل" → "عرفنا إيه اللي هيحصل
///  وبعدين قرّرنا". ده الفرق بين إدارة ومقامرة.
/// ═══════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CampaignPlanner
{
    private readonly HybridDbContext _db;
    private readonly ChannelRouter _router;
    private readonly ICostBook _costBook;
    private readonly ITemplateRegistry _templates;

    public CampaignPlanner(HybridDbContext db, ChannelRouter router, ICostBook costBook,
        ITemplateRegistry templates)
        => (_db, _router, _costBook, _templates) = (db, router, costBook, templates);

    public async Task<CampaignPlan> PlanAsync(string intentName, string? segment,
        int limit = 1000, CancellationToken ct = default)
    {
        var spec = IntentRegistry.Get(intentName);
        var now = DateTimeOffset.UtcNow;

        // المستهدفين: مشتركين، مش محظورين، مش في قائمة الحظر
        var query = _db.Customers.AsNoTracking()
            .Where(c => c.OptedIn && !c.OptedOut);

        if (!string.IsNullOrWhiteSpace(segment))
            query = query.Where(c => c.Segment == segment);

        var suppressed = await _db.SuppressionList.AsNoTracking()
            .Select(s => s.Phone).ToListAsync(ct);
        var suppressedSet = suppressed.ToHashSet(StringComparer.Ordinal);

        var customers = await query
            .OrderByDescending(c => c.Priority)
            .Take(limit)
            .Select(c => new { c.Id, c.Phone, c.Name, c.Segment })
            .ToListAsync(ct);

        // 🔑 قراءة النوافذ بضربة واحدة — مش استعلام لكل عميل.
        // على ١٠ آلاف عميل، الفرق بين ثانية و ١٠ آلاف استعلام.
        var ids = customers.Select(c => c.Id).ToList();
        var windowRows = await _db.CustomerWindows.AsNoTracking()
            .Where(w => ids.Contains(w.CustomerId) && w.ExpiresAt > now)
            .Select(w => new { w.CustomerId, w.Kind, w.ExpiresAt })
            .ToListAsync(ct);

        var windowMap = windowRows
            .GroupBy(w => w.CustomerId)
            .ToDictionary(
                g => g.Key,
                g => CustomerWindowState.From(
                    g.Where(x => x.Kind == WindowKind.Fep).Select(x => (DateTimeOffset?)x.ExpiresAt).Max(),
                    g.Where(x => x.Kind == WindowKind.Csw).Select(x => (DateTimeOffset?)x.ExpiresAt).Max(),
                    now));

        var template = await _templates.ForIntentAsync(intentName, "ar", ct);
        var plan = new CampaignPlan
        {
            IntentName = intentName,
            IntentLabel = spec.ArabicLabel,
            Segment = segment,
            TotalTargeted = customers.Count,
            TemplateName = template?.Name,
            TemplateAvailable = template is not null
        };

        foreach (var c in customers)
        {
            if (suppressedSet.Contains(c.Phone))
            {
                plan.AddSkip("gSuppression", "في قائمة الحظر");
                continue;
            }

            var win = windowMap.TryGetValue(c.Id, out var w) ? w : CustomerWindowState.None;

            var sendIntent = new SendIntent
            {
                Name = intentName,
                CustomerId = c.Id,
                Phone = c.Phone,
                Segment = c.Segment
            };

            var decision = await _router.DecideAsync(spec, win, sendIntent, ct);

            if (!decision.Allowed)
            {
                plan.AddSkip("router", decision.Reason);
                continue;
            }

            var cost = decision.Mode == SendMode.Template && win.State != WindowState.FepOpen
                ? _costBook.Price(c.Phone, template?.Category ?? spec.MetaCategory)
                : 0m;

            plan.Add(decision.Channel!.Value, decision.Mode!.Value, win.State, cost, decision.Reason);
        }

        return plan;
    }
}

/// <summary>خطة الحملة — النتيجة اللي تتعرض للمدير</summary>
public sealed class CampaignPlan
{
    public string IntentName { get; set; } = "";
    public string IntentLabel { get; set; } = "";
    public string? Segment { get; set; }
    public int TotalTargeted { get; set; }
    public string? TemplateName { get; set; }
    public bool TemplateAvailable { get; set; }

    public int Official { get; private set; }
    public int Unofficial { get; private set; }
    public int Skipped { get; private set; }

    public int FreeMessages { get; private set; }
    public int TemplateMessages { get; private set; }

    public int InFep { get; private set; }
    public int InCsw { get; private set; }
    public int NoWindow { get; private set; }

    public decimal EstimatedCostUsd { get; private set; }

    /// <summary>أسباب التخطّي مجمّعة — عشان تعرف الفاقد راح فين</summary>
    public Dictionary<string, int> SkipReasons { get; } = new();

    /// <summary>أسباب التوجيه مجمّعة — عشان تراجع قرارات الـ Router</summary>
    public Dictionary<string, int> RouteReasons { get; } = new();

    public void Add(ChannelKind ch, SendMode mode, WindowState win, decimal cost, string reason)
    {
        if (ch == ChannelKind.Official) Official++; else Unofficial++;
        if (mode == SendMode.Free) FreeMessages++; else TemplateMessages++;

        switch (win)
        {
            case WindowState.FepOpen: InFep++; break;
            case WindowState.CswOpen: InCsw++; break;
            default: NoWindow++; break;
        }

        EstimatedCostUsd += cost;

        var key = reason.Split(' ')[0];
        RouteReasons[key] = RouteReasons.GetValueOrDefault(key) + 1;
    }

    public void AddSkip(string gate, string reason)
    {
        Skipped++;
        var key = $"{gate}: {reason.Split(' ')[0]}";
        SkipReasons[key] = SkipReasons.GetValueOrDefault(key) + 1;
    }

    public int Sendable => Official + Unofficial;

    /// <summary>
    /// 🎯 المؤشر الأساسي في النظام كله (docs/10 §8.3): لازم يبقى > ٧٥٪.
    /// لو أقل، معناها إحنا بنبعت بره النوافذ كتير — والحل مش رقم تاني،
    /// الحل استراتيجية FEP/CTWA (docs/10 §10).
    /// </summary>
    public double FreePct => Sendable == 0 ? 0 : (double)FreeMessages / Sendable * 100;

    public decimal CostPerSendable => Sendable == 0 ? 0 : EstimatedCostUsd / Sendable;

    /// <summary>
    /// تقدير التكلفة لو كنا رسمي ١٠٠٪ بقالب (السيناريو "بدون هجين") —
    /// الرقم اللي بيوري المدير قيمة النظام بالدولار.
    /// </summary>
    public decimal CostIfAllOfficialTemplates(decimal marketingRate = 0.035m)
        => Sendable * marketingRate;

    public decimal SavingsVsAllOfficial(decimal marketingRate = 0.035m)
        => CostIfAllOfficialTemplates(marketingRate) - EstimatedCostUsd;

    /// <summary>عرض ASCII — للطباعة في الـ console أو الـ logs</summary>
    public string ToAsciiBox()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════════════╗");
        sb.AppendLine($"║  خطة الحملة: {IntentLabel,-42} ║");
        sb.AppendLine("╠══════════════════════════════════════════════════════════╣");
        sb.AppendLine($"║  المستهدفين      : {TotalTargeted,-38} ║");
        sb.AppendLine($"║  قابل للإرسال     : {Sendable,-38} ║");
        sb.AppendLine($"║  مرفوض/متخطّى     : {Skipped,-38} ║");
        sb.AppendLine("╠══════════════════════════════════════════════════════════╣");
        sb.AppendLine($"║  → رسمي          : {Official,-38} ║");
        sb.AppendLine($"║  → غير رسمي      : {Unofficial,-38} ║");
        sb.AppendLine($"║  → رسالة حرة     : {FreeMessages,-38} ║");
        sb.AppendLine($"║  → قالب معتمد    : {TemplateMessages,-38} ║");
        sb.AppendLine("╠══════════════════════════════════════════════════════════╣");
        sb.AppendLine($"║  🎁 في نافذة FEP  : {InFep,-38} ║");
        sb.AppendLine($"║  🟡 في نافذة CSW  : {InCsw,-38} ║");
        sb.AppendLine($"║  🔴 مفيش نافذة    : {NoWindow,-38} ║");
        sb.AppendLine("╠══════════════════════════════════════════════════════════╣");
        sb.AppendLine($"║  💰 التكلفة       : ${EstimatedCostUsd,-37:F3} ║");
        sb.AppendLine($"║  📊 نسبة المجاني  : {FreePct,-34:F1} %  ║");
        sb.AppendLine($"║  💵 وفّرنا         : ${SavingsVsAllOfficial(),-37:F2} ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════╝");
        return sb.ToString();
    }
}
