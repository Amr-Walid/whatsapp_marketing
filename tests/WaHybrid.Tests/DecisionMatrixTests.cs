using FluentAssertions;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Intents;
using WaHybrid.Domain.Windows;
using WaHybrid.Infrastructure.Core;

namespace WaHybrid.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════
///  🎯 بوابة القبول الرسمية — الـ ١٢ حالة. docs/10 §8.2
/// ═══════════════════════════════════════════════════════════════════
///
/// دي **مش اختبارات عادية**. دي العقد اللي المستند اتفق عليه:
/// لو الـ ١٢ حالة نجحت، يبقى المُوجّه بيتصرف صح.
/// لو حالة واحدة فشلت، يبقى فيه فلوس بتضيع أو حساب في خطر — حسب الحالة.
///
/// وكل حالة ليها **سبب مالي أو أمني** واضح:
///
///  ١. تسويق + FEP → رسمي/حر
///     🎁 دي أغلى فرصة في النظام. التسويق بـ $0.0300 في مصر، والـ FEP
///     بيخلّيه صفر. لو وجّهناه غير رسمي، ضيّعنا **جودة التسليم** ببلاش
///     (الرسمي مضمون ١٠٠٪) عشان نوفّر حاجة أصلاً مجانية.
///
///  ٢. تسويق + CSW → غير رسمي/حر
///     💰 هنا التوفير الحقيقي. النافذة مفتوحة فالرسالة الحرة مسموحة،
///     والعميل هو اللي بادر بالكلام فالخطر واطي. لو بعتنا رسمي، كنا
///     هندفع من فئتنا الرسالية (tier) بدون داعي.
///
///  ٣. تسويق + مفيش نافذة → رسمي/قالب
///     🔴 القاعدة الحديدية المالية: التسويق البارد **رسمي فقط**.
///     لو بعتنا تسويق بارد على غير رسمي = حظر الرقم = خسارة الأصل كله.
///     ندفع $0.0350 ونعيش، أحسن من نوفّرهم ونموت.
///
///  ٤-٦. تأكيد أوردر (حرج) → رسمي في كل الحالات
///     الحرج = الموثوقية أهم من التكلفة. لو العميل مستلمش تأكيد أوردره،
///     ده اتصال بخدمة العملاء + فقدان ثقة. الرسمي مضمون التسليم.
///
///  ٧. تم التوصيل + CSW → غير رسمي/حر
///     معاملة **غير حرجة** — لو وصلت متأخرة مفيش كارثة. فبنوفّر.
///
///  ٨. رد بوت + FEP → رسمي/حر
///     قاعدة التسليم (§4.4): طول ما فاضل من الـ FEP أكتر من ساعتين،
///     المحادثة تفضل رسمي — مجانية أصلاً، ومضمونة.
///
///  ٩. رد بوت + CSW → غير رسمي/حر
///     المحادثات هي أكبر حجم رسايل في أي نظام. توفيرها = التوفير الأكبر.
///
///  ١٠. رد بوت + مفيش نافذة → 🚫 مرفوض تماماً
///     ⚠️ أخطر حالة في المصفوفة كلها. مفيش قالب معتمد لـ "رد بوت"
///     (Meta مش هتعتمد قالب محتواه متغيّر بالكامل)، والرسالة الحرة
///     ممنوعة بره النافذة. فالنظام **لازم** يرفض.
///     لو النظام حاول يبعتها غير رسمي هنا → ده تسويق بارد فعلياً = حظر.
///     ولو حاول رسمي → Meta بترجّع 131047 والرسالة بتفشل.
///     الرفض هو **القرار الصح الوحيد**.
///
///  ١١. سؤال شائع + CSW → غير رسمي/حر (نفس منطق ٩)
///
///  ١٢. سلة متروكة + مفيش نافذة → رسمي/قالب
///     تسويق بره النافذة = قالب رسمي. نفس منطق ٣.
/// </summary>
public class DecisionMatrixTests
{
    /// <summary>
    /// الـ ١٢ حالة كـ MemberData — كل حالة اختبار منفصل في التقرير،
    /// فلو فشلت واحدة تعرف بالظبط أنهي واحدة.
    /// </summary>
    public static TheoryData<string, WindowState, ChannelKind?, SendMode?, string> Cases()
        => new()
        {
            // النية                       النافذة                  القناة المتوقعة        النمط المتوقع        السبب
            { IntentNames.CampaignPromo,   WindowState.FepOpen,   ChannelKind.Official,   SendMode.Free,     "🎁 FEP: التسويق نفسه مجاني — أغلى فرصة في النظام" },
            { IntentNames.CampaignPromo,   WindowState.CswOpen,   ChannelKind.Unofficial, SendMode.Free,     "💰 CSW: التوفير الحقيقي — نافذة مفتوحة وخطر واطي" },
            { IntentNames.CampaignPromo,   WindowState.NoWindow,  ChannelKind.Official,   SendMode.Template, "🔴 تسويق بارد = رسمي فقط، وإلا حظر" },

            { IntentNames.OrderConfirmed,  WindowState.FepOpen,   ChannelKind.Official,   SendMode.Free,     "حرج + FEP: مجاني ومضمون" },
            { IntentNames.OrderConfirmed,  WindowState.CswOpen,   ChannelKind.Official,   SendMode.Free,     "حرج: الموثوقية أهم من التكلفة" },
            { IntentNames.OrderConfirmed,  WindowState.NoWindow,  ChannelKind.Official,   SendMode.Template, "حرج بره النافذة: قالب معتمد" },

            { IntentNames.OrderDelivered,  WindowState.CswOpen,   ChannelKind.Unofficial, SendMode.Free,     "معاملة غير حرجة: نوفّر" },

            { IntentNames.BotReply,        WindowState.FepOpen,   ChannelKind.Official,   SendMode.Free,     "قاعدة التسليم §4.4: فاضل >ساعتين فنكمّل رسمي" },
            { IntentNames.BotReply,        WindowState.CswOpen,   ChannelKind.Unofficial, SendMode.Free,     "المحادثات = أكبر حجم = أكبر توفير" },
            { IntentNames.BotReply,        WindowState.NoWindow,  null,                   null,              "🚫 مرفوض: مفيش قالب للمحادثة، والحر ممنوع" },

            { IntentNames.FaqAnswer,       WindowState.CswOpen,   ChannelKind.Unofficial, SendMode.Free,     "محادثة داخل CSW: غير رسمي" },

            { IntentNames.AbandonedCart,   WindowState.NoWindow,  ChannelKind.Official,   SendMode.Template, "تسويق بره النافذة: قالب رسمي" }
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task المصفوفة_بتطلع_القرار_الصح(
        string intentName, WindowState window,
        ChannelKind? expectedChannel, SendMode? expectedMode, string why)
    {
        using var h = await new TestHarness().SeedBaseAsync();

        var spec = IntentRegistry.Get(intentName);
        var win = SyntheticWindow(window);

        var intent = NewIntent(intentName);

        var d = await h.Router.DecideAsync(spec, win, intent);

        if (expectedChannel is null)
        {
            // 🚫 الحالة رقم ١٠ — الرفض هو النجاح
            d.Allowed.Should().BeFalse(
                $"الحالة دي لازم ترفض. {why}. لكن النظام قرر: {d.Channel}/{d.Mode} ({d.Reason})");
            return;
        }

        d.Allowed.Should().BeTrue(
            $"الحالة دي لازم تنجح. {why}. لكن النظام رفض بسبب: {d.Reason}");

        d.Channel.Should().Be(expectedChannel,
            $"القناة غلط. {why}. السبب اللي رجع: {d.Reason}");

        d.Mode.Should().Be(expectedMode,
            $"نمط الإرسال غلط. {why}. السبب اللي رجع: {d.Reason}");
    }

    /// <summary>
    /// 📊 الاختبار التجميعي — بيطبع المصفوفة كلها ويتأكد من ١٢/١٢.
    ///
    /// الاختبار اللي فوق بيديك ١٢ نتيجة منفصلة (مفيد للتشخيص).
    /// الاختبار ده بيديك **حكم واحد** على المصفوفة كلها — وده اللي
    /// تعرضه في مراجعة الكود أو على المدير.
    /// </summary>
    [Fact]
    public async Task المصفوفة_كلها_١٢_على_١٢()
    {
        using var h = await new TestHarness().SeedBaseAsync();

        var rows = Cases().Select(r => (
            Intent: (string)r[0]!,
            Window: (WindowState)r[1]!,
            Channel: (ChannelKind?)r[2],
            Mode: (SendMode?)r[3],
            Why: (string)r[4]!
        )).ToList();

        var passed = 0;
        var failures = new List<string>();
        var report = new System.Text.StringBuilder();

        report.AppendLine();
        report.AppendLine("╔══════════════════════════════════════════════════════════════════════════╗");
        report.AppendLine("║  🎯 مصفوفة القرار — بوابة القبول الرسمية (docs/10 §8.2)                 ║");
        report.AppendLine("╚══════════════════════════════════════════════════════════════════════════╝");

        foreach (var (intentName, window, expCh, expMode, why) in rows)
        {
            var spec = IntentRegistry.Get(intentName);
            var d = await h.Router.DecideAsync(spec, SyntheticWindow(window),
                NewIntent(intentName));

            var gotCh = d.Allowed ? d.Channel : null;
            var gotMode = d.Allowed ? d.Mode : null;
            var ok = gotCh == expCh && gotMode == expMode;

            if (ok) passed++;
            else failures.Add(
                $"{intentName} + {window}: توقّعنا {Fmt(expCh, expMode)} "
                + $"وجالنا {Fmt(gotCh, gotMode)} — {d.Reason}");

            report.AppendLine(
                $"  {(ok ? "✅" : "❌")} {intentName,-18} {window,-10} → "
                + $"{Fmt(gotCh, gotMode),-24} {d.Reason}");
        }

        report.AppendLine();
        report.AppendLine($"  النتيجة النهائية: {passed}/{rows.Count}");
        report.AppendLine();

        // بيطلع في مخرجات الاختبار — مفيد للـ CI وللعرض
        Console.WriteLine(report.ToString());

        passed.Should().Be(rows.Count,
            "بوابة القبول لازم تعدّي ١٢/١٢. الفشل:\n  - " + string.Join("\n  - ", failures));
    }

    // ══════════════════════════════════════════════════════════════
    //  مساعدات
    // ══════════════════════════════════════════════════════════════

    private static string Fmt(ChannelKind? ch, SendMode? mode)
        => ch is null ? "🚫 مرفوض" : $"{ch}/{mode}";

    /// <summary>نية إرسال جاهزة بكل المتغيرات — عشان القوالب تلاقي اللي محتاجاه</summary>
    private static SendIntent NewIntent(string name) => new()
    {
        Name = name,
        CustomerId = 1,
        Phone = "201000000001",
        Body = "نص تجريبي",
        TemplateParams = DefaultParams()
    };

    /// <summary>
    /// بناء حالة نافذة **صناعية** بدون قاعدة بيانات.
    ///
    /// 🔑 ليه ده مهم؟ لأن <c>DecideAsync</c> اتصمّم عامد يكون **دالة نقية**
    /// بتاخد حالة النافذة كـ parameter مش بتقراها من القاعدة. النتيجة:
    ///   • الاختبار سريع جداً (مفيش I/O)
    ///   • ومحدد تماماً (مفيش وقت حقيقي بيأثر على النتيجة)
    ///   • وبنقدر نختبر نوافذ مستحيلة نصنعها في القاعدة
    ///
    /// ⚠️ بنحاكي الواقع بدقة: FEP دايماً بيجي مع CSW، لأن ضغطة الإعلان
    ///    نفسها رسالة داخلة بتفتح الـ CSW. فمستحيل يكون عندك FEP بدون CSW.
    /// </summary>
    private static CustomerWindowState SyntheticWindow(WindowState state)
    {
        var now = DateTimeOffset.UtcNow;
        return state switch
        {
            // فاضل ٦٠ ساعة من الـ ٧٢ — أكبر بكتير من حد الساعتين
            WindowState.FepOpen => CustomerWindowState.From(
                fep: now.AddHours(60), csw: now.AddHours(20), now: now),

            WindowState.CswOpen => CustomerWindowState.From(
                fep: null, csw: now.AddHours(18), now: now),

            // 🔑 نوافذ منتهية مش null — عشان نختبر إن الكود بيحسب
            //    الانتهاء صح، مش بس بيتعامل مع الغياب
            _ => CustomerWindowState.From(
                fep: now.AddHours(-5), csw: now.AddHours(-3), now: now)
        };
    }

    /// <summary>كل أسماء المتغيرات اللي أي قالب في النظام محتاجها</summary>
    internal static Dictionary<string, string> DefaultParams() => new()
    {
        ["name"] = "أحمد",
        ["order_id"] = "EG-10234",
        ["amount"] = "450",
        ["eta"] = "الثلاثاء",
        ["tracking"] = "TRK-99881",
        ["reason"] = "نفاد المخزون",
        ["offer"] = "خصم ٢٥٪",
        ["link"] = "shop.example.com/c/1"
    };
}
