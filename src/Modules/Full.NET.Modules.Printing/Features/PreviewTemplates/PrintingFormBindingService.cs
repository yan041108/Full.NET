using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;

namespace Full.NET.Modules.Printing.Features.PreviewTemplates;

/// <summary>按固定表单 Schema 解析打印数据绑定字段。</summary>
internal sealed class PrintingFormBindingService(
    ICurrentTenant currentTenant,
    IClock clock,
    IPrintingTenantProfileBindingSource tenantProfileBindingSource)
{
    /// <summary>为指定 Schema 解析绑定字段；缺少租户上下文或绑定源时立即失败。</summary>
    public async Task<Result<IReadOnlyDictionary<string, string?>>> ResolveAsync(
        string formSchemaKey,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var schema = PrintingFormSchemaCatalog.TryGet(formSchemaKey);
        if (schema is null)
        {
            return Result<IReadOnlyDictionary<string, string?>>.Failure(new Error(
                PrintingErrorCodes.FormSchemaNotFound,
                "The printing form schema was not found.",
                ErrorType.Validation));
        }

        if (currentTenant.Id is null)
        {
            return BindingFailed("Tenant context is required for printing data binding.");
        }

        if (!string.Equals(formSchemaKey, PrintingFormSchemaKeys.TenantProfileCard, StringComparison.Ordinal))
        {
            return BindingFailed("The printing form schema is not supported yet.");
        }

        var profile = await tenantProfileBindingSource
            .ResolveAsync(currentTenant.Id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
        {
            return BindingFailed("The tenant profile could not be resolved for printing.");
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["tenantName"] = profile.TenantName,
            ["tenantCode"] = profile.TenantCode,
            ["tenantDomain"] = profile.TenantDomain,
            ["printedByDisplayName"] = ResolveDisplayName(principal),
            ["printedAtUtc"] = clock.UtcNow.ToString("O"),
        };

        foreach (var field in schema.Fields)
        {
            if (!values.ContainsKey(field.FieldKey))
            {
                return BindingFailed($"Missing binding value for field '{field.FieldKey}'.");
            }
        }

        return Result<IReadOnlyDictionary<string, string?>>.Success(values);
    }

    private static string ResolveDisplayName(ClaimsPrincipal principal)
    {
        var displayName = principal.FindFirst("preferred_username")?.Value;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        var subject = principal.FindFirst(FullNetIdentityClaimTypes.Subject)?.Value;
        return string.IsNullOrWhiteSpace(subject) ? "Unknown" : subject;
    }

    private static Result<IReadOnlyDictionary<string, string?>> BindingFailed(string message) =>
        Result<IReadOnlyDictionary<string, string?>>.Failure(new Error(
            PrintingErrorCodes.BindingFailed,
            message,
            ErrorType.Validation));
}
