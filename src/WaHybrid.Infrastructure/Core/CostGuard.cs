using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Infrastructure.Data;
using WaHybrid.Infrastructure.Options;

namespace WaHybrid.Infrastructure.Core;

/// <summary>
/// 💰 حزام الأمان المالي. docs/10 §4.
///
/// السيناريو اللي بيحصل في الواقع: حد بيشغّل حملة على ٥٠ ألف عميل بالغلط،
/// أو bug في الـ loop بيبعت نفس الرسالة ١٠ مرات. من غير الحاجة دي بتصحى
/// الصبح تلاقي فاتورة بألف دولار.
///
/// 🔑 القاعدة الذكية: الإيقاف بيضرب **التسويق بس**.
/// المعاملات الحرجة (تأكيد أوردر، إلغاء، OTP) بتفضل ماشية —
/// لأن إيقافها يخسّرك عملاء وفلوس أكتر من الفاتورة.
/// ده مطبّق في <c>MessageSender</c> بشرط <c>IntentSpec.Critical</c>.
/// </summary>
public sealed class CostGuard : ICostGuard
{
    private readonly HybridDbContext _db;
    private readonly ICacheStore _cache;
    private readonly CostOptions _opt;
    private readonly IAlerter _alerter;
    private readonly ILogger<CostGuard> _log;

    public CostGuard(HybridDbContext db, ICacheStore cache, IOptions<HybridOptions> opt,
        IAlerter alerter, ILogger<CostGuard> log)
    {
        _db = db;
        _cache = cache;
        _opt = opt.Value.Cost;
        _alerter = alerter;
        _log = log;
    }

    public async Task<BudgetSnapshot> CheckAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // 💰 التكلفة الفعلية لو الـ webhook أكّدها، وإلا التقديرية
        var todayRows = await _db.MessageLogs
            .Where(m => m.Direction == MessageDirection.Out
                        && m.CreatedAt >= new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            .Select(m => new { m.CostEstimated, m.CostBilled })
            .ToListAsync(ct);

        var monthRows = await _db.MessageLogs
            .Where(m => m.Direction == MessageDirection.Out
                        && m.CreatedAt >= new DateTimeOffset(monthStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            .Select(m => new { m.CostEstimated, m.CostBilled })
            .ToListAsync(ct);

        var spentToday = todayRows.Sum(r => r.CostBilled ?? r.CostEstimated);
        var spentMonth = monthRows.Sum(r => r.CostBilled ?? r.CostEstimated);

        // النسبة = الأسوأ بين اليومي والشهري
        var dailyPct = _opt.DailyLimitUsd == 0 ? 0 : (double)(spentToday / _opt.DailyLimitUsd) * 100;
        var monthlyPct = _opt.MonthlyLimitUsd == 0 ? 0 : (double)(spentMonth / _opt.MonthlyLimitUsd) * 100;
        var pct = Math.Max(dailyPct, monthlyPct);

        var hardStop = pct >= _opt.HardStopAtPct;
        var alert = pct >= _opt.AlertAtPct;

        if (alert) await AlertOnceAsync(pct, spentToday, spentMonth, hardStop, ct);

        return new BudgetSnapshot(spentToday, spentMonth,
            _opt.DailyLimitUsd, _opt.MonthlyLimitUsd, pct, hardStop, alert);
    }

    /// <summary>
    /// تنبيه مرة واحدة في الساعة — عشان متغرقش تليجرام بـ ٥٠٠ رسالة
    /// في الدقيقة والفريق يطفّي الإشعارات ويفوته التنبيه المهم.
    /// </summary>
    private async Task AlertOnceAsync(double pct, decimal today, decimal month,
        bool hardStop, CancellationToken ct)
    {
        var key = $"cost:alerted:{DateTime.UtcNow:yyyy-MM-dd-HH}";
        if (!await _cache.SetIfNotExistsAsync(key, "1", TimeSpan.FromHours(2), ct))
            return;

        var msg = hardStop
            ? $"🚨 إيقاف تلقائي للتسويق! الميزانية {pct:F0}% — اليوم ${today:F2} / الشهر ${month:F2}"
            : $"⚠️ تحذير ميزانية: {pct:F0}% — اليوم ${today:F2} / الشهر ${month:F2}";

        await _alerter.SendAsync(hardStop ? "critical" : "warn", msg, ct);
        _log.LogWarning("{Message}", msg);
    }
}

/// <summary>
/// دفتر التكاليف المجمّع — بيغذّي الداشبورد وتقرير المدير.
/// docs/09 §6 (6).
/// </summary>
public sealed class CostLedger
{
    private readonly HybridDbContext _db;
    private readonly ICostBook _book;

    public CostLedger(HybridDbContext db, ICostBook book) => (_db, _book) = (db, book);

    /// <summary>تجميع يوم كامل في جدول <c>cost_ledger</c> (بيتنده من مهمة ليلية)</summary>
    public async Task RollUpAsync(DateOnly day, CancellationToken ct = default)
    {
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = from.AddDays(1);

        var rows = await _db.MessageLogs
            .Where(m => m.Direction == MessageDirection.Out && m.CreatedAt >= from && m.CreatedAt < to)
            .Select(m => new
            {
                m.Channel, m.MetaCategory, m.Phone, m.Status,
                m.CostEstimated, m.CostBilled
            })
            .ToListAsync(ct);

        var groups = rows
            .GroupBy(r => new
            {
                r.Channel,
                r.MetaCategory,
                Country = ConfigCostBook.CountryFromPhone(r.Phone)
            });

        foreach (var g in groups)
        {
            var existing = await _db.CostLedger.FirstOrDefaultAsync(
                x => x.Day == day && x.Channel == g.Key.Channel
                     && x.MetaCategory == g.Key.MetaCategory
                     && x.CountryCode == g.Key.Country, ct);

            var entry = existing ?? new Domain.Entities.CostLedgerEntry
            {
                Day = day, Channel = g.Key.Channel,
                MetaCategory = g.Key.MetaCategory, CountryCode = g.Key.Country
            };

            entry.MsgCount = g.Count();
            // 🔑 الفاتورة على التسليم — فبنعدّ المُسلَّم منفصل
            entry.Delivered = g.Count(x => x.Status is MessageStatus.Delivered or MessageStatus.Read);
            entry.CostUsd = g.Sum(x => x.CostBilled ?? x.CostEstimated);
            entry.BspFeeUsd = entry.Delivered * _book.BspFee;

            if (existing is null) _db.CostLedger.Add(entry);
        }

        await _db.SaveChangesAsync(ct);
    }
}
