namespace Full.NET.Data.Abstractions;

public sealed class TenantContextMissingException(string statementName)
    : InvalidOperationException($"SQL statement '{statementName}' requires a tenant context.");

public sealed class TenantScopeViolationException(string statementName)
    : InvalidOperationException($"SQL statement '{statementName}' must contain the @TenantId predicate parameter.");

public sealed class HostContextRequiredException(string statementName)
    : InvalidOperationException($"SQL statement '{statementName}' requires the host context.");
