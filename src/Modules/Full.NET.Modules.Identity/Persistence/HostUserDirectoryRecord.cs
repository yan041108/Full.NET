using Full.NET.Localization;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>供跨模块批量显示投影使用的最小 Host 用户行。</summary>
internal sealed record HostUserDirectoryRecord(
    Guid Id,
    string Username,
    string DisplayName,
    string PreferredLocale = LocaleCatalog.DefaultLocale);
