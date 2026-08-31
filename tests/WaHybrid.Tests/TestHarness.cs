using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Infrastructure.Core;
using WaHybrid.Infrastructure.Data;
using WaHybrid.Infrastructure.Gates;
using WaHybrid.Infrastructure.Options;
using WaHybrid.Infrastructure.Providers;
using WaHybrid.Infrastructure.Routing;
using WaHybrid.Infrastructure.Webhooks;

namespace WaHybrid.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════
///  🧪 بيئة اختبار معزولة بالكامل
/// ═══════════════════════════════════════════════════════════════════
///
/// كل اختبار بياخد قاعدة بيانات SQLite **في الذاكرة** لوحده، وكاش لوحده،
/// ومزوّدين وهميين لوحده. يعني:
///   • مفيش اختبار بيأثر على اختبار تاني
///   • الاختبارات تشتغل بالتوازي بدون تعارض
///   • مفيش ملفات على الديسك تفضل بعد الاختبار
///
/// ⚠️ نقطة تقنية مهمة: SQLite in-memory بتموت لحظة ما آخر connection
/// يتقفل. عشان كده بنمسك <c>SqliteConnection</c> مفتوح في الـ field
/// وبنقفله في <c>Dispose</c> بس. لو سيبناها لـ EF، القاعدة كانت
/// هتتفضى بين كل كويري والتاني.
///
/// 🔑 والأهم: ده **نفس الكود بالحرف** اللي بيشتغل على SQL Server في
/// الإنتاج — نفس الـ DbContext، نفس الـ services، نفس الـ router.
/// إحنا بنغيّر المزوّد بس. فلو الاختبار نجح هنا، منطق العمل صح.
/// </summary>
public sealed class TestHarness : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _conn;
    private readonly ServiceProvider _sp;

    public HybridDbContext Db { get; }
    public ChannelRouter Router { get; }
    public IWindowTracker Windows { get; }
    public IMessageSender Sender { get; }
    public ITemplateRegistry Templates { get; }
    public TierStore Tiers { get; }
    public ICacheStore Cache { get; }
    public ICostGuard Cost { get; }
    public IGateChain Gates { get; }
    public InboundHandler Inbound { get; }
    public CampaignPlanner Planner { get; }
    public MockProvider Official { get; }
    public MockProvider Unofficial { get; }
    public HybridOptions Options { get; }

    public TestHarness(Action<HybridOptions>? tweak = null)
    {
        // ── قاعدة بيانات في الذاكرة، عمرها = عمر الـ harness ──
        _conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        _conn.Open();

        Options = new HybridOptions();
        // في الاختبار مش هنستنى ٤٥ ثانية تأخير بشري
        Options.Unofficial.SkipDelayInDev = true;
        tweak?.Invoke(Options);

        var opt = Microsoft.Extensions.Options.Options.Create(Options);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddDbContext<HybridDbContext>(o => o.UseSqlite(_conn));
        services.AddSingleton(opt);

        _sp = services.BuildServiceProvider();

        Db = _sp.GetRequiredService<HybridDbContext>();
        Db.Database.EnsureCreated();

        // ── التركيب اليدوي: بيخلّي كل اعتماد واضح قصادك ──
        Cache = new MemoryCacheStore();
        var alerter = new InMemoryAlerter(NullLogger<InMemoryAlerter>.Instance);
        var costBook = new ConfigCostBook(opt);
        var kill = new KillSwitch(Cache, alerter, NullLogger<KillSwitch>.Instance);

        Windows = new WindowTracker(Db, Cache, NullLogger<WindowTracker>.Instance);
        Templates = new TemplateRegistry(Db, NullLogger<TemplateRegistry>.Instance);
        Tiers = new TierStore(Db, Cache, NullLogger<TierStore>.Instance);
        var freq = new FrequencyCap(Cache);
        Cost = new CostGuard(Db, Cache, opt, alerter, NullLogger<CostGuard>.Instance);

        Official = new MockProvider(ChannelKind.Official, NullLogger<MockProvider>.Instance);
        Unofficial = new MockProvider(ChannelKind.Unofficial, NullLogger<MockProvider>.Instance);
        var registry = new ProviderRegistry(new IMessageProvider[] { Official, Unofficial });

        Router = new ChannelRouter(Windows, registry, Templates, Db, opt,
            NullLogger<ChannelRouter>.Instance);

        // ── البوابات بترتيبها الصحيح ──
        var gateList = new IGate[]
        {
            new SuppressionGate(Db),
            new ConsentGate(Db),
            new CrossChannelDedupeGate(Cache, NullLogger<CrossChannelDedupeGate>.Instance),
            new GlobalFrequencyGate(freq, opt),
            new WindowGate(Windows, NullLogger<WindowGate>.Instance),
            new MetaFrequencyCapGate(freq, opt),
            new MessagingTierGate(Tiers, opt, NullLogger<MessagingTierGate>.Instance),
            new TemplateReadyGate(Templates)
        };
        Gates = new GateChain(gateList, NullLogger<GateChain>.Instance);

        Sender = new MessageSender(Db, Router, registry, Windows, Templates, Gates,
            Cost, costBook, freq, kill, alerter,
            NullLogger<MessageSender>.Instance);

        Inbound = new InboundHandler(Db, Windows, NullLogger<InboundHandler>.Instance);
        Planner = new CampaignPlanner(Db, Router, costBook, Templates);
    }

    /// <summary>يبذر القوالب الخمسة + حالة رسمية سليمة (TIER_1K / أخضر)</summary>
    public async Task<TestHarness> SeedBaseAsync()
    {
        Db.WaTemplates.AddRange(TemplateRegistry.SeedTemplates());
        Db.OfficialStatuses.Add(new Domain.Entities.OfficialStatus
        {
            Id = 1,
            Tier = "TIER_1K",
            DailyLimit = 1000,
            UsedToday = 0,
            QualityRating = QualityRating.Green,
            LastCheckedAt = DateTimeOffset.UtcNow
        });
        Db.WaSessions.Add(new Domain.Entities.WaSession
        {
            SessionId = "sess-test",
            Status = "active",
            RiskScore = 5,
            SentToday = 0,
            DailyQuota = 200,
            WarmupDay = 30
        });
        await Db.SaveChangesAsync();
        return this;
    }

    /// <summary>عميل جديد بحالة نافذة محددة — الأداة الأساسية في الاختبارات</summary>
    public async Task<Domain.Entities.Customer> NewCustomerAsync(
        string phone, WindowState window = WindowState.NoWindow,
        bool optedIn = true, string? segment = null,
        ChannelKind? preferred = null)
    {
        var c = new Domain.Entities.Customer
        {
            Phone = phone,
            Name = "عميل اختبار",
            OptedIn = optedIn,
            OptedInAt = optedIn ? DateTimeOffset.UtcNow : null,
            Segment = segment,
            PreferredChannel = preferred,
            AcquisitionSource = window == WindowState.FepOpen
                ? AcquisitionSource.Ctwa
                : AcquisitionSource.Import
        };
        Db.Customers.Add(c);
        await Db.SaveChangesAsync();

        // 🔑 ملاحظة: FEP دايماً بيجي مع CSW في الواقع، لأن ضغطة الإعلان
        //    نفسها رسالة داخلة. فبنحاكي ده بالظبط.
        if (window == WindowState.FepOpen)
        {
            await Windows.OpenFepAsync(c.Id, phone, Domain.Windows.WindowSources.CtwaAd,
                "ad_test", ChannelKind.Official);
            await Windows.TouchCswAsync(c.Id, phone, "msg_test", ChannelKind.Official);
        }
        else if (window == WindowState.CswOpen)
        {
            await Windows.TouchCswAsync(c.Id, phone, "msg_test", ChannelKind.Unofficial);
        }

        return c;
    }

    public void Dispose()
    {
        Db.Dispose();
        _sp.Dispose();
        _conn.Dispose();
    }
}
