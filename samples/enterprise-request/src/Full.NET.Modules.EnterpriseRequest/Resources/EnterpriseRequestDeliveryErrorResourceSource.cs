using System.Resources;
using Full.NET.Hosting.Api;

namespace Full.NET.Modules.EnterpriseRequest.Resources;

/// <summary>持久任务只保存稳定机器码，HTTP 错误在最终交付时按当前语言解析。</summary>
internal sealed class EnterpriseRequestDeliveryErrorResourceSource() : ResourceManagerErrorResourceSource("enterprise_request.",
    new ResourceManager("Full.NET.Modules.EnterpriseRequest.Resources.EnterpriseRequestDeliveryErrors", typeof(EnterpriseRequestDeliveryErrorResourceSource).Assembly));
