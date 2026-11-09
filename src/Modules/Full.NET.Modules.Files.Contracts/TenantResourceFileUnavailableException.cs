namespace Full.NET.Modules.Files.Contracts;

/// <summary>原企业不可读取时中断文件列表恢复，避免把不可用误判为文件不存在。</summary>
public sealed class TenantResourceFileUnavailableException()
    : InvalidOperationException("The original tenant resource files are unavailable.");
