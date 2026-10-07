using Full.NET.Abstractions.Messaging;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.ImportExport;

/// <summary>回执占位、领域创建与完成在同一事务内；崩溃或重复批次不重复创建申请。</summary>
internal sealed class EnterpriseRequestImportService(IQueryExecutor queryExecutor, ICommandExecutor commandExecutor,
    ICommandTransaction transaction, EnterpriseRequestManagementService managementService,
    EnterpriseRequestQueryService queries, ICurrentTenant currentTenant, IClock clock,
    IIdGenerator idGenerator, IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer)
{
    internal async Task<Result<Guid>> ImportAsync(Guid taskId, int lineNumber, CreateEnterpriseRequestRequest request,
        Guid organizationUnitId, Guid actorUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
            return Result<Guid>.Failure(new Error("enterprise_request.tenant_context_required", "Tenant context is required.", ErrorType.Forbidden));
        if (taskId == Guid.Empty || lineNumber < 2) throw new ArgumentException("Persisted task and source row are required.");
        var hash = PayloadHash(request, organizationUnitId, actorUserId);
        var key = Parameters(("TaskId", taskId), ("LineNumber", lineNumber));
        var receipt = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestImportReceiptRecord>(
            EnterpriseRequestImportSql.Find, key, cancellationToken).ConfigureAwait(false);
        if (receipt is not null) return await ReplayAsync(receipt, hash, actorUserId, cancellationToken).ConfigureAwait(false);
        try
        {
            return await transaction.ExecuteResultAsync(async token =>
            {
                var receiptId = idGenerator.NewId();
                await commandExecutor.ExecuteAsync(EnterpriseRequestImportSql.Insert,
                    Parameters(("Id", receiptId), ("TaskId", taskId), ("LineNumber", lineNumber),
                        ("PayloadHash", hash), ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
                // 复用领域创建及组织写入授权；嵌套事务复用当前 DbSession，不独立提交。
                var created = await managementService.CreateAsync(request, actorUserId, organizationUnitId, token).ConfigureAwait(false);
                if (!created.IsSuccess) return Result<Guid>.Failure(created.Error!);
                if (await commandExecutor.ExecuteAsync(EnterpriseRequestImportSql.Complete,
                        Parameters(("Id", receiptId), ("EntityId", created.Value!.Id)), token).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Import receipt completion lost its owner.");
                return Result<Guid>.Success(created.Value!.Id);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 并发败方事务回滚后才读取胜方已提交回执；不能在失败事务内吞掉唯一键冲突。
            receipt = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestImportReceiptRecord>(
                EnterpriseRequestImportSql.Find, key, cancellationToken).ConfigureAwait(false);
            if (receipt is null) throw;
            return await ReplayAsync(receipt, hash, actorUserId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Result<Guid>> ReplayAsync(EnterpriseRequestImportReceiptRecord receipt, string hash,
        Guid actorUserId, CancellationToken cancellationToken)
    {
        if (receipt.EntityId is not Guid id || !string.Equals(receipt.PayloadHash, hash, StringComparison.Ordinal))
            return Result<Guid>.Failure(new Error(ValidationErrorCodes.Failed,
                "The import row identity was reused with different content or an incomplete receipt.", ErrorType.Conflict));
        var existing = await queries.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (!existing.IsSuccess) return Result<Guid>.Failure(existing.Error!);
        // 重放也检查当前实体归属，不能用旧回执绕过撤销权限或机构变更。
        var authorization = await writeAuthorizer.EnsureCanWriteAsync(currentTenant.Id!.Value,
            existing.Value!.OrganizationUnitId, actorUserId, cancellationToken).ConfigureAwait(false);
        return authorization.IsSuccess && authorization.Value ? Result<Guid>.Success(id)
            : Result<Guid>.Failure(authorization.Error ?? new Error(ValidationErrorCodes.Failed,
                "The current organization write authorization was denied.", ErrorType.Forbidden));
    }

    internal static string PayloadHash(CreateEnterpriseRequestRequest request, Guid unitId, Guid actorId)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            // 长度前缀避免字段拼接碰撞；固定版本包含发起人和机构，摘要不泄露原文。
            writer.Write(1); writer.Write(request.RequestNumber); writer.Write(request.Title); writer.Write(request.Status);
            writer.Write(request.TotalAmount.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
            writer.Write(request.ApplicantUserId.ToString("D")); writer.Write(unitId.ToString("D")); writer.Write(actorId.ToString("D"));
        }
        return Convert.ToHexString(SHA256.HashData(payload.GetBuffer().AsSpan(0, (int)payload.Length)));
    }

    private static Dictionary<string, object?> Parameters(params (string Key, object? Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
}
