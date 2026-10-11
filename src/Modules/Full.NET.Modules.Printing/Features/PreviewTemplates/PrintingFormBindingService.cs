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
    IPrintingTenantProfileBindingSource tenantProfileBindingSource,
    PrintingFormSchemaCatalog catalog,
    IEnumerable<IPrintingRecordBindingSource> recordSources)
{
    /// <summary>为指定 Schema 解析绑定字段；缺少租户上下文或绑定源时立即失败。</summary>
    public async Task<Result<IReadOnlyDictionary<string, string?>>> ResolveAsync(
        string formSchemaKey,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default,
        Guid? recordId = null)
    {
        var schema = catalog.TryGet(formSchemaKey);
        if (schema is null)
        {
            return Result<IReadOnlyDictionary<string, string?>>.Failure(new Error(
                PrintingErrorCodes.FormSchemaNotFound,
                "The printing form schema was not found.",
                ErrorType.Validation));
        }

        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            return BindingFailed("Tenant context is required for printing data binding.");
        }

        Dictionary<string, string?> values;
        if (schema.RequiresRecordId)
        {
            if (recordId is null || recordId == Guid.Empty)
                return BindingFailed("A business record identifier is required for this printing form.");
            var sources = recordSources.Where(source => string.Equals(source.FormSchemaKey, formSchemaKey, StringComparison.Ordinal)).ToArray();
            if (sources.Length != 1)
                return BindingFailed("The printing record binding source is unavailable.");
            var result = await sources[0].ResolveAsync(recordId.Value, principal, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is null)
                return Result<IReadOnlyDictionary<string, string?>>.Failure(result.Error!);
            values = new Dictionary<string, string?>(result.Value, StringComparer.Ordinal);
        }
        else
        {
            if (!string.Equals(formSchemaKey, PrintingFormSchemaKeys.TenantProfileCard, StringComparison.Ordinal))
                return BindingFailed("The printing form schema is not supported yet.");
            var profile = await tenantProfileBindingSource.ResolveAsync(currentTenant.Id.Value, cancellationToken).ConfigureAwait(false);
            if (profile is null) return BindingFailed("The tenant profile could not be resolved for printing.");
            values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["tenantName"] = profile.TenantName, ["tenantCode"] = profile.TenantCode, ["tenantDomain"] = profile.TenantDomain,
            };
        }
        // 打印人和时间来自可信当前主体与时钟，业务源不能覆盖；只输出 Schema 声明的字段。
        values["printedByDisplayName"] = ResolveDisplayName(principal);
        values["printedAtUtc"] = clock.UtcNow.ToString("O");

        foreach (var field in schema.Fields)
        {
            if (!values.ContainsKey(field.FieldKey))
            {
                return BindingFailed($"Missing binding value for field '{field.FieldKey}'.");
            }
        }

        return Result<IReadOnlyDictionary<string, string?>>.Success(schema.Fields.ToDictionary(field => field.FieldKey, field => values[field.FieldKey], StringComparer.Ordinal));
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
