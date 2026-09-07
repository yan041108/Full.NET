using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Persistence;
using Full.NET.Modules.Organization.Serialization;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Organization.Features.ManageTenantPositions;

internal sealed partial class TenantPositionManagementService
{
    /// <summary>按可信任务与原始行号重放同一岗位导入；幂等凭据由 Organization 所有。</summary>
    /// <param name="taskId">调度方持久化任务标识，不是用户输入的任意幂等键。</param>
    /// <param name="lineNumber">原始工作簿中的一基行号。</param>
    /// <param name="row">不可变源文件的行载荷。</param>
    /// <param name="capabilities">已验证的关联权限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal async Task<Result<ImportOrganizationPositionRowResult>> ImportTaskRowAsync(Guid taskId, int lineNumber,
        ImportOrganizationPositionRow row, OrganizationPositionImportCapabilities capabilities, CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        if (taskId == Guid.Empty || lineNumber < 1)
            return Result<ImportOrganizationPositionRowResult>.Failure(new Error(ValidationErrorCodes.Failed,
                "A durable task identity and original line number are required.", ErrorType.Validation));
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(row,
            OrganizationJsonSerializerContext.Default.ImportOrganizationPositionRow)));
        var key = OrganizationSqlParameters.Create(("TaskId", taskId), ("LineNumber", lineNumber));
        var receipt = await queryExecutor.QuerySingleOrDefaultAsync<PositionImportReceiptRecord>(
            PositionImportSql.Find, key, cancellationToken).ConfigureAwait(false);
        if (receipt is not null) return ReplayImportRow(receipt, hash, lineNumber);
        try
        {
            return await transaction.ExecuteResultAsync(async token =>
            {
                // 唯一任务/行占位与岗位及关联同事务；并发请求在唯一键处串行，失败回滚占位。
                var receiptId = idGenerator.NewId();
                await commandExecutor.ExecuteAsync(PositionImportSql.Insert,
                    OrganizationSqlParameters.Create(("Id", receiptId), ("TaskId", taskId), ("LineNumber", lineNumber),
                        ("PayloadHash", hash), ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
                var imported = await ImportRowCoreAsync(row, capabilities, token).ConfigureAwait(false);
                if (!imported.IsSuccess) return Result<ImportOrganizationPositionRowResult>.Failure(imported.Error!);
                var positionId = imported.Value!.Id;
                var affected = await commandExecutor.ExecuteAsync(PositionImportSql.Complete,
                    OrganizationSqlParameters.Create(("Id", receiptId), ("PositionId", positionId)), token).ConfigureAwait(false);
                if (affected != 1) throw new InvalidOperationException("Import receipt completion lost its owner.");
                return Result<ImportOrganizationPositionRowResult>.Success(
                    new ImportOrganizationPositionRowResult(lineNumber, true, positionId, null, null));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 必须在竞争事务已回滚后重读，不能在失败事务内吞掉唯一键错误。
            receipt = await queryExecutor.QuerySingleOrDefaultAsync<PositionImportReceiptRecord>(
                PositionImportSql.Find, key, cancellationToken).ConfigureAwait(false);
            if (receipt is null) throw;
            return ReplayImportRow(receipt, hash, lineNumber);
        }
    }

    private static Result<ImportOrganizationPositionRowResult> ReplayImportRow(
        PositionImportReceiptRecord receipt, string hash, int lineNumber)
    {
        if (receipt.PositionId is not Guid id || !string.Equals(receipt.PayloadHash, hash, StringComparison.Ordinal))
            return Result<ImportOrganizationPositionRowResult>.Failure(new Error(ValidationErrorCodes.Failed,
                "The import row identity was reused with different content or an incomplete receipt.", ErrorType.Conflict));
        return Result<ImportOrganizationPositionRowResult>.Success(
            new ImportOrganizationPositionRowResult(lineNumber, true, id, null, null));
    }
}
