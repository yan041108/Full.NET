namespace Full.NET.Modules.Identity.Http;

internal sealed record ClientRequestContext(
    string? IpAddress,
    string? UserAgent);
