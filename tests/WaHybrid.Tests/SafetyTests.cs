using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Intents;
using WaHybrid.Infrastructure.Webhooks;

namespace WaHybrid.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════
///  🛡️ اختبارات حدود الأمان — الحاجات اللي لو كسرت، تكسر الشركة
/// ═══════════════════════════════════════════════════════════════════
///
/// كل اختبار هنا بيحمي من كارثة محددة:
///   • منع التكرار    → العميل ياخد نفس الرسالة من القناتين
///   • قائمة الحظر    → نبعت لحد عمل opt-out = مخالفة قانونية
///   • الموافقة       → تسويق لحد مش موافق = شكاوى = حظر
///   • سقف التسويق    → 131049 من Meta
///   • حرس التكلفة    → فاتورة خارجة عن السيطرة
///   • التسويق البارد → حظر الرقم غير الرسمي
/// </summary>
public class SafetyTests
{
    // ══════════════════════════════════════════════════════════════
    //  🔁 منع التكرار
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void نفس_المدخلات_بتطلع_نفس_مفتاح_منع_التكرار_بالظبط()
    {
        var day = new DateOnly(2026, 8, 31);

        var a = IdempotencyKeyFactory.Create(1001, IntentNames.OrderConfirmed, null, day);
        var b = IdempotencyKeyFactory.Create(1001, IntentNames.OrderConfirmed, null, day);

        a.Should().Be(b, "المفتاح **حتمي** — نفس المدخلات = نفس الناتج دايماً");
        a.Should().HaveLength(32);

        // 🔑 أي مدخل بيتغيّر = مفتاح مختلف
        IdempotencyKeyFactory.Create(1002, IntentNames.OrderConfirmed, null, day)
            .Should().NotBe(a, "عميل مختلف");

        IdempotencyKeyFactory.Create(1001, IntentNames.OrderShipped, null, day)
            .Should().NotBe(a, "نية مختلفة");

        IdempotencyKeyFactory.Create(1001, IntentNames.OrderConfirmed, 77, day)
            .Should().NotBe(a, "حملة مختلفة");

        IdempotencyKeyFactory.Create(1001, IntentNames.OrderConfirmed, null, day.AddDays(1))
            .Should().NotBe(a, "يوم مختلف — عشان نفس النية تعدّي بكرة");
    }

    [Fact]
    public async Task المحاولة_التانية_لنفس_الرسالة_في_نفس_اليوم_بترفض()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201222222221", WindowState.CswOpen);

        var intent = Intent(c, IntentNames.OrderConfirmed);

        var first = await h.Sender.SendAsync(intent);
        var second = await h.Sender.SendAsync(intent);

        first.Ok.Should().BeTrue("الأولى لازم تنجح");
        second.Ok.Should().BeFalse("التانية لازم ترفض");
        second.BlockedByGate.Should().Be("gCrossChannelDedupe");

        // ⚠️ الأهم: المزوّد شاف رسالة **واحدة** بس.
        //    الرفض لازم يكون **قبل** الوصول للمزوّد، مش بعده.
        (h.Official.Sent.Count + h.Unofficial.Sent.Count).Should().Be(1);
    }

    [Fact]
    public async Task منع_التكرار_بيمشي_بين_القناتين_مش_على_قناة_واحدة()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        // FEP → الرسمي. لو غيّرنا لـ CSW → غير الرسمي.
        var c = await h.NewCustomerAsync("201222222222", WindowState.FepOpen);

        var r1 = await h.Sender.SendAsync(Intent(c, IntentNames.OrderDelivered));
        r1.Channel.Should().Be(ChannelKind.Official);

        // نقفل الـ FEP → التوجيه هيتغيّر لغير الرسمي
        var fep = await h.Db.CustomerWindows
            .FirstAsync(w => w.CustomerId == c.Id && w.Kind == WindowKind.Fep);
        fep.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        await h.Db.SaveChangesAsync();
        await h.Windows.InvalidateAsync(c.Phone);

        var r2 = await h.Sender.SendAsync(Intent(c, IntentNames.OrderDelivered));

        // 🔑 القناة اتغيّرت، لكن منع التكرار **لسه شغّال**.
        //    ده جوهر "منع التكرار بين القناتين": المفتاح مبنيّ على
        //    (عميل + نية + يوم) — مش على القناة. فالعميل مستحيل
        //    ياخد نفس الرسالة مرتين من قناتين مختلفتين.
        r2.Ok.Should().BeFalse();
        r2.BlockedByGate.Should().Be("gCrossChannelDedupe");
    }

    // ══════════════════════════════════════════════════════════════
    //  🚫 الحظر والموافقة
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task قائمة_الحظر_بتقطع_قبل_أي_بوابة_تانية()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201222222223", WindowState.CswOpen);

        h.Db.SuppressionList.Add(new Domain.Entities.SuppressionEntry
        {
            Phone = c.Phone, Reason = "opt_out"
        });
        await h.Db.SaveChangesAsync();

        // نجرّب أخطر نية: حرجة. لو الحظر بيتخطّى للحرج، ده خطأ قانوني.
        var r = await h.Sender.SendAsync(Intent(c, IntentNames.OrderConfirmed));

        r.Ok.Should().BeFalse();
        r.BlockedByGate.Should().Be("gSuppression",
            "الحظر أولوية ١٠ — أول بوابة في السلسلة، وبتقطع كل حاجة");

        (h.Official.Sent.Count + h.Unofficial.Sent.Count).Should().Be(0);
    }

    [Fact]
    public async Task التسويق_لعميل_مش_موافق_بيرفض()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201222222224", WindowState.CswOpen, optedIn: false);

        var r = await h.Sender.SendAsync(Intent(c, IntentNames.CampaignPromo));

        r.Ok.Should().BeFalse();
        r.BlockedByGate.Should().Be("gConsent");
    }

    [Fact]
    public async Task تأكيد_إلغاء_الاشتراك_بيعدّي_حتى_لو_العميل_عامل_opt_out()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201222222225", WindowState.CswOpen, optedIn: false);
        c.OptedOut = true;
        await h.Db.SaveChangesAsync();

        var r = await h.Sender.SendAsync(Intent(c, IntentNames.OptOutAck));

        // 🔑 الاستثناء الوحيد المنطقي: لازم تقدر تقوله "تم إلغاء اشتراكك".
        //    لو منعنا دي، العميل مش هيعرف إن الإلغاء نجح، وهيشتكي تاني.
        r.BlockedByGate.Should().NotBe("gConsent",
            "تأكيد الإلغاء لازم يعدّي بوابة الموافقة — وإلا العميل مش هيعرف إن إلغاءه نجح");
    }

    // ══════════════════════════════════════════════════════════════
    //  📊 السقوف
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task التسويق_التاني_في_نفس_الـ٢٤_ساعة_بيرفض()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201222222226", WindowState.CswOpen);

        // حملتين مختلفتين = مفتاحين مختلفين، فمنع التكرار مش هيمسكهم.
        // اللي لازم يمسكهم هو **سقف التسويق**.
        var r1 = await h.Sender.SendAsync(Intent(c, IntentNames.CampaignPromo, campaignId: 1));
        var r2 = await h.Sender.SendAsync(Intent(c, IntentNames.CampaignPromo, campaignId: 2));

        r1.Ok.Should().BeTrue();
        r2.Ok.Should().BeFalse("سقفنا رسالة تسويقية واحدة لكل عميل كل ٢٤ ساعة");
        r2.BlockedByGate.Should().BeOneOf("gGlobalFrequency", "gMetaFrequencyCap");
    }

    [Fact]
    public async Task الجودة_الحمراء_بتوقف_التسويق_وبتسيب_الحرج_يمشي()
    {
        using var h = await new TestHarness().SeedBaseAsync();

        // 🔴 نحاكي Meta وهي بتنزّل التقييم لأحمر
        await h.Tiers.RefreshFromMetaAsync("TIER_1K", 1000, QualityRating.Red);

        var st = await h.Db.OfficialStatuses.AsNoTracking().FirstAsync();
        st.MarketingPaused.Should().BeTrue("الأحمر لازم يوقف التسويق تلقائياً");

        // والحرج؟ لازم يفضل ماشي — إيقاف تأكيدات الأوردرات بسبب
        // مشكلة جودة تسويقية بيضاعف المشكلة مش بيحلّها.
        var c = await h.NewCustomerAsync("201222222227", WindowState.CswOpen);
        var r = await h.Sender.SendAsync(Intent(c, IntentNames.OrderConfirmed));
        r.Ok.Should().BeTrue("الرسايل الحرجة مالهاش علاقة بجودة التسويق");
    }

    // ══════════════════════════════════════════════════════════════
    //  💰 حرس التكلفة
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task الوقف_الصارم_بيمنع_التسويق_وبيسيب_الحرج()
    {
        using var h = await new TestHarness(o => o.Cost.DailyLimitUsd = 0.01m)
            .SeedBaseAsync();

        // نحرق الميزانية: صف في سجل الرسايل بتكلفة أكبر من الحد
        var burner = await h.NewCustomerAsync("201222222228");
        h.Db.MessageLogs.Add(new Domain.Entities.MessageLog
        {
            CustomerId = burner.Id, Phone = burner.Phone,
            Direction = MessageDirection.Out, Channel = ChannelKind.Official,
            Intent = IntentNames.CampaignPromo, WindowState = WindowState.NoWindow,
            SendMode = SendMode.Template, MetaCategory = MetaCategory.Marketing,
            Status = MessageStatus.Delivered, CostBilled = 5.00m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await h.Db.SaveChangesAsync();

        var budget = await h.Cost.CheckAsync();
        budget.HardStop.Should().BeTrue();

        var c = await h.NewCustomerAsync("201222222229", WindowState.NoWindow);

        var marketing = await h.Sender.SendAsync(Intent(c, IntentNames.CampaignPromo));
        marketing.Ok.Should().BeFalse("الميزانية خلصت — التسويق يستنى بكرة");

        var critical = await h.Sender.SendAsync(Intent(c, IntentNames.OrderConfirmed));
        critical.Ok.Should().BeTrue(
            "🔑 الوقف الصارم بيمنع غير الحرج بس. تأكيد أوردر دفع العميل تمنه "
            + "لازم يوصل حتى لو الميزانية اتخطّت — التكلفة $0.005 والبديل "
            + "اتصال بخدمة العملاء بـ $2");
    }

    // ══════════════════════════════════════════════════════════════
    //  🔴 القاعدة الحديدية: التسويق البارد رسمي فقط
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task التسويق_البارد_مابيروحش_غير_رسمي_ولا_لما_الرسمي_يقع()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201222222230", WindowState.NoWindow);

        // 💥 نوقّع القناة الرسمية
        h.Official.ForceDown = true;

        var r = await h.Sender.SendAsync(Intent(c, IntentNames.CampaignPromo));

        r.Ok.Should().BeFalse(
            "🔴 القاعدة الحديدية: لما الرسمي يقع، التسويق البارد **يستنى** — "
            + "مايتحوّلش لغير الرسمي. الرسالة الواحدة تكلفتها $0.035، "
            + "والحساب المحظور تكلفته الشركة كلها.");

        h.Unofficial.Sent.Should().BeEmpty(
            "⚠️ لو الاختبار ده فشل، يبقى النظام بيبعت تسويق بارد على "
            + "غير الرسمي — وده حظر مؤكد. ده أخطر اختبار في الملف كله.");
    }

    [Fact]
    public async Task الرسالة_الحرجة_بتلاقي_طريق_تاني_لما_قناة_تقع()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        // CSW مفتوحة → الحرج عادة رسمي، لكن الرسمي واقع
        var c = await h.NewCustomerAsync("201222222231", WindowState.CswOpen);

        h.Official.ForceDown = true;

        var r = await h.Sender.SendAsync(Intent(c, IntentNames.OrderConfirmed));

        // 🔑 الفرق عن الاختبار اللي فوق: النافذة **مفتوحة**، والنية **حرجة**.
        //    فغير الرسمي هنا مش تسويق بارد — العميل بادر بالكلام.
        //    الموثوقية أهم، فبنحوّل.
        r.Ok.Should().BeTrue("الحرج بياخد fallback لما النافذة مفتوحة");
        r.Channel.Should().Be(ChannelKind.Unofficial);
    }

    // ══════════════════════════════════════════════════════════════
    //  📥 مسار الدخول
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ضغطة_إعلان_بتفتح_FEP_وبتحوّل_التسويق_من_مدفوع_لمجاني()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201222222232", WindowState.NoWindow);
        var spec = IntentRegistry.Get(IntentNames.CampaignPromo);

        // قبل: مفيش نافذة → قالب مدفوع رسمي
        var before = await h.Router.RouteAsync(Intent(c, IntentNames.CampaignPromo));
        before.Channel.Should().Be(ChannelKind.Official);
        before.Mode.Should().Be(SendMode.Template,
            "بره النافذة = قالب مدفوع. في مصر ده $0.0300 + رسم الـ BSP");

        // 🎁 ضغطة إعلان — بنمرّ على نفس المطبّع والمعالج الحقيقيين
        var payload = OfficialCtwaPayload(c.Phone);
        var msgs = WebhookNormalizers.Official(payload);
        msgs.Should().HaveCount(1);
        msgs[0].Fep.Should().NotBeNull("لازم المطبّع يكتشف الـ referral");
        msgs[0].Fep!.Source.Should().Be(Domain.Windows.WindowSources.CtwaAd);

        await h.Inbound.HandleAsync(msgs[0]);

        // بعد: FEP مفتوحة → رسالة حرة مجانية
        var after = await h.Router.RouteAsync(Intent(c, IntentNames.CampaignPromo));
        after.Channel.Should().Be(ChannelKind.Official);
        after.Mode.Should().Be(SendMode.Free,
            "🎁 دي كل الحكاية: ضغطة إعلان واحدة حوّلت التسويق من قالب مدفوع "
            + "لرسالة حرة مجانية تماماً — وده بيفرق في حملة ألف عميل بحوالي $35");

        // والنافذة نفسها بتقول إن التسويق بقى مجاني
        var win = await h.Windows.GetStateAsync(c.Phone);
        win.State.Should().Be(WindowState.FepOpen);
        win.MarketingFree.Should().BeTrue();
    }

    [Fact]
    public void المطبّع_غير_الرسمي_بيتخطّى_رسايلنا_الطالعة()
    {
        // ⚠️ لو مااتخطّيناهاش، كل رسالة إحنا بنبعتها كانت هتفتح نافذة
        //    CSW على نفسها — والنظام هيفضل شايف كل العملاء نوافذهم
        //    مفتوحة للأبد، وهيبعت رسايل حرة بره النافذة وتفشل كلها.
        var json = """
        {"event":"messages.upsert","data":{
          "key":{"remoteJid":"201555555555@s.whatsapp.net","fromMe":true,"id":"X1"},
          "message":{"conversation":"رسالة إحنا بعتناها"},
          "messageTimestamp":1788000000}}
        """;

        WebhookNormalizers.Unofficial(json).Should().BeEmpty();
    }

    [Fact]
    public void المطبّع_غير_الرسمي_بيتخطّى_الجروبات()
    {
        var json = """
        {"event":"messages.upsert","data":{
          "key":{"remoteJid":"20111111-12345@g.us","fromMe":false,"id":"X2"},
          "message":{"conversation":"رسالة في جروب"},
          "messageTimestamp":1788000000}}
        """;

        WebhookNormalizers.Unofficial(json).Should().BeEmpty(
            "إحنا بنتعامل مع أفراد — الجروبات مش عملاء");
    }

    [Fact]
    public async Task كلمة_إلغاء_بتعمل_opt_out_وبتضيف_للحظر()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201222222233", WindowState.CswOpen);

        var msgs = WebhookNormalizers.Unofficial(UnofficialPayload(c.Phone, "إلغاء"));

        var r = await h.Inbound.HandleAsync(msgs[0]);

        r.OptedOut.Should().BeTrue();
        (await h.Db.SuppressionList.AnyAsync(s => s.Phone == c.Phone))
            .Should().BeTrue("الحظر لازم يتسجّل عشان يمشي على القناتين");

        // وأي محاولة إرسال بعد كده بترفض
        var send = await h.Sender.SendAsync(Intent(c, IntentNames.CampaignPromo));
        send.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task جملة_طويلة_فيها_كلمة_إلغاء_مابتتحسبش_opt_out()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201222222234", WindowState.CswOpen);

        // 🔑 حماية من إيجاب زائف: "مش عايز إلغاء الأوردر" معناها العكس تماماً
        var msgs = WebhookNormalizers.Unofficial(UnofficialPayload(c.Phone,
            "لا لا مش عايز إلغاء الأوردر خلاص أنا موافق عليه وهستلمه بكرة إن شاء الله"));

        var r = await h.Inbound.HandleAsync(msgs[0]);
        r.OptedOut.Should().BeFalse("الجملة الطويلة مش أمر إلغاء");
    }

    // ══════════════════════════════════════════════════════════════
    //  🔐 توقيع الـ webhook
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void التوقيع_الصح_بيعدّي_والغلط_بيرفض()
    {
        const string secret = "app_secret_demo";
        var body = System.Text.Encoding.UTF8.GetBytes("""{"entry":[]}""");

        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var sig = "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();

        WebhookSignature.Verify(body, sig, secret).Should().BeTrue();

        WebhookSignature.Verify(body, "sha256=deadbeef", secret).Should().BeFalse();
        WebhookSignature.Verify(body, sig, "wrong_secret").Should().BeFalse();
        WebhookSignature.Verify(body, null, secret).Should().BeFalse();
        WebhookSignature.Verify(body, sig, null).Should().BeFalse("مفيش secret = مفيش ثقة");

        // بايت واحد بيتغيّر = التوقيع بيسقط
        var tampered = System.Text.Encoding.UTF8.GetBytes("""{"entry":[1]}""");
        WebhookSignature.Verify(tampered, sig, secret).Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════
    //  🧹 فحص القوالب
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void فاحص_القوالب_بيمسك_الأخطاء_اللي_Meta_بترفض_بسببها()
    {
        // متغيّر في الأول → رفض شبه مؤكد من Meta
        Infrastructure.Core.TemplateRegistry
            .Lint("{{1}} أهلاً بك", MetaCategory.Utility)
            .Should().NotBeEmpty();

        // متغيّرين ملزوقين → Meta مش بتقبل
        Infrastructure.Core.TemplateRegistry
            .Lint("أهلاً {{1}}{{2}} معاك", MetaCategory.Utility)
            .Should().NotBeEmpty();

        // ترقيم مش متسلسل
        Infrastructure.Core.TemplateRegistry
            .Lint("أوردر {{1}} بمبلغ {{3}}", MetaCategory.Utility)
            .Should().NotBeEmpty();

        // ⚠️ الأخطر: كلمات تسويقية في قالب Utility.
        //    Meta بتعيد تصنيفه لـ Marketing، والسعر بيقفز من
        //    $0.005 لـ $0.030 — ٦ أضعاف — وانت مش واخد بالك.
        Infrastructure.Core.TemplateRegistry
            .Lint("أوردر {{1}} جاهز — وعندنا خصم ٥٠٪ النهاردة!", MetaCategory.Utility)
            .Should().NotBeEmpty();

        // وقالب سليم يعدّي نضيف
        Infrastructure.Core.TemplateRegistry
            .Lint("أهلاً {{1}}، أوردرك رقم {{2}} اتأكد وجاهز للتوصيل.",
                MetaCategory.Utility)
            .Should().BeEmpty();
    }

    [Fact]
    public void القوالب_المبذورة_كلها_بتعدّي_الفاحص()
    {
        // لو قالب من قوالبنا الأساسية بيفشل الفاحص، يبقى إحنا بنقدّم
        // لـ Meta حاجة هترفض — وده أسبوع تعطيل.
        foreach (var t in Infrastructure.Core.TemplateRegistry.SeedTemplates())
        {
            Infrastructure.Core.TemplateRegistry
                .Lint(t.BodyText, t.Category)
                .Should().BeEmpty($"القالب '{t.Name}' لازم يعدّي الفاحص");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  🎛️ مفتاح الإيقاف
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task مفتاح_الإيقاف_العام_بيوقّف_كل_حاجة_حتى_الحرج()
    {
        using var h = await new TestHarness().SeedBaseAsync();
        var c = await h.NewCustomerAsync("201222222235", WindowState.CswOpen);

        await h.Cache.SetAsync("kill:global", "1", TimeSpan.FromHours(1));

        var r = await h.Sender.SendAsync(Intent(c, IntentNames.OrderConfirmed));

        // 🔴 الفرق عن الوقف المالي: ده مفتاح **يدوي** بيتضغط في حالة
        //    كارثة (تسريب، خطأ في حملة، مشكلة قانونية). لما تضغطه،
        //    كل حاجة بتقف — حتى الحرج. مفيش استثناءات.
        r.Ok.Should().BeFalse();
        (h.Official.Sent.Count + h.Unofficial.Sent.Count).Should().Be(0);
    }

    // ══════════════════════════════════════════════════════════════
    //  🗂️ سلسلة البوابات
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void البوابات_مرتّبة_بالترتيب_الصح()
    {
        using var h = new TestHarness();
        var chain = (Infrastructure.Gates.GateChain)h.Gates;

        var orders = chain.Gates.Select(g => g.Order).ToList();
        orders.Should().BeInAscendingOrder(
            "الترتيب مهم: الأرخص والأقطع الأول. مفيش داعي تسأل Meta "
            + "عن الفئة الرسالية لعميل موجود أصلاً في قائمة الحظر.");

        // أول بوابة لازم تكون الحظر — أرخص فحص وأقطع نتيجة
        chain.Gates.First().Name.Should().Be("gSuppression");

        // وآخر بوابة جاهزية القالب — أغلى فحص، بنعمله لما نبقى
        // متأكدين إن كل حاجة تانية عدّت
        chain.Gates.Last().Name.Should().Be("gTemplateReady");
    }

    // ══════════════════════════════════════════════════════════════
    //  📋 التخطيط
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task تخطيط_الحملة_مابيبعتش_ولا_رسالة()
    {
        using var h = await new TestHarness().SeedBaseAsync();

        for (var i = 0; i < 5; i++)
            await h.NewCustomerAsync($"20133300000{i}", WindowState.CswOpen);

        var plan = await h.Planner.PlanAsync(IntentNames.CampaignPromo, null);

        plan.TotalTargeted.Should().Be(5);
        (plan.Official + plan.Unofficial).Should().BeGreaterThan(0);

        // 🔑 ده جوهر الـ dry-run: قبل ما تصرف مليم، تعرف الحملة هتكلّف
        //    كام وتوصل لكام. لو الرقم مش عاجبك، تعدّل القطاع أو تستنى
        //    نوافذ تتفتح — كله من غير أي مخاطرة.
        (h.Official.Sent.Count + h.Unofficial.Sent.Count).Should().Be(0,
            "⚠️ التخطيط قراءة بس — أي إرسال هنا يبقى خطأ خطير");
    }

    // ══════════════════════════════════════════════════════════════
    //  مساعدات
    // ══════════════════════════════════════════════════════════════

    private static SendIntent Intent(Domain.Entities.Customer c, string name,
        long? campaignId = null) => new()
    {
        Name = name,
        CustomerId = c.Id,
        Phone = c.Phone,
        Body = "نص تجريبي للرسالة",
        CampaignId = campaignId,
        TemplateParams = DecisionMatrixTests.DefaultParams()
    };

    /// <summary>
    /// بناء payload رسمي فيه referral بتاع إعلان CTWA.
    ///
    /// ⚠️ ملاحظة عن الأسلوب: بنستخدم raw string عادي + Replace مش
    /// interpolation، لأن الـ JSON جوه أقواس متتالية (}}}) ودي بتتعارض
    /// مع قواعد الـ escaping في الـ interpolated raw strings.
    /// الـ Replace أوضح ومفيه لبس.
    /// </summary>
    private static string OfficialCtwaPayload(string phone) => """
    {"object":"whatsapp_business_account","entry":[{"id":"W","changes":[{"field":"messages","value":{
      "messaging_product":"whatsapp",
      "contacts":[{"profile":{"name":"عميل"},"wa_id":"__PHONE__"}],
      "messages":[{"from":"__PHONE__","id":"wamid.T1","timestamp":"1788000000","type":"text",
        "text":{"body":"شفت الإعلان"},
        "referral":{"source_type":"ad","source_id":"AD1","headline":"خصم ٢٥٪","ctwa_clid":"clid1"}}]
    }}]}]}
    """.Replace("__PHONE__", phone);

    /// <summary>بناء payload Evolution لرسالة داخلة حقيقية</summary>
    private static string UnofficialPayload(string phone, string text) => """
    {"event":"messages.upsert","data":{
      "key":{"remoteJid":"__PHONE__@s.whatsapp.net","fromMe":false,"id":"X9"},
      "pushName":"عميل",
      "message":{"conversation":"__TEXT__"},
      "messageTimestamp":1788000000}}
    """.Replace("__PHONE__", phone).Replace("__TEXT__", text);
}
