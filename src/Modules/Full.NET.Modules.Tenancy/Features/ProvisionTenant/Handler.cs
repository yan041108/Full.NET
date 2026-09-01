using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Localization;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Domain;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

/// <summary>
/// 负责执行租户开通、唯一性校验和可选套餐绑定。
/// </summary>
/// <param name="queryExecutor">执行只读唯一性与套餐查询的查询执行器。</param>
/// <param name="commandExecutor">执行租户写入命令的命令执行器。</param>
/// <param name="clock">提供统一 UTC 时间的时钟服务。</param>
/// <param name="idGenerator">生成租户主键的标识生成器。</param>
internal sealed class Handler(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<ProvisionTenantCommand, TenantSummary>
{
    /// <summary>
    /// 执行租户开通并返回新租户摘要。
    /// </summary>
    /// <param name="command">开通命令，包含标识、名称、域名和可选套餐。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>成功时返回新租户摘要；失败时返回冲突、未找到或业务规则错误。</returns>
    public async Task<Result<TenantSummary>> HandleAsync(
        ProvisionTenantCommand command,
        CancellationToken cancellationToken)
    {
        // 先做统一归一化，避免唯一性检查与最终入库使用不同大小写/空白语义。
        var identifier = command.Identifier?.Trim().ToLowerInvariant() ?? string.Empty;
        var name = command.Name?.Trim() ?? string.Empty;
        var domain = command.Domain?.Trim().ToLowerInvariant() ?? string.Empty;

        var identifierMatchCount = await queryExecutor
            .QuerySingleOrDefaultAsync<long>(
                TenantSql.FindByIdentifier,
                TenancySqlParameters.Create(("Identifier", identifier)),
                cancellationToken)
            .ConfigureAwait(false);
        if (identifierMatchCount > 0)
        {
            return Conflict(
                TenancyErrorCodes.IdentifierExists,
                "A tenant with this identifier already exists.");
        }

        // 标识与域名分别独立判重，避免一个冲突覆盖另一个更贴近用户输入的问题。
        var domainMatchCount = await queryExecutor
            .QuerySingleOrDefaultAsync<long>(
                TenantSql.CountByDomain,
                TenancySqlParameters.Create(("Domain", domain)),
                cancellationToken)
            .ConfigureAwait(false);
        if (domainMatchCount > 0)
        {
            return Conflict(
                TenancyErrorCodes.DomainExists,
                "A tenant with this domain already exists.");
        }

        string? packageCode = null;
        string? packageName = null;
        if (command.TenantPackageId is Guid packageId)
        {
            // 套餐绑定采用“存在且激活”双门禁，避免把失效套餐静默带入新租户。
            var package = await queryExecutor.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(
                    TenantPackageSql.FindPackageById,
                    TenancySqlParameters.Create(("PackageId", packageId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (package is null)
            {
                return NotFoundPackage();
            }

            if (!package.IsActive)
            {
                return PackageInactive();
            }

            packageCode = package.Code;
            packageName = package.Name;
        }

        // 默认语言在租户创建时一次性写入平台基线，后续再由独立设置链路调整。
        var tenant = new Tenant(
            idGenerator.NewId(),
            identifier,
            name,
            domain,
            true,
            clock.UtcNow,
            1,
            LocaleCatalog.DefaultLocale);
        var affectedRows = await commandExecutor
            .ExecuteAsync(
                TenantSql.Insert,
                TenancySqlParameters.Create(
                    ("Id", tenant.Id),
                    ("Identifier", tenant.Identifier),
                    ("Name", tenant.Name),
                    ("Domain", tenant.Domain),
                    ("IsActive", tenant.IsActive),
                    ("CreatedAtUtc", tenant.CreatedAtUtc),
                    ("Version", tenant.Version),
                    ("DefaultLocale", tenant.DefaultLocale),
                    ("TenantPackageId", command.TenantPackageId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Tenant insert affected {affectedRows} rows instead of one.");
        }

        // Expand/Cutover：开通成功后由服务层直接失效缓存；不再写入缓存专用 Outbox。
        // 旧消息类型与兼容 Handler 保留，仅用于排空升级前已入库消息。

        return Result<TenantSummary>.Success(new TenantSummary(
            tenant.Id,
            tenant.Identifier,
            tenant.Name,
            tenant.Domain,
            tenant.IsActive,
            tenant.Version,
            tenant.DefaultLocale,
            command.TenantPackageId,
            packageCode,
            packageName));
    }

    /// <summary>
    /// 构造“标识或域名已存在”的冲突结果。
    /// </summary>
    /// <param name="code">稳定错误码。</param>
    /// <param name="message">供调用方展示或记录的错误消息。</param>
    /// <returns>Conflict 类型的失败结果。</returns>
    private static Result<TenantSummary> Conflict(string code, string message) =>
        Result<TenantSummary>.Failure(new Error(
            Code: code,
            Message: message,
            Type: ErrorType.Conflict));

    /// <summary>
    /// 构造“租户套餐不存在”的失败结果。
    /// </summary>
    /// <returns>NotFound 类型的失败结果。</returns>
    private static Result<TenantSummary> NotFoundPackage() =>
        Result<TenantSummary>.Failure(new Error(
            TenancyErrorCodes.PackageNotFound,
            "The tenant package was not found.",
            ErrorType.NotFound));

    /// <summary>
    /// 构造“租户套餐未激活”的失败结果。
    /// </summary>
    /// <returns>BusinessRule 类型的失败结果。</returns>
    private static Result<TenantSummary> PackageInactive() =>
        Result<TenantSummary>.Failure(new Error(
            TenancyErrorCodes.PackageInactive,
            "The tenant package is not active.",
            ErrorType.BusinessRule));
}
