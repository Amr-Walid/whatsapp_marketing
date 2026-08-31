using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Windows;

namespace WaHybrid.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════
///  🪟 اختبارات متتبّع النوافذ
/// ═══════════════════════════════════════════════════════════════════
///
/// النوافذ هي **مصدر الحقيقة المالية** في النظام كله. لو المتتبّع غلط:
///   • نافذة مفتوحة والنظام شايفها مقفولة → بنبعت بقالب مدفوع ($0.035)
///     حاجة كانت مجانية. خسارة مباشرة.
///   • نافذة مقفولة والنظام شايفها مفتوحة → بنبعت رسالة حرة، Meta
///     بترجّع 131047، الرسالة بتفشل، والعميل مستلمش حاجة.
///
/// فالاختبارات دي بتحمي الفلوس وبتحمي التسليم في نفس الوقت.
/// </summary>
public class WindowTrackerTests
{
    [Fact]
    public async Task فتح_FEP_بيرجّع_٧٢_ساعة_وبيسجّل_في_القاعدة()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201111111111");

        var before = DateTimeOffset.UtcNow;
        var until = await h.Windows.OpenFepAsync(c.Id, c.Phone,
            WindowSources.CtwaAd, "ad_123", ChannelKind.Official);

        // ٧٢ ساعة بالظبط (بهامش دقيقة للتنفيذ)
        (until - before).TotalHours.Should().BeApproximately(WindowDurations.FepHours, 0.02);

        var row = await h.Db.CustomerWindows
            .FirstAsync(w => w.CustomerId == c.Id && w.Kind == WindowKind.Fep);

        row.OpenedBy.Should().Be(WindowSources.CtwaAd);
        row.SourceRef.Should().Be("ad_123");
    }

    [Fact]
    public async Task تجديد_CSW_بيرجّع_٢٤_ساعة_وبيحافظ_على_وقت_الفتح_الأصلي()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201111111112");

        await h.Windows.TouchCswAsync(c.Id, c.Phone, "m1", ChannelKind.Unofficial);
        var first = await h.Db.CustomerWindows
            .AsNoTracking()
            .FirstAsync(w => w.CustomerId == c.Id && w.Kind == WindowKind.Csw);

        await Task.Delay(30);
        var until = await h.Windows.TouchCswAsync(c.Id, c.Phone, "m2", ChannelKind.Official);

        var rows = await h.Db.CustomerWindows
            .Where(w => w.CustomerId == c.Id && w.Kind == WindowKind.Csw)
            .ToListAsync();

        // 🔑 صف واحد بس — بنحدّث مش بنضيف. لو ضفنا صف كل رسالة داخلة،
        //    جدول النوافذ كان هيبقى أكبر من جدول الرسايل نفسه.
        rows.Should().HaveCount(1, "لازم صف واحد لكل (عميل, نوع نافذة)");

        // وقت الفتح الأصلي محفوظ — مهم للتحليلات (طول المحادثة)
        rows[0].OpenedAt.Should().BeCloseTo(first.OpenedAt, TimeSpan.FromMilliseconds(5));

        // بس الانتهاء اتمدّ ٢٤ ساعة جديدة من دلوقتي
        (until - DateTimeOffset.UtcNow).TotalHours
            .Should().BeApproximately(WindowDurations.CswHours, 0.02);

        rows[0].RenewCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FEP_له_الأسبقية_على_CSW()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        // العميل ده عنده الاتنين مفتوحين — الحالة الطبيعية بعد ضغطة إعلان
        var c = await h.NewCustomerAsync("201111111113", WindowState.FepOpen);

        var s = await h.Windows.GetStateAsync(c.Phone);

        // 🎁 الأسبقية: FEP > CSW > مفيش. لأن الـ FEP أوسع صلاحيات
        //    (بيخلّي التسويق مجاني، والـ CSW لأ).
        s.State.Should().Be(WindowState.FepOpen);
        s.MarketingFree.Should().BeTrue("الـ FEP بيخلّي التسويق نفسه مجاني");
        s.FreeFormAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CSW_لوحدها_بتسمح_بالحر_بس_مش_بالتسويق_المجاني()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201111111114", WindowState.CswOpen);

        var s = await h.Windows.GetStateAsync(c.Phone);

        s.State.Should().Be(WindowState.CswOpen);
        s.FreeFormAllowed.Should().BeTrue("النافذة مفتوحة فالرسالة الحرة مسموحة");
        s.MarketingFree.Should().BeFalse(
            "⚠️ الفرق الجوهري: الـ CSW مش بيخلّي التسويق مجاني — الـ FEP بس اللي بيعمل كده");
    }

    [Fact]
    public async Task النافذة_المنتهية_مابتتحسبش_مفتوحة()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201111111115");

        // نضيف نافذتين منتهيتين يدوياً — بنحاكي عميل قديم
        h.Db.CustomerWindows.AddRange(
            new Domain.Entities.CustomerWindow
            {
                CustomerId = c.Id, Phone = c.Phone, Kind = WindowKind.Fep,
                OpenedAt = DateTimeOffset.UtcNow.AddHours(-80),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(-8),
                OpenedBy = WindowSources.CtwaAd
            },
            new Domain.Entities.CustomerWindow
            {
                CustomerId = c.Id, Phone = c.Phone, Kind = WindowKind.Csw,
                OpenedAt = DateTimeOffset.UtcNow.AddHours(-30),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(-6),
                OpenedBy = WindowSources.InboundMessage
            });
        await h.Db.SaveChangesAsync();

        var s = await h.Windows.GetStateAsync(c.Phone);

        s.State.Should().Be(WindowState.NoWindow);
        s.FreeFormAllowed.Should().BeFalse();
        s.MarketingFree.Should().BeFalse();
    }

    [Fact]
    public async Task إبطال_الكاش_بعد_فتح_نافذة_بيخلّي_القراءة_تشوف_الجديد()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201111111116");

        // ١. قراءة أولى → بتخزّن "مفيش نافذة" في الكاش
        var s1 = await h.Windows.GetStateAsync(c.Phone);
        s1.State.Should().Be(WindowState.NoWindow);

        // ٢. نفتح FEP
        await h.Windows.OpenFepAsync(c.Id, c.Phone, WindowSources.CtwaAd, null,
            ChannelKind.Official);

        // ٣. ⚠️ لحظة الحقيقة: لو الكاش مابيتبطّلش، القراءة دي هتفضل
        //    ترجّع "مفيش نافذة" لحد ٥ دقايق — وخلال الـ ٥ دقايق دي
        //    كل رسالة تتبعت للعميل هتتكلّف $0.035 بدون داعي.
        var s2 = await h.Windows.GetStateAsync(c.Phone);
        s2.State.Should().Be(WindowState.FepOpen, "الكاش لازم يتبطّل بعد أي كتابة");
    }

    [Fact]
    public async Task العميل_الجديد_تماماً_مفيش_عنده_نوافذ()
    {
        using var h = await new TestHarness().SeedBaseAsync();

        // رقم مش موجود في القاعدة خالص
        var s = await h.Windows.GetStateAsync("209999999999");

        s.State.Should().Be(WindowState.NoWindow);
        s.FepUntil.Should().BeNull();
        s.CswUntil.Should().BeNull();
    }
}
