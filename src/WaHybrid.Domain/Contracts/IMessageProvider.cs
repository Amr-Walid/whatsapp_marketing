using WaHybrid.Domain.Enums;

namespace WaHybrid.Domain.Contracts;

/// <summary>
/// 🔑 العقد الموحّد لأي مزوّد إرسال. مطابق لـ <c>Provider</c> في docs/09 §2.1.
///
/// القاعدة الحديدية: أي كود فوق الطبقة دي **ممنوع** يعرف إحنا بنبعت من أنهي قناة.
/// لو لقيت <c>if (channel == ...)</c> في البوت أو الأوردرات — التصميم غلط.
///
/// التماثل بين <c>OfficialProvider</c> و <c>UnofficialProvider</c> هو اللي
/// بيخلّي الـ <c>ChannelRouter</c> بسيط: نفس التوقيع بالظبط، والفرق كله جوّه.
/// </summary>
public interface IMessageProvider
{
    ChannelKind Channel { get; }

    /// <summary>هل المزوّد يقدر ينفّذ الطلب ده دلوقتي؟ (فحص بدون إرسال)</summary>
    Task<CanSendResult> CanAsync(SendRequest request, CancellationToken ct = default);

    /// <summary>الإرسال الفعلي. لازم يعمل idempotency check الأول.</summary>
    Task<SendResult> SendAsync(SendRequest request, CancellationToken ct = default);

    /// <summary>صحة المزوّد — للتدهور عند السقوط</summary>
    Task<ProviderHealth> HealthAsync(CancellationToken ct = default);
}

/// <summary>سجل المزوّدين — docs/09 §2.4</summary>
public interface IProviderRegistry
{
    IMessageProvider Get(ChannelKind channel);
    IReadOnlyList<IMessageProvider> All { get; }
}
