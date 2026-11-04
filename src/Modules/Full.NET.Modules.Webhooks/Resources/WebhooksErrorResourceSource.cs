using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Webhooks.Contracts;

namespace Full.NET.Modules.Webhooks.Resources;

/// <summary>订阅管理错误按稳定机器码本地化，签名材料不进入响应。</summary>
internal sealed class WebhooksErrorResourceSource() : ResourceManagerErrorResourceSource(
    WebhookErrorCodes.Prefix, new ResourceManager("Full.NET.Modules.Webhooks.Resources.WebhooksErrors", typeof(WebhooksErrorResourceSource).Assembly));
