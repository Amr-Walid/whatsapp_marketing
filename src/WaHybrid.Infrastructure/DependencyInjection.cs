using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Infrastructure.Core;
using WaHybrid.Infrastructure.Data;
using WaHybrid.Infrastructure.Gates;
using WaHybrid.Infrastructure.Options;
using WaHybrid.Infrastructure.Providers;
using WaHybrid.Infrastructure.Routing;
using WaHybrid.Infrastructure.Webhooks;

namespace WaHybrid.Infrastructure;

/// <summary>
/// تركيب النظام كله. ده المكان الوحيد اللي بيعرف "أنهي تنفيذ لأنهي واجهة".
///
/// 🔑 لاحظ إن استبدال المكوّنات في الإنتاج كله سطر واحد:
///   • MemoryCacheStore → RedisCacheStore
///   • MockProvider → OfficialProvider / UnofficialProvider
///   • InMemoryAlerter → TelegramAlerter
///   • SQLite → SQL Server
/// وباقي النظام مش بيتغيّر فيه حرف. ده مقصود مش صدفة.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaHybrid(this IServiceCollection services,
        IConfiguration config)
    {
        // ═══════════════ الإعدادات ═══════════════
        services.Configure<HybridOptions>(config.GetSection(HybridOptions.SectionName));

        // ═══════════════ قاعدة البيانات ═══════════════
        // 🔑 المزوّد الأساسي = SQL Server. SQLite للتطوير بنفس المخطط بالظبط.
        var provider = config["Database:Provider"] ?? "Sqlite";
        var conn = config.GetConnectionString(provider) ?? "Data Source=wahybrid.db";

        services.AddDbContext<HybridDbContext>(o =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                o.UseSqlServer(conn, sql => sql.EnableRetryOnFailure(3));
            else
                o.UseSqlite(conn);
        });

        // ═══════════════ الخدمات المساندة ═══════════════
        // ⚠️ Singleton مقصود: الكاش والتنبيهات لازم يبقوا مشتركين بين كل الطلبات
        services.AddSingleton<ICacheStore, MemoryCacheStore>();
        services.AddSingleton<IAlerter, InMemoryAlerter>();
        services.AddSingleton<ICostBook, ConfigCostBook>();
        services.AddSingleton<IKillSwitch, KillSwitch>();
        services.AddSingleton<DelayEngine>();

        // ═══════════════ المكوّنات الأساسية ═══════════════
        services.AddScoped<IWindowTracker, WindowTracker>();
        services.AddScoped<ITierStore, TierStore>();
        services.AddScoped<TierStore>();
        services.AddSingleton<IFrequencyCap, FrequencyCap>();
        services.AddScoped<ICostGuard, CostGuard>();
        services.AddScoped<CostLedger>();
        services.AddScoped<ITemplateRegistry, TemplateRegistry>();
        services.AddScoped<TemplateRegistry>();

        // ═══════════════ البوابات (بترتيبها) ═══════════════
        services.AddScoped<IGate, SuppressionGate>();          // 10
        services.AddScoped<IGate, ConsentGate>();              // 20
        services.AddScoped<IGate, CrossChannelDedupeGate>();   // 30
        services.AddScoped<IGate, GlobalFrequencyGate>();      // 40
        services.AddScoped<IGate, WindowGate>();               // 50
        services.AddScoped<IGate, MetaFrequencyCapGate>();     // 60
        services.AddScoped<IGate, MessagingTierGate>();        // 70
        services.AddScoped<IGate, TemplateReadyGate>();        // 80
        services.AddScoped<IGateChain, GateChain>();

        // ═══════════════ المزوّدين ═══════════════
        AddProviders(services, config);
        services.AddScoped<IProviderRegistry, ProviderRegistry>();

        // ═══════════════ التوجيه والإرسال ═══════════════
        services.AddScoped<ChannelRouter>();
        services.AddScoped<IChannelRouter>(sp => sp.GetRequiredService<ChannelRouter>());
        services.AddScoped<IMessageSender, MessageSender>();
        services.AddScoped<CampaignPlanner>();

        // ═══════════════ مسار الدخول ═══════════════
        services.AddScoped<InboundHandler>();

        return services;
    }

    /// <summary>
    /// 🔑 هنا بالظبط بيتحدّد "وهمي ولا حقيقي" — سطر واحد في الإعدادات.
    ///
    /// وضع mock: مفيش شبكة، مفيش تكلفة، مفيش خطر — بس كل منطق النظام شغّال.
    /// وضع live: نفس الكود بالظبط، بس بيكلّم Meta و Evolution فعلاً.
    /// </summary>
    private static void AddProviders(IServiceCollection services, IConfiguration config)
    {
        var mode = config[$"{HybridOptions.SectionName}:Channels:ProviderMode"] ?? "mock";
        var failRate = double.TryParse(
            config[$"{HybridOptions.SectionName}:Channels:MockFailRate"], out var f) ? f : 0;

        if (mode.Equals("mock", StringComparison.OrdinalIgnoreCase))
        {
            // ⚠️ Singleton: عشان سجل الرسايل المُرسَلة يعيش بين الطلبات
            //    (الداشبورد بيقرأه)
            services.AddSingleton(sp => new MockProvider(ChannelKind.Official,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("MockProvider.Official"),
                failRate));

            services.AddSingleton(sp => new MockProvider(ChannelKind.Unofficial,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("MockProvider.Unofficial"),
                failRate));

            services.AddSingleton<IMessageProvider>(sp =>
                sp.GetServices<MockProvider>().First(p => p.Channel == ChannelKind.Official));
            services.AddSingleton<IMessageProvider>(sp =>
                sp.GetServices<MockProvider>().First(p => p.Channel == ChannelKind.Unofficial));
        }
        else
        {
            services.AddHttpClient<OfficialProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
            services.AddHttpClient<UnofficialProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
            services.AddScoped<IMessageProvider>(sp => sp.GetRequiredService<OfficialProvider>());
            services.AddScoped<IMessageProvider>(sp => sp.GetRequiredService<UnofficialProvider>());
        }
    }
}
