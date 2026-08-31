using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Infrastructure.Data;
using WaHybrid.Infrastructure.Providers;

namespace WaHybrid.Infrastructure.Core;

/// <summary>
/// متتبّع الحد اليومي للحساب الرسمي. docs/10 §3.
///
/// ⚠️ فخ اتكلمنا عنه في docs/10: Meta بتصفّر الحد على **UTC** مش التوقيت المحلي.
/// لو استخدمت التاريخ المحلي في مصر (UTC+2/+3)، العدّاد بيتصفّر عندك الساعة ١٢
/// بالليل بتوقيتك، بينما Meta بتصفّره الساعة ٢ الفجر — فبتفضل ساعتين
/// فاكر إن عندك حصة وهي مخلصة، وتاخد 133016 على كل رسالة.
///
/// عشان كده مفتاح العدّاد صريح: <c>tier:used:YYYY-MM-DD</c> بتاريخ UTC.
/// </summary>
public sealed class TierStore : ITierStore
{
    private readonly HybridDbContext _db;
    private readonly ICacheStore _cache;
    private readonly ILogger<TierStore> _log;

    public TierStore(HybridDbContext db, ICacheStore cache, ILogger<TierStore> log)
        => (_db, _cache, _log) = (db, cache, log);

    /// <summary>🔑 UTC صريح — مش DateTime.Today</summary>
    private static string UsedKey => $"tier:used:{DateTime.UtcNow:yyyy-MM-dd}";

    public async Task<TierSnapshot> CurrentAsync(CancellationToken ct = default)
    {
        var row = await _db.OfficialStatuses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);

        var tier = row?.Tier ?? "TIER_250";
        var limit = row?.DailyLimit > 0 ? row.DailyLimit : TierLimits.For(tier);
        var quality = row?.QualityRating ?? QualityRating.Unknown;
        var paused = row?.MarketingPaused ?? false;

        // العدّاد من الكاش (سريع)، وبنرجع لقاعدة البيانات لو الكاش فاضي
        var cached = await _cache.GetAsync(UsedKey, ct);
        var used = cached is not null && int.TryParse(cached, out var u) ? u : row?.UsedToday ?? 0;

        return new TierSnapshot(tier, limit, used, quality, paused);
    }

    public async Task<int> IncrementAsync(int n = 1, CancellationToken ct = default)
    {
        // TTL ٤٨ ساعة: يغطّي فرق التوقيت وميخلّيش المفاتيح تتراكم
        var v = await _cache.IncrementAsync(UsedKey, TimeSpan.FromHours(48), ct);

        // نكتب في قاعدة البيانات كذلك — عشان الرقم يعيش بعد restart
        var row = await _db.OfficialStatuses.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (row is not null)
        {
            row.UsedToday = (int)v;
            row.LastCheckedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return (int)v;
    }

    /// <summary>
    /// بتتنده من مهمة خلفية كل ٦ ساعات (نفس دورة تقييم Meta) —
    /// أو يدوياً من الداشبورد. في وضع mock بنحاكي القيم.
    /// </summary>
    public async Task RefreshFromMetaAsync(string tier, int limit, QualityRating quality,
        CancellationToken ct = default)
    {
        var row = await _db.OfficialStatuses.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (row is null)
        {
            row = new Domain.Entities.OfficialStatus { Id = 1 };
            _db.OfficialStatuses.Add(row);
        }

        var prevTier = row.Tier;
        row.Tier = tier;
        row.DailyLimit = limit > 0 ? limit : TierLimits.For(tier);
        row.QualityRating = quality;
        row.LastCheckedAt = DateTimeOffset.UtcNow;
        row.ResetAt = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero);

        // 🔴 الجودة بقت حمراء → وقّف التسويق أوتوماتيك (docs/10 §5.4)
        row.Notes = quality == QualityRating.Red ? "marketing_paused_red" : null;

        await _db.SaveChangesAsync(ct);

        if (prevTier != tier)
            _log.LogWarning("📊 الـ Tier اتغيّر: {Prev} → {New} (حد {Limit})", prevTier, tier, row.DailyLimit);
    }
}

/// <summary>
/// سقف تكرار التسويق. docs/09 §5.
///
/// 🔑 فيه **عدّادين** مختلفين، وده مقصود:
///
///   1. العدّاد الموحّد (<c>freq:all:*</c>) — بيحسب القناتين مع بعض.
///      ده سقفنا الأخلاقي: العميل ميستلمش أكتر من رسالة تسويقية واحدة
///      في اليوم، سواء من الرسمي أو من غير الرسمي. لأن العميل مش فارق
///      عنده إحنا بعتنا من أنهي رقم — هو بيشوف إزعاج.
///
///   2. عدّاد Meta (<c>freq:meta:*</c>) — الرسمي بس.
///      تقدير محافظ لسقف 131049 عشان نتجنّب الخطأ قبل ما ياخده.
///      ⚠️ ده تقدير مش حقيقة — Meta مش بتنشر الرقم الفعلي.
/// </summary>
public sealed class FrequencyCap : IFrequencyCap
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly ICacheStore _cache;
    public FrequencyCap(ICacheStore cache) => _cache = cache;

    private static string AllKey(string phone) => $"freq:all:{phone}";
    private static string MetaKey(string phone) => $"freq:meta:{phone}";

    public async Task<int> GetGlobalMarketingCountAsync(string phone, CancellationToken ct = default)
        => int.TryParse(await _cache.GetAsync(AllKey(phone), ct), out var v) ? v : 0;

    public async Task<int> GetMetaMarketingCountAsync(string phone, CancellationToken ct = default)
        => int.TryParse(await _cache.GetAsync(MetaKey(phone), ct), out var v) ? v : 0;

    public async Task RecordAsync(string phone, ChannelKind channel, CancellationToken ct = default)
    {
        await _cache.IncrementAsync(AllKey(phone), Window, ct);

        // عدّاد Meta بيتزوّد بالرسمي بس — غير الرسمي مش بيدخل في حساباتهم
        if (channel == ChannelKind.Official)
            await _cache.IncrementAsync(MetaKey(phone), Window, ct);
    }
}
