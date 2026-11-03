using System.Resources;
using Full.NET.Hosting.Api;

namespace Full.NET.Modules.Payments.Resources;

/// <summary>付费订阅的稳定错误码资源；持久化工作项仅保存机器码。</summary>
internal sealed class PaymentsBillingErrorResourceSource() : ResourceManagerErrorResourceSource("payments.",
    new ResourceManager("Full.NET.Modules.Payments.Resources.PaymentsBillingErrors", typeof(PaymentsBillingErrorResourceSource).Assembly));
