using System.Net.Mail;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageRecipientEndpoints;

/// <summary>按受信作用域登记收件端点；查询只返回掩码与验证状态。</summary>
/// <param name="queryExecutor">执行受作用域保护的读取和行锁查询。</param>
/// <param name="commandExecutor">执行受保护端点的插入与删除。</param>
/// <param name="transaction">协调检查、锁和写入使用同一数据库事务。</param>
/// <param name="currentTenant">提供 Host 或 Tenant 的受信作用域。</param>
/// <param name="protector">在原值落库前执行 Data Protection。</param>
/// <param name="clock">提供可测试的 UTC 时间。</param>
/// <param name="idGenerator">生成 UUID v7 逻辑主键。</param>
/// <param name="providerAdapters">提供闭合 ProviderType 与端点类型目录。</param>
/// <param name="databaseOptions">选择 SQL Server 或 MySQL 的行锁语句。</param>
internal sealed class RecipientEndpointStore(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    NotificationRecipientEndpointProtector protector,
    IClock clock,
    IIdGenerator idGenerator,
    IEnumerable<INotificationProviderAdapter> providerAdapters,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>列出当前用户在当前受信作用域下的全部脱敏收件端点。</summary>
    /// <param name="userId">从认证 Claim 解析的当前用户标识。</param>
    /// <param name="cancellationToken">用于取消数据库查询的令牌。</param>
    /// <returns>不包含原值或受保护值的端点集合。</returns>
    public async Task<Result<IReadOnlyList<RecipientEndpointResponse>>> ListMineAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var rows = await queryExecutor.QueryAsync<NotificationRecipientEndpointRecord>(
                NotificationRecipientEndpointSql.ListMaskedByScopeUser,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<RecipientEndpointResponse>>.Success(rows.Select(Map).ToArray());
    }

    /// <summary>由当前用户登记待验证端点，验证状态不接受客户端输入。</summary>
    /// <param name="userId">从认证 Claim 解析的当前用户标识。</param>
    /// <param name="request">只包含 Profile 版本、端点类型和原值的请求。</param>
    /// <param name="cancellationToken">用于取消事务和数据库命令的令牌。</param>
    /// <returns>仅包含掩码和待验证状态的端点响应。</returns>
    public Task<Result<RecipientEndpointResponse>> CreateMineAsync(
        Guid userId,
        CreateMyRecipientEndpointRequest request,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(
            userId,
            request.ProviderProfileVersionId,
            request.EndpointKindKey,
            request.RawValue,
            NotificationRecipientEndpointStatuses.Pending,
            cancellationToken);

    /// <summary>删除当前用户在当前受信作用域下拥有的端点。</summary>
    /// <param name="userId">从认证 Claim 解析的当前用户标识。</param>
    /// <param name="endpointId">待删除端点标识。</param>
    /// <param name="cancellationToken">用于取消事务和数据库命令的令牌。</param>
    /// <returns>删除成功时返回 true；越权和不存在统一返回未找到。</returns>
    public Task<Result<bool>> DeleteMineAsync(
        Guid userId,
        Guid endpointId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            async token =>
            {
                var scope = NotificationInboxScope.Resolve(currentTenant);
                var affected = await commandExecutor.ExecuteAsync(
                        NotificationRecipientEndpointSql.DeleteMine,
                        NotificationPlatformSqlParameters.Create(
                            ("Id", endpointId),
                            ("TenantScopeKey", scope.TenantScopeKey),
                            ("UserId", userId)),
                        token)
                    .ConfigureAwait(false);
                return affected == 0
                    ? Result<bool>.Failure(EndpointNotFound())
                    : Result<bool>.Success(true);
            },
            cancellationToken);

    /// <summary>
    /// 在可信模块边界登记指定验证状态的端点；HTTP 当前用户入口只能调用
    /// <see cref="CreateMineAsync"/> 并固定为待验证。
    /// </summary>
    /// <param name="actorUserId">端点所属且发起登记的用户标识。</param>
    /// <param name="providerProfileVersionId">当前作用域最新发布并启用的 Profile 版本标识。</param>
    /// <param name="endpointKindKey">Adapter 声明的端点类型键。</param>
    /// <param name="rawValue">需要在落库前保护的端点原值。</param>
    /// <param name="verificationStatusKey">可信模块边界提供的验证状态。</param>
    /// <param name="cancellationToken">用于取消事务和数据库命令的令牌。</param>
    /// <returns>登记后的脱敏端点。</returns>
    public Task<Result<RecipientEndpointResponse>> UpsertAsync(
        Guid actorUserId,
        Guid providerProfileVersionId,
        string endpointKindKey,
        string rawValue,
        string verificationStatusKey,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpsertCoreAsync(
                actorUserId,
                providerProfileVersionId,
                endpointKindKey,
                rawValue,
                verificationStatusKey,
                token),
            cancellationToken);

    /// <summary>在单一事务内完成 Profile 校验、唯一键行锁和端点写入。</summary>
    /// <param name="actorUserId">端点所属用户标识。</param>
    /// <param name="providerProfileVersionId">不可变 Profile 版本标识。</param>
    /// <param name="endpointKindKey">端点类型键。</param>
    /// <param name="rawValue">端点原值。</param>
    /// <param name="verificationStatusKey">可信调用方指定的验证状态。</param>
    /// <param name="cancellationToken">用于取消数据库工作的令牌。</param>
    /// <returns>成功时只返回脱敏投影。</returns>
    private async Task<Result<RecipientEndpointResponse>> UpsertCoreAsync(
        Guid actorUserId,
        Guid providerProfileVersionId,
        string endpointKindKey,
        string rawValue,
        string verificationStatusKey,
        CancellationToken cancellationToken)
    {
        var kind = endpointKindKey?.Trim() ?? string.Empty;
        var value = rawValue?.Trim() ?? string.Empty;
        var status = verificationStatusKey?.Trim() ?? string.Empty;
        if (kind.Length is < 1 or > 32 || value.Length is < 1 or > 256
            || !IsValidEndpointValue(value, kind)
            || status is not (NotificationRecipientEndpointStatuses.Pending
                or NotificationRecipientEndpointStatuses.Verified
                or NotificationRecipientEndpointStatuses.Failed))
        {
            return Result<RecipientEndpointResponse>.Failure(EndpointValidationFailed());
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var providerTypeKey = await queryExecutor.QuerySingleOrDefaultAsync<string>(
                NotificationRecipientEndpointSql.FindPublishedProviderTypeForScope,
                NotificationPlatformSqlParameters.Create(
                    ("ProviderProfileVersionId", providerProfileVersionId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(providerTypeKey))
        {
            // ProfileVersion 的 Guid 是全局标识，但仍必须与当前作用域和最新发布指针联合授权。
            return Result<RecipientEndpointResponse>.Failure(new Error(
                NotificationsErrorCodes.ProviderProfileNotFound,
                "The provider profile was not found in the current scope.",
                ErrorType.NotFound));
        }

        var matchingAdapters = providerAdapters
            .Where(adapter => string.Equals(
                adapter.Descriptor.ProviderTypeKey,
                providerTypeKey,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matchingAdapters.Length != 1
            || !string.Equals(
                matchingAdapters[0].RecipientEndpointKindKey,
                kind,
                StringComparison.Ordinal))
        {
            // 客户端不能为 Profile 自行发明端点类型；类型必须来自闭合 Adapter 目录。
            return Result<RecipientEndpointResponse>.Failure(EndpointValidationFailed());
        }

        var lockStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => NotificationRecipientEndpointSql.LockExistingSqlServer,
            DatabaseProvider.MySql => NotificationRecipientEndpointSql.LockExistingMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var uniqueParameters = NotificationPlatformSqlParameters.Create(
            ("TenantScopeKey", scope.TenantScopeKey),
            ("UserId", actorUserId),
            ("ProviderProfileVersionId", providerProfileVersionId),
            ("EndpointKindKey", kind));
        var existingId = await queryExecutor.QuerySingleOrDefaultAsync<Guid>(
                lockStatement,
                uniqueParameters,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingId != Guid.Empty)
        {
            return Result<RecipientEndpointResponse>.Failure(new Error(
                NotificationsErrorCodes.RecipientEndpointConflict,
                "A recipient endpoint of this kind is already registered for the provider profile.",
                ErrorType.Conflict));
        }

        var now = clock.UtcNow;
        var id = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                NotificationRecipientEndpointSql.Insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", id),
                    ("InboxTenantId", scope.TenantId),
                    ("ScopeKey", scope.ScopeKey),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", actorUserId),
                    ("ProviderProfileVersionId", providerProfileVersionId),
                    ("EndpointKindKey", kind),
                    ("ProtectedValue", protector.Protect(value)),
                    ("MaskedValue", NotificationRecipientEndpointMasker.Mask(value, kind)),
                    ("VerificationStatusKey", status),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        var record = await queryExecutor.QuerySingleOrDefaultAsync<NotificationRecipientEndpointRecord>(
                NotificationRecipientEndpointSql.FindMaskedById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", id),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<RecipientEndpointResponse>.Failure(EndpointValidationFailed())
            : Result<RecipientEndpointResponse>.Success(Map(record));
    }

    /// <summary>验证端点原值；邮箱禁止 display-name 等可产生歧义的扩展格式。</summary>
    /// <param name="value">已经去除首尾空白的端点原值。</param>
    /// <param name="kind">Adapter 声明的端点类型键。</param>
    /// <returns>原值满足对应闭合格式时返回 true。</returns>
    private static bool IsValidEndpointValue(string value, string kind)
    {
        if (!string.Equals(kind, "email", StringComparison.Ordinal))
        {
            return true;
        }

        return MailAddress.TryCreate(value, out var address)
            && string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>创建不含端点原值的校验错误。</summary>
    /// <returns>稳定的收件端点校验错误。</returns>
    private static Error EndpointValidationFailed() => new(
        NotificationsErrorCodes.RecipientEndpointValidationFailed,
        "The recipient endpoint value or kind is invalid.",
        ErrorType.Validation);

    /// <summary>创建不区分不存在与越权的端点未找到错误。</summary>
    /// <returns>稳定的收件端点未找到错误。</returns>
    private static Error EndpointNotFound() => new(
        NotificationsErrorCodes.RecipientEndpointNotFound,
        "The recipient endpoint was not found in the current scope.",
        ErrorType.NotFound);

    /// <summary>把持久化投影转换为不含受保护原值的 HTTP 响应。</summary>
    /// <param name="record">不包含 ProtectedValue 的端点投影。</param>
    /// <returns>只含掩码和验证状态的响应。</returns>
    private static RecipientEndpointResponse Map(NotificationRecipientEndpointRecord record) =>
        new(
            record.Id,
            record.UserId,
            record.ProviderProfileVersionId,
            record.EndpointKindKey,
            record.MaskedValue,
            record.VerificationStatusKey,
            record.CreatedAtUtc);
}
