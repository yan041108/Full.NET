// 身份模块 HTTP 客户端请求上下文；承载设备与会话关联元数据。
namespace Full.NET.Modules.Identity.Http;

/// <summary>身份模块 HTTP 客户端请求上下文；承载设备与会话关联元数据。</summary>
internal sealed record ClientRequestContext(
    string? IpAddress,
    string? UserAgent);
