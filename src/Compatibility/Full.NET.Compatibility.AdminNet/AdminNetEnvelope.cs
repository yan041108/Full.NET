namespace Full.NET.Compatibility.AdminNet;

public sealed record AdminNetEnvelope<T>(
    bool Success,
    string Code,
    string? Message,
    T? Data,
    string TraceId);
