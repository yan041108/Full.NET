namespace Full.NET.Data.Abstractions;

public sealed class TenantContextMissingException(string statementName)
    : InvalidOperationException($"SQL statement '{statementName}' requires a tenant context.");

public sealed class TenantScopeViolationException(string statementName)
    : InvalidOperationException(
        $"SQL statement '{statementName}' declares an invalid tenant binding or omits the @TenantId parameter.");

public sealed class HostContextRequiredException(string statementName)
    : InvalidOperationException($"SQL statement '{statementName}' requires the host context.");
