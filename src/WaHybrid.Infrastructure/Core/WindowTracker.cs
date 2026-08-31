using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Entities;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Windows;
using WaHybrid.Infrastructure.Data;

namespace WaHybrid.Infrastructure.Core;

/// <summary>
/// 🔑 متتبّع النوافذ — أهم مكوّن في النظام كله. docs/09 §3.
///
/// ليه هو الأهم؟ لأن الإجابة على سؤال واحد ("إيه النافذة المفتوحة للعميل ده دلوقتي؟")
/// بتحدّد ٣ حاجات في نفس الوقت:
///   1. القناة اللي هنبعت منها (رسمي ولا غير رسمي)
///   2. الوضع (رسالة حرة ولا قالب معتمد)
///   3. التكلفة (صفر ولا $0.03)
///
/// القواعد (من كارت Meta، docs/08 §2):
///   • FEP = 72 ساعة، بتتفتح بضغطة إعلان CTWA أو زرار الصفحة. **كل حاجة مجاناً**
///     حتى قوالب التسويق. وبتفضل مجانية بعد 1 أكتوبر 2026.
///   • CSW = 24 ساعة، بتتفتح/بتتجدّد بأي رسالة داخلة من العميل.
///     مجانية النهاردة، لكن **هتبقى مدفوعة من 1 أكتوبر 2026**.
///   • الأسبقية: FEP > CSW > NO_WINDOW (لأن FEP أرخص وأوسع).
///
/// ⚡ الأداء: <see cref="GetStateAsync"/> بيتنده على **كل** رسالة داخلة وخارجة.
/// فبنستخدم كاش قصير جداً؛ TTL = أقرب انتهاء نافذة، وبحد أقصى ٥ دقايق —
/// كده مستحيل الكاش يقول "مفتوحة" وهي قافلة لأكتر من ٥ دقايق.
/// </summary>
public sealed class WindowTracker : IWindowTracker
{
    /// <summary>سقف عمر الكاش — حتى لو النافذة فاضلها ٧٠ ساعة</summary>
    private static readonly TimeSpan MaxCacheTtl = TimeSpan.FromMinutes(5);

    private readonly HybridDbContext _db;
    private readonly ICacheStore _cache;
    private readonly ILogger<WindowTracker> _log;

    public WindowTracker(HybridDbContext db, ICacheStore cache, ILogger<WindowTracker> log)
        => (_db, _cache, _log) = (db, cache, log);

    private static string Key(string phone) => $"win:{phone}";

    // ══════════════════════════════════════════════════════════════════
    //  1. فتح نافذة FEP (٧٢ ساعة مجانية) 🎁
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// بتتنده من:
    ///   • webhook الرسمي لما يشوف <c>referral.source_type = "ad"</c> (ضغطة CTWA)
    ///   • أو <c>source_type = "page"</c> (زرار "راسلنا" على صفحة فيسبوك)
    ///
    /// ⚠️ ملاحظة مهمة: FEP **مش بتتجدّد** بالمعنى الحرفي — كل ضغطة إعلان جديدة
    /// بتفتح ٧٢ ساعة جديدة من لحظتها. فبنعمل upsert وبنمدّ <c>ExpiresAt</c>
    /// وبنزوّد <c>RenewCount</c> للتحليل (كام مرة العميل ضغط إعلان).
    /// </summary>
    public async Task<DateTimeOffset> OpenFepAsync(long customerId, string phone, string source,
        string? sourceRef, ChannelKind channel, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(WindowDurations.FepHours);

        var existing = await _db.CustomerWindows
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.Kind == WindowKind.Fep, ct);

        if (existing is null)
        {
            _db.CustomerWindows.Add(new CustomerWindow
            {
                CustomerId = customerId,
                Phone = phone,
                Kind = WindowKind.Fep,
                OpenedAt = now,
                ExpiresAt = expires,
                OpenedBy = source,
                SourceRef = sourceRef,
                ChannelSeen = channel,
                RenewCount = 0
            });
        }
        else
        {
            existing.OpenedAt = now;
            existing.ExpiresAt = expires;
            existing.OpenedBy = source;
            existing.SourceRef = sourceRef ?? existing.SourceRef;
            existing.ChannelSeen = channel;
            existing.RenewCount += 1;
            existing.Phone = phone;
        }

        await _db.SaveChangesAsync(ct);
        await InvalidateAsync(phone, ct);

        _log.LogInformation(
            "🎁 FEP اتفتحت — عميل {CustomerId} ({Phone}) من {Source}, بتنتهي {Expires:u}",
            customerId, phone, source, expires);

        return expires;
    }

    // ══════════════════════════════════════════════════════════════════
    //  2. تجديد نافذة CSW (٢٤ ساعة) 🟡
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// بتتنده من **أي** رسالة داخلة، على أي قناة.
    ///
    /// 🔑 القاعدة الحديدية: العميل لو كلّمنا على الواتساب غير الرسمي، النافذة
    /// دي معتبرة مفتوحة كذلك (لأن الرقم واحد في نظر Meta... لأ، مش واحد).
    /// عشان كده بنسجّل <c>ChannelSeen</c> — عشان تعرف إن النافذة دي مفتوحة على
    /// أنهي رقم بالظبط. لكن على مستوى "المحادثة مع العميل" بنعتبرها واحدة
    /// لأن العميل شخص واحد وبيتوقع رد.
    /// </summary>
    public async Task<DateTimeOffset> TouchCswAsync(long customerId, string phone, string? messageId,
        ChannelKind channel, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(WindowDurations.CswHours);

        var existing = await _db.CustomerWindows
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.Kind == WindowKind.Csw, ct);

        if (existing is null)
        {
            _db.CustomerWindows.Add(new CustomerWindow
            {
                CustomerId = customerId,
                Phone = phone,
                Kind = WindowKind.Csw,
                OpenedAt = now,
                ExpiresAt = expires,
                OpenedBy = WindowSources.InboundMessage,
                SourceRef = messageId,
                ChannelSeen = channel,
                RenewCount = 0
            });
        }
        else
        {
            // ⚠️ مش بنغيّر OpenedAt — عشان نعرف بدأت إمتاي أول مرة
            existing.ExpiresAt = expires;
            existing.SourceRef = messageId ?? existing.SourceRef;
            existing.ChannelSeen = channel;
            existing.RenewCount += 1;
            existing.Phone = phone;
        }

        await _db.SaveChangesAsync(ct);
        await InvalidateAsync(phone, ct);

        _log.LogDebug("🟡 CSW اتجدّدت — عميل {CustomerId} على {Channel}, بتنتهي {Expires:u}",
            customerId, channel, expires);

        return expires;
    }

    // ══════════════════════════════════════════════════════════════════
    //  3. القراءة — المسار الحار 🔥
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// صيغة الكاش: <c>"fepTicks|cswTicks"</c> — أرخص من JSON بمراحل،
    /// و"-" معناها مفيش نافذة من النوع ده.
    /// </summary>
    public async Task<CustomerWindowState> GetStateAsync(string phone, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cacheKey = Key(phone);

        var cached = await _cache.GetAsync(cacheKey, ct);
        if (cached is not null && TryParse(cached, out var fepC, out var cswC))
            return CustomerWindowState.From(fepC, cswC, now);

        // ── قراءة من قاعدة البيانات ──
        // ⚠️ بنقرأ بالتليفون مش بالـ CustomerId عشان المسار الحار عنده التليفون
        //    بس (الرسالة الداخلة فيها رقم، مش id).
        var rows = await _db.CustomerWindows
            .Where(w => w.Phone == phone && w.ExpiresAt > now)
            .Select(w => new { w.Kind, w.ExpiresAt })
            .ToListAsync(ct);

        DateTimeOffset? fep = rows.Where(r => r.Kind == WindowKind.Fep)
                                  .Select(r => (DateTimeOffset?)r.ExpiresAt).Max();
        DateTimeOffset? csw = rows.Where(r => r.Kind == WindowKind.Csw)
                                  .Select(r => (DateTimeOffset?)r.ExpiresAt).Max();

        await _cache.SetAsync(cacheKey, Serialize(fep, csw), ComputeTtl(fep, csw, now), ct);

        return CustomerWindowState.From(fep, csw, now);
    }

    public Task InvalidateAsync(string phone, CancellationToken ct = default)
        => _cache.RemoveAsync(Key(phone), ct);

    // ══════════════════════════════════════════════════════════════════
    //  مساعدات
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔑 TTL = المدة لحد أقرب انتهاء نافذة، وبحد أقصى ٥ دقايق.
    ///
    /// ليه؟ لو نافذة CSW بتنتهي بعد ٣٠ ثانية، لازم الكاش يموت بعد ٣٠ ثانية
    /// مش بعد ٥ دقايق — وإلا هنبعت رسالة حرة على نافذة مقفولة وناخد 131047.
    /// وفي نفس الوقت لو النافذة فاضلها ٧٠ ساعة، مش معنى كده إن الكاش يعيش
    /// ٧٠ ساعة — لأن العميل ممكن يبعت رسالة تفتح CSW ونبقى مش عارفين.
    /// (وعلى أي حال بنعمل Invalidate في كل كتابة، ده حزام أمان تاني.)
    /// </summary>
    private static TimeSpan ComputeTtl(DateTimeOffset? fep, DateTimeOffset? csw, DateTimeOffset now)
    {
        var candidates = new List<TimeSpan>();
        if (fep.HasValue && fep.Value > now) candidates.Add(fep.Value - now);
        if (csw.HasValue && csw.Value > now) candidates.Add(csw.Value - now);

        // مفيش نوافذ خالص → نكاش النتيجة السلبية دقيقة واحدة بس
        if (candidates.Count == 0) return TimeSpan.FromMinutes(1);

        var nearest = candidates.Min();
        return nearest < MaxCacheTtl ? nearest : MaxCacheTtl;
    }

    private static string Serialize(DateTimeOffset? fep, DateTimeOffset? csw)
        => $"{(fep?.UtcTicks.ToString() ?? "-")}|{(csw?.UtcTicks.ToString() ?? "-")}";

    private static bool TryParse(string s, out DateTimeOffset? fep, out DateTimeOffset? csw)
    {
        fep = csw = null;
        var parts = s.Split('|');
        if (parts.Length != 2) return false;

        if (parts[0] != "-")
        {
            if (!long.TryParse(parts[0], out var t)) return false;
            fep = new DateTimeOffset(t, TimeSpan.Zero);
        }
        if (parts[1] != "-")
        {
            if (!long.TryParse(parts[1], out var t)) return false;
            csw = new DateTimeOffset(t, TimeSpan.Zero);
        }
        return true;
    }
}
