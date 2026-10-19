using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Auditing.Contracts;

/// <summary>Auditing 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.Auditing.Resources;

internal sealed class AuditingErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        AuditingErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Auditing.Resources.AuditingErrors",
            typeof(AuditingErrorResourceSource).Assembly));
