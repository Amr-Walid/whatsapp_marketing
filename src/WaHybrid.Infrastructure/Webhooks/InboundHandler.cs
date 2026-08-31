using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WaHybrid.Domain.Contracts;
using WaHybrid.Domain.Entities;
using WaHybrid.Domain.Enums;
using WaHybrid.Domain.Windows;
using WaHybrid.Infrastructure.Data;

namespace WaHybrid.Infrastructure.Webhooks;

/// <summary>
/// رسالة داخلة **موحّدة** — بعد التطبيع. docs/09 §7.
///
/// 🔑 الفكرة: الرسمي وغير الرسمي بيبعتوا JSON مختلف تماماً في الشكل.
/// بنطبّعهم للشكل الواحد ده، وبعد كده باقي النظام **مش بيعرف** الرسالة
/// جت من فين. ده تطبيق القاعدة الحديدية على مسار الدخول.
/// </summary>
public sealed record NormalizedInbound(
    ChannelKind Channel,
    string Phone,
    string? MessageId,
    string Type,
    string? Text,
    DateTimeOffset At,
    /// <summary>🎁 بيانات ضغطة إعلان CTWA — لو موجودة، دي نافذة ٧٢ ساعة مجانية</summary>
    FepSignal? Fep,
    string? ProfileName);

/// <summary>
/// إشارة FEP. docs/09 §3.5.
/// موجودة في الرسمي بس — غير الرسمي مش بيشوف بيانات الإعلانات.
/// </summary>
public sealed record FepSignal(string Source, string? SourceId, string? Headline, string? CtwaClid);

// ══════════════════════════════════════════════════════════════════════
//  المطبّعات
// ══════════════════════════════════════════════════════════════════════

public static class WebhookNormalizers
{
    /// <summary>
    /// تطبيع webhook الرسمي (Meta Cloud API).
    ///
    /// الشكل: entry[].changes[].value.messages[]
    /// و الـ <c>referral</c> جوه الرسالة نفسها هو اللي بيقول إن العميل
    /// جاي من إعلان.
    /// </summary>
    public static List<NormalizedInbound> Official(string json)
    {
        var result = new List<NormalizedInbound>();

        JsonNode? root;
        try { root = JsonNode.Parse(json); } catch { return result; }

        var entries = root?["entry"]?.AsArray();
        if (entries is null) return result;

        foreach (var entry in entries)
        {
            var changes = entry?["changes"]?.AsArray();
            if (changes is null) continue;

            foreach (var change in changes)
            {
                var value = change?["value"];
                var messages = value?["messages"]?.AsArray();
                if (messages is null) continue;

                var contacts = value?["contacts"]?.AsArray();
                var profileName = contacts?.FirstOrDefault()?["profile"]?["name"]?.ToString();

                foreach (var m in messages)
                {
                    if (m is null) continue;

                    var from = m["from"]?.ToString();
                    if (string.IsNullOrEmpty(from)) continue;

                    var type = m["type"]?.ToString() ?? "text";
                    var text = type switch
                    {
                        "text" => m["text"]?["body"]?.ToString(),
                        "button" => m["button"]?["text"]?.ToString(),
                        "interactive" => m["interactive"]?["button_reply"]?["title"]?.ToString()
                                      ?? m["interactive"]?["list_reply"]?["title"]?.ToString(),
                        _ => m[type]?["caption"]?.ToString()
                    };

                    var ts = long.TryParse(m["timestamp"]?.ToString(), out var t)
                        ? DateTimeOffset.FromUnixTimeSeconds(t)
                        : DateTimeOffset.UtcNow;

                    result.Add(new NormalizedInbound(
                        ChannelKind.Official, from, m["id"]?.ToString(), type, text, ts,
                        DetectFep(m), profileName));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 🎁 كشف نافذة الدخول المجاني. docs/09 §3.5.
    ///
    /// ⚠️ ده **أهم ٢٠ سطر في مسار الدخول كله** — لأن كل ضغطة إعلان
    /// بتفوتنا هنا معناها ٧٢ ساعة مجانية ضاعت، وبنبعت بقالب بـ $0.035
    /// حاجة كان ممكن تكون مجانية.
    ///
    ///   source_type = "ad"   → ضغط إعلان Click-to-WhatsApp
    ///   source_type = "page" → ضغط زرار "راسلنا" على صفحة فيسبوك
    ///
    /// و<c>ctwa_clid</c> هو المعرّف اللي بيربط المحادثة بالإعلان في
    /// Ads Manager — بدونه مش هتعرف أنهي إعلان جاب أنهي عميل.
    /// </summary>
    private static FepSignal? DetectFep(JsonNode msg)
    {
        var referral = msg["referral"];
        if (referral is null) return null;

        var sourceType = referral["source_type"]?.ToString();
        if (sourceType is not ("ad" or "page")) return null;

        return new FepSignal(
            Source: sourceType == "ad" ? WindowSources.CtwaAd : WindowSources.PageCta,
            SourceId: referral["source_id"]?.ToString(),
            Headline: referral["headline"]?.ToString(),
            CtwaClid: referral["ctwa_clid"]?.ToString());
    }

    /// <summary>
    /// تطبيع webhook غير رسمي (Evolution API).
    /// الشكل: data.key.remoteJid + data.message.conversation
    /// </summary>
    public static List<NormalizedInbound> Unofficial(string json)
    {
        var result = new List<NormalizedInbound>();

        JsonNode? root;
        try { root = JsonNode.Parse(json); } catch { return result; }

        var data = root?["data"];
        if (data is null) return result;

        // Evolution بيبعت أحياناً مصفوفة وأحياناً كائن واحد
        var items = data is JsonArray arr ? arr.ToList() : [data];

        foreach (var d in items)
        {
            if (d is null) continue;

            var jid = d["key"]?["remoteJid"]?.ToString();
            if (string.IsNullOrEmpty(jid)) continue;

            // 🔑 نتخطّى الجروبات والقنوات — إحنا بنتعامل مع أفراد بس
            if (jid.Contains("@g.us") || jid.Contains("@broadcast")) continue;

            // 🔑 والأهم: نتخطّى رسايلنا الطالعة (fromMe) وإلا هنفتح نافذة
            //    CSW على رسالة إحنا بعتناها — وده غلط منطقي خطير.
            if (d["key"]?["fromMe"]?.GetValue<bool>() == true) continue;

            var phone = jid.Split('@')[0];

            var msg = d["message"];
            var text = msg?["conversation"]?.ToString()
                    ?? msg?["extendedTextMessage"]?["text"]?.ToString()
                    ?? msg?["imageMessage"]?["caption"]?.ToString();

            var type = msg?["imageMessage"] is not null ? "image"
                     : msg?["audioMessage"] is not null ? "audio"
                     : msg?["documentMessage"] is not null ? "document"
                     : "text";

            var ts = long.TryParse(d["messageTimestamp"]?.ToString(), out var t)
                ? DateTimeOffset.FromUnixTimeSeconds(t)
                : DateTimeOffset.UtcNow;

            result.Add(new NormalizedInbound(
                ChannelKind.Unofficial, phone, d["key"]?["id"]?.ToString(),
                type, text, ts,
                Fep: null,   // غير الرسمي مش بيشوف بيانات الإعلانات
                ProfileName: d["pushName"]?.ToString()));
        }

        return result;
    }
}

// ══════════════════════════════════════════════════════════════════════
//  المعالج الموحّد
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// المعالج الموحّد للرسايل الداخلة. docs/09 §7.3.
///
/// 🔑 الميثود دي هي اللي بتخلّي النظام "دماغ واحدة": مهما كانت الرسالة
/// جت من الرسمي أو غير الرسمي، نفس المنطق بالظبط بيتنفّذ:
///   ١. لاقي العميل أو اعمله (upsert)
///   ٢. 🎁 لو فيه إشارة FEP → افتح ٧٢ ساعة
///   ٣. 🟡 دايماً جدّد CSW ٢٤ ساعة
///   ٤. لو "إلغاء" → opt-out على القناتين
///   ٥. سجّل الرسالة الداخلة
/// </summary>
public sealed class InboundHandler
{
    /// <summary>كلمات إلغاء الاشتراك — بالعربي والإنجليزي والعامية</summary>
    private static readonly string[] OptOutWords =
        ["إلغاء", "الغاء", "بلاش", "توقف", "امسحني", "لا أريد", "لا اريد",
         "stop", "unsubscribe", "cancel", "remove"];

    private readonly HybridDbContext _db;
    private readonly IWindowTracker _windows;
    private readonly ILogger<InboundHandler> _log;

    public InboundHandler(HybridDbContext db, IWindowTracker windows, ILogger<InboundHandler> log)
        => (_db, _windows, _log) = (db, windows, log);

    public async Task<InboundResult> HandleAsync(NormalizedInbound msg, CancellationToken ct = default)
    {
        // ١️⃣ العميل — upsert
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Phone == msg.Phone, ct);
        var isNew = customer is null;

        if (customer is null)
        {
            customer = new Customer
            {
                Phone = msg.Phone,
                Name = msg.ProfileName,
                // 🔑 عميل جاي من إعلان = مصدره CTWA، وده أعلى قيمة من الاستيراد
                AcquisitionSource = msg.Fep is not null
                    ? AcquisitionSource.Ctwa
                    : AcquisitionSource.Organic,
                CtwaClid = msg.Fep?.CtwaClid,
                // العميل بادر بالكلام = موافقة ضمنية سياقية
                OptedIn = true,
                OptInSource = msg.Fep is not null ? "ctwa_ad" : "inbound_message",
                OptedInAt = DateTimeOffset.UtcNow
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(ct);

            _log.LogInformation("👤 عميل جديد {Phone} من {Source}",
                msg.Phone, customer.AcquisitionSource);
        }
        else
        {
            if (customer.Name is null && msg.ProfileName is not null)
                customer.Name = msg.ProfileName;
            if (msg.Fep?.CtwaClid is not null)
                customer.CtwaClid = msg.Fep.CtwaClid;
        }

        // ٢️⃣ 🎁 نافذة FEP — ٧٢ ساعة كل حاجة مجاناً
        DateTimeOffset? fepUntil = null;
        if (msg.Fep is not null)
        {
            fepUntil = await _windows.OpenFepAsync(customer.Id, msg.Phone,
                msg.Fep.Source, msg.Fep.SourceId, msg.Channel, ct);

            _log.LogInformation(
                "🎁 نافذة FEP اتفتحت لـ {Phone} من إعلان '{Headline}' — مجانية لحد {Until:u}",
                msg.Phone, msg.Fep.Headline, fepUntil);
        }

        // ٣️⃣ 🟡 CSW — دايماً، أي رسالة داخلة بتجدّد الـ ٢٤ ساعة
        var cswUntil = await _windows.TouchCswAsync(customer.Id, msg.Phone,
            msg.MessageId, msg.Channel, ct);

        // ٤️⃣ إلغاء الاشتراك
        var optedOut = false;
        if (IsOptOut(msg.Text))
        {
            customer.OptedOut = true;
            customer.OptedOutAt = DateTimeOffset.UtcNow;

            var suppressed = await _db.SuppressionList.AnyAsync(s => s.Phone == msg.Phone, ct);
            if (!suppressed)
                _db.SuppressionList.Add(new SuppressionEntry
                {
                    Phone = msg.Phone,
                    Reason = "opt_out",
                    SeenOnChannel = msg.Channel
                });

            optedOut = true;
            _log.LogWarning("🚫 {Phone} عمل opt-out — الحظر بيمشي على القناتين", msg.Phone);
        }

        // ٥️⃣ سجّل الرسالة الداخلة
        _db.MessageLogs.Add(new MessageLog
        {
            CustomerId = customer.Id,
            Phone = msg.Phone,
            Direction = MessageDirection.In,
            Channel = msg.Channel,
            Intent = "inbound",
            WindowState = fepUntil is not null ? WindowState.FepOpen : WindowState.CswOpen,
            SendMode = SendMode.Free,
            MetaCategory = MetaCategory.Service,
            Content = msg.Text?.Length > 900 ? msg.Text[..900] : msg.Text,
            WaMessageId = msg.MessageId,
            Status = MessageStatus.Delivered,
            CostEstimated = 0m,
            CreatedAt = msg.At,
            DeliveredAt = msg.At
        });

        await _db.SaveChangesAsync(ct);

        return new InboundResult(customer.Id, isNew, fepUntil, cswUntil, optedOut);
    }

    private static bool IsOptOut(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().ToLowerInvariant();
        // 🔑 بنشترط الرسالة تكون قصيرة — عشان "مش عايز إلغاء الأوردر"
        //    ميتحسبش opt-out بالغلط
        return t.Length <= 40 && OptOutWords.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record InboundResult(
    long CustomerId, bool IsNewCustomer,
    DateTimeOffset? FepOpenedUntil, DateTimeOffset CswUntil, bool OptedOut);

// ══════════════════════════════════════════════════════════════════════
//  التحقق من التوقيع
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// 🔐 التحقق من توقيع webhook الرسمي. docs/10 §6.
///
/// بدون ده، أي حد يعرف الـ URL بتاعك يقدر يبعتلك أحداث مزوّرة:
/// يفتح نوافذ FEP وهمية، يعمل عملاء وهميين، أو يعمل opt-out لعملائك كلهم.
///
/// ⚠️ نقطتين حرجتين:
///   ١. لازم تستخدم الـ **raw body** بالبايت بالظبط — لو عملت
///      deserialize و serialize تاني، التوقيع مش هيطابق (ترتيب المفاتيح
///      والمسافات بتتغيّر).
///   ٢. لازم مقارنة **ثابتة الزمن** (<c>FixedTimeEquals</c>) — المقارنة
///      العادية بتسرّب معلومات عن طريق توقيت التنفيذ (timing attack).
/// </summary>
public static class WebhookSignature
{
    public static bool Verify(byte[] rawBody, string? signatureHeader, string? appSecret)
    {
        if (string.IsNullOrWhiteSpace(appSecret)) return false;
        if (string.IsNullOrWhiteSpace(signatureHeader)) return false;

        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var provided = signatureHeader[prefix.Length..];

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var computed = Convert.ToHexString(hmac.ComputeHash(rawBody)).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(provided.ToLowerInvariant()));
    }
}
