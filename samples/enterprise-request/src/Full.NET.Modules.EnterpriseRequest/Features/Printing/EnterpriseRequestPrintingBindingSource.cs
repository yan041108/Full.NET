using System.Globalization;
using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.Printing;

/// <summary>复用申请读取的租户与组织范围边界，绑定前后权威确认当前会话业务读取权限。</summary>
internal sealed class EnterpriseRequestPrintingBindingSource(
    ICurrentTenant tenant, ICurrentSessionAuthorization authorization, EnterpriseRequestQueryService queries)
    : IPrintingRecordBindingSource
{
    /// <summary>获取样例拥有的固定申请摘要表单键。</summary>
    public string FormSchemaKey => EnterpriseRequestPrintingSchemaContributor.SchemaKey;

    /// <summary>解析可读取的申请；权限、租户、用户或会话发生变化时拒绝交付字段。</summary>
    public async Task<Result<IReadOnlyDictionary<string, string?>>> ResolveAsync(
        Guid recordId, ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!tenant.IsAvailable || tenant.IsHost || tenant.Id is null || recordId == Guid.Empty
            || !Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var userId) || userId == Guid.Empty)
            return Denied();
        var actor = await authorization.AuthorizeAsync(EnterpriseRequestPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.UserId != userId || actor.TenantId != tenant.Id) return Denied();
        var superAdministrator = bool.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SuperAdministrator), out var enabled) && enabled;
        var result = await queries.GetByIdAsync(recordId, actor.UserId, superAdministrator, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
            return Result<IReadOnlyDictionary<string, string?>>.Failure(result.Error!);
        var finalActor = await authorization.AuthorizeAsync(EnterpriseRequestPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (finalActor != actor || result.Value.TenantId != tenant.Id) return Denied();
        var record = result.Value;
        return Result<IReadOnlyDictionary<string, string?>>.Success(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["requestNumber"] = record.RequestNumber, ["title"] = record.Title, ["status"] = record.Status,
            ["totalAmount"] = record.TotalAmount.ToString(CultureInfo.InvariantCulture),
        });
    }

    private static Result<IReadOnlyDictionary<string, string?>> Denied() => Result<IReadOnlyDictionary<string, string?>>.Failure(
        new Error(CommonErrorCodes.PermissionDenied, "The current tenant session must be authorized to read this business record.", ErrorType.Forbidden));
}
