using System.Resources;
using Full.NET.Hosting.Api;

namespace Full.NET.Modules.Messaging;

/// <summary>运维错误通过模块资源本地化；机器码保持稳定，禁止把载荷或内部异常回显。</summary>
internal sealed class MessagingErrorResourceSource() : ResourceManagerErrorResourceSource("messaging.",
    new ResourceManager("Full.NET.Modules.Messaging.Resources.MessagingErrors", typeof(MessagingErrorResourceSource).Assembly));
