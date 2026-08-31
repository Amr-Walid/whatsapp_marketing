using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Infrastructure.Options;

namespace WaHybrid.Infrastructure.Core;

/// <summary>
/// 🔴 مفتاح الطوارئ. من docs/03 — بيوقف الإرسال فوراً بدون deploy.
/// الحالة في الكاش (Redis في الإنتاج) عشان تشتغل على كل الـ instances.
/// </summary>
public sealed class KillSwitch : IKillSwitch
{
    private const string UnofficialKey = "kill:unofficial";
    private const string GlobalKey = "kill:global";
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private readonly ICacheStore _cache;
    private readonly IAlerter _alerter;
    private readonly ILogger<KillSwitch> _log;

    public KillSwitch(ICacheStore cache, IAlerter alerter, ILogger<KillSwitch> log)
        => (_cache, _alerter, _log) = (cache, alerter, log);

    public Task<bool> IsUnofficialKilledAsync(CancellationToken ct = default)
        => _cache.ExistsAsync(UnofficialKey, ct);

    public Task<bool> IsGlobalKilledAsync(CancellationToken ct = default)
        => _cache.ExistsAsync(GlobalKey, ct);

    public async Task SetUnofficialAsync(bool killed, string? reason, CancellationToken ct = default)
    {
        if (killed)
        {
            await _cache.SetAsync(UnofficialKey, reason ?? "manual", Ttl, ct);
            await _alerter.SendAsync("critical", $"🔴 القناة غير الرسمية اتوقفت: {reason ?? "يدوي"}", ct);
        }
        else
        {
            await _cache.RemoveAsync(UnofficialKey, ct);
            await _alerter.SendAsync("info", "🟢 القناة غير الرسمية اتشغّلت تاني", ct);
        }
        _log.LogWarning("KillSwitch(unofficial) = {Killed} — {Reason}", killed, reason);
    }

    public async Task SetGlobalAsync(bool killed, string? reason, CancellationToken ct = default)
    {
        if (killed)
        {
            await _cache.SetAsync(GlobalKey, reason ?? "manual", Ttl, ct);
            await _alerter.SendAsync("critical", $"🚨 الإرسال اتوقف بالكامل (القناتين): {reason ?? "يدوي"}", ct);
        }
        else
        {
            await _cache.RemoveAsync(GlobalKey, ct);
            await _alerter.SendAsync("info", "🟢 الإرسال اتشغّل تاني", ct);
        }
        _log.LogWarning("KillSwitch(global) = {Killed} — {Reason}", killed, reason);
    }
}

/// <summary>
/// التنبيهات. في الإنتاج بتبعت على تليجرام؛ هنا بتتخزّن في الذاكرة
/// عشان الداشبورد يعرضها والاختبار يتحقق منها.
/// </summary>
public sealed class InMemoryAlerter : IAlerter
{
    private readonly List<AlertRecord> _records = new();
    private readonly object _lock = new();
    private readonly ILogger<InMemoryAlerter> _log;

    public InMemoryAlerter(ILogger<InMemoryAlerter> log) => _log = log;

    public Task SendAsync(string severity, string message, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _records.Add(new AlertRecord(DateTimeOffset.UtcNow, severity, message));
            if (_records.Count > 500) _records.RemoveRange(0, 200);
        }

        var level = severity switch
        {
            "critical" => LogLevel.Critical,
            "warn" => LogLevel.Warning,
            _ => LogLevel.Information
        };
        _log.Log(level, "[ALERT:{Severity}] {Message}", severity, message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<AlertRecord> Recent(int take = 50)
    {
        lock (_lock)
            return _records.AsEnumerable().Reverse().Take(take).ToList();
    }
}

/// <summary>
/// جدول الأسعار.
/// ⚠️ من docs/08 §4.2: **متبنيش حساباتك على رقم من مقالة.**
/// الأسعار هنا من الإعدادات ولازم تتحقق منها من كارت Meta كل 3 شهور.
/// </summary>
public sealed class ConfigCostBook : ICostBook
{
    private readonly PricingOptions _opt;

    public ConfigCostBook(IOptions<HybridOptions> opt) => _opt = opt.Value.Pricing;

    public decimal BspFee => _opt.BspFeePerMessage;

    public decimal Price(string phone, MetaCategory category)
    {
        var country = CountryFromPhone(phone);
        var key = $"{country}:{category}";

        if (_opt.Rates.TryGetValue(key, out var rate))
            return rate + (category == MetaCategory.Service ? 0m : _opt.BspFeePerMessage);

        // fallback على البلد الافتراضي
        var fallbackKey = $"{_opt.DefaultCountry}:{category}";
        if (_opt.Rates.TryGetValue(fallbackKey, out var fb))
            return fb + (category == MetaCategory.Service ? 0m : _opt.BspFeePerMessage);

        return 0m;
    }

    /// <summary>استنتاج البلد من كود الاتصال — مبسّط، وسّعه حسب أسواقك</summary>
    public static string CountryFromPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone)) return "EG";
        if (phone.StartsWith("20", StringComparison.Ordinal)) return "EG";
        if (phone.StartsWith("966", StringComparison.Ordinal)) return "SA";
        if (phone.StartsWith("971", StringComparison.Ordinal)) return "AE";
        if (phone.StartsWith("1", StringComparison.Ordinal)) return "US";
        if (phone.StartsWith("44", StringComparison.Ordinal)) return "GB";
        return "EG";
    }
}
