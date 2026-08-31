using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Entities;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Windows;
using WaHybrid.Infrastructure.Core;

namespace WaHybrid.Infrastructure.Data;

/// <summary>
/// 🌱 بيانات العرض التوضيحي.
///
/// الهدف: أي حد يفتح النظام يلاقي **الحالات الثلاث** موجودة فعلاً
/// (FEP مفتوحة، CSW مفتوحة، مفيش نافذة) — عشان يشوف الـ Router بياخد
/// قرارات مختلفة على نفس النية حسب النافذة. ده جوهر العرض.
///
/// ⚠️ أرقام وهمية بالكامل (بادئة 2010xxxxxxx) — مفيش إرسال حقيقي.
/// </summary>
public static class DemoSeeder
{
    public static async Task SeedAsync(HybridDbContext db, ILogger log, CancellationToken ct = default)
    {
        if (await db.Customers.AnyAsync(ct))
        {
            log.LogInformation("🌱 البيانات موجودة بالفعل — تخطّي البذر");
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // ═══════════════ حالة الحساب الرسمي ═══════════════
        db.OfficialStatuses.Add(new OfficialStatus
        {
            Id = 1,
            PhoneNumberId = "DEMO_PHONE_ID",
            Tier = "TIER_1K",
            DailyLimit = 1000,
            UsedToday = 0,
            QualityRating = QualityRating.Green,
            ResetAt = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero),
            LastCheckedAt = now
        });

        // ═══════════════ الجلسات غير الرسمية ═══════════════
        // حصص من جدول الـ warmup في docs/03 — لاحظ إن الجلسة الجديدة
        // حصتها ٢٠ بس، والقديمة ١٥٠. ده مش تحكيم، ده منع حظر.
        db.WaSessions.AddRange(
            new WaSession
            {
                SessionId = "sess-main", Phone = "201000000001", Status = "active",
                WarmupDay = 45, DailyQuota = 150, SentToday = 0, RiskScore = 12,
                ProxyLabel = "proxy-eg-01", LastSeenAt = now
            },
            new WaSession
            {
                SessionId = "sess-backup", Phone = "201000000002", Status = "active",
                WarmupDay = 30, DailyQuota = 100, SentToday = 0, RiskScore = 20,
                ProxyLabel = "proxy-eg-02", LastSeenAt = now
            },
            new WaSession
            {
                SessionId = "sess-warming", Phone = "201000000003", Status = "warming",
                WarmupDay = 6, DailyQuota = 20, SentToday = 0, RiskScore = 5,
                ProxyLabel = "proxy-eg-03", LastSeenAt = now
            });

        // ═══════════════ القوالب ═══════════════
        db.WaTemplates.AddRange(TemplateRegistry.SeedTemplates());

        await db.SaveChangesAsync(ct);

        // ═══════════════ العملاء ═══════════════
        var customers = new List<(Customer c, string window)>();

        // ── مجموعة أ: ٦ عملاء في نافذة FEP 🎁 (جايين من إعلانات CTWA) ──
        string[] fepNames = ["أحمد محمود", "سارة علي", "محمد حسن", "نور إبراهيم", "خالد سمير", "مريم فؤاد"];
        for (var i = 0; i < fepNames.Length; i++)
            customers.Add((new Customer
            {
                Phone = $"20101000{i:D4}",
                Name = fepNames[i],
                Segment = i < 2 ? "champions" : "new",
                OptedIn = true, OptInSource = "ctwa_ad", OptedInAt = now.AddHours(-i - 1),
                AcquisitionSource = AcquisitionSource.Ctwa,
                CtwaClid = $"ctwa_demo_{i:D3}",
                Monetary = 500 + i * 250, Frequency = 1 + i, RecencyDays = i,
                Priority = 200 - i
            }, "fep"));

        // ── مجموعة ب: ٨ عملاء في نافذة CSW 🟡 (كلّمونا خلال ٢٤ ساعة) ──
        string[] cswNames = ["يوسف طارق", "دينا رمضان", "عمر شريف", "هبة ماهر",
                             "كريم عادل", "منى صلاح", "طارق أنور", "ريم ياسر"];
        for (var i = 0; i < cswNames.Length; i++)
            customers.Add((new Customer
            {
                Phone = $"20102000{i:D4}",
                Name = cswNames[i],
                Segment = i < 3 ? "loyal" : "potential",
                OptedIn = true, OptInSource = "inbound_message", OptedInAt = now.AddDays(-10 - i),
                AcquisitionSource = AcquisitionSource.Organic,
                Monetary = 1200 + i * 180, Frequency = 3 + i, RecencyDays = 1,
                Priority = 150 - i,
                // عميل واحد بيفضّل الرسمي صريح — عشان نوري قاعدة التفضيل شغّالة
                PreferredChannel = i == 0 ? ChannelKind.Official : null
            }, "csw"));

        // ── مجموعة ج: ٦ عملاء مفيش نافذة 🔴 (قوالب بس) ──
        string[] noneNames = ["حسام الدين", "أميرة نبيل", "مصطفى كامل",
                              "لبنى وحيد", "شادي جمال", "هدى عصام"];
        for (var i = 0; i < noneNames.Length; i++)
            customers.Add((new Customer
            {
                Phone = $"20103000{i:D4}",
                Name = noneNames[i],
                Segment = i < 3 ? "at_risk" : "hibernating",
                OptedIn = true, OptInSource = "import", OptedInAt = now.AddDays(-90 - i),
                AcquisitionSource = AcquisitionSource.Import,
                Monetary = 2500 - i * 300, Frequency = 8 - i, RecencyDays = 45 + i * 10,
                Priority = 100 - i
            }, "none"));

        // ── حالات حدّية: عميل عمل opt-out + عميل في قائمة الحظر ──
        var optedOut = new Customer
        {
            Phone = "201040000001", Name = "زياد (عمل إلغاء)", Segment = "lost",
            OptedIn = true, OptedOut = true, OptedOutAt = now.AddDays(-5),
            AcquisitionSource = AcquisitionSource.Import, Priority = 10
        };
        var blocked = new Customer
        {
            Phone = "201040000002", Name = "فادي (رقم غلط)", Segment = "lost",
            OptedIn = true, AcquisitionSource = AcquisitionSource.Import, Priority = 10
        };

        db.Customers.AddRange(customers.Select(x => x.c));
        db.Customers.AddRange(optedOut, blocked);
        await db.SaveChangesAsync(ct);

        db.SuppressionList.AddRange(
            new SuppressionEntry { Phone = optedOut.Phone, Reason = "opt_out", SeenOnChannel = ChannelKind.Unofficial },
            new SuppressionEntry { Phone = blocked.Phone, Reason = "invalid", SeenOnChannel = ChannelKind.Official });

        // ═══════════════ النوافذ ═══════════════
        foreach (var (c, kind) in customers)
        {
            switch (kind)
            {
                case "fep":
                    // 🎁 FEP فاضلها من ٦٠ لـ ٧٠ ساعة (اتفتحت قريب)
                    db.CustomerWindows.Add(new CustomerWindow
                    {
                        CustomerId = c.Id, Phone = c.Phone, Kind = WindowKind.Fep,
                        OpenedAt = now.AddHours(-4),
                        ExpiresAt = now.AddHours(WindowDurations.FepHours - 4),
                        OpenedBy = WindowSources.CtwaAd,
                        SourceRef = c.CtwaClid, ChannelSeen = ChannelKind.Official
                    });
                    // وضغطة الإعلان فتحت CSW كذلك (الرسالة الأولى)
                    db.CustomerWindows.Add(new CustomerWindow
                    {
                        CustomerId = c.Id, Phone = c.Phone, Kind = WindowKind.Csw,
                        OpenedAt = now.AddHours(-4),
                        ExpiresAt = now.AddHours(WindowDurations.CswHours - 4),
                        OpenedBy = WindowSources.InboundMessage,
                        ChannelSeen = ChannelKind.Official
                    });
                    break;

                case "csw":
                    // 🟡 CSW فاضلها من ساعتين لـ ٢٠ ساعة
                    db.CustomerWindows.Add(new CustomerWindow
                    {
                        CustomerId = c.Id, Phone = c.Phone, Kind = WindowKind.Csw,
                        OpenedAt = now.AddHours(-6),
                        ExpiresAt = now.AddHours(WindowDurations.CswHours - 6),
                        OpenedBy = WindowSources.InboundReply,
                        ChannelSeen = ChannelKind.Unofficial,
                        RenewCount = 3
                    });
                    break;

                // "none" → مفيش نوافذ خالص 🔴
            }
        }

        await db.SaveChangesAsync(ct);

        var counts = customers.GroupBy(x => x.window).ToDictionary(g => g.Key, g => g.Count());
        log.LogInformation(
            "🌱 البذر خلص: {Total} عميل | 🎁 FEP={Fep} 🟡 CSW={Csw} 🔴 بدون={None} | "
            + "{Templates} قالب | {Sessions} جلسة",
            customers.Count + 2, counts.GetValueOrDefault("fep"), counts.GetValueOrDefault("csw"),
            counts.GetValueOrDefault("none"), 5, 3);
    }
}
