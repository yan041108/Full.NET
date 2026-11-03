using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.ImportExport;

/// <summary>以任务和原始行号原子记录申请单导入回执；组织授权在本地写事务之前完成。</summary>
internal sealed class EnterpriseRequestImportWriter(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, ICurrentTenant tenant, IOrganizationOwnedEntityWriteAuthorizer authorizer,
    IIdGenerator ids, IClock clock, IBackgroundSessionAuthorization sessions)
{
    private const string Table = "demo_enterprise_request_import_row";
    internal static readonly SqlStatement FindRequest = new("enterprise_request.import.find_request",
        $"SELECT RequestId FROM {Table} WHERE TenantId=@TenantId AND TaskId=@TaskId AND LineNumber=@LineNumber",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindDigest = new("enterprise_request.import.find_digest",
        $"SELECT Digest FROM {Table} WHERE TenantId=@TenantId AND TaskId=@TaskId AND LineNumber=@LineNumber",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertReceipt = new("enterprise_request.import.insert_receipt",
        $"INSERT INTO {Table} (Id,TenantId,TaskId,LineNumber,Digest,RequestId,CreatedAtUtc) VALUES (@Id,@TenantId,@TaskId,@LineNumber,@Digest,@RequestId,@CreatedAtUtc)",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal async Task<Result<Guid>> WriteAsync(EnterpriseRequestWorkbookReader.Row row,
        StaticImportPreviewContext context, CancellationToken token)
    {
        if (!tenant.IsAvailable || tenant.IsHost || tenant.Id is not { } tenantId
            || context.TaskId is not { } taskId || taskId == Guid.Empty || context.SourceFileId is not { } fileId || fileId == Guid.Empty
            || context.RequestedByUserId == Guid.Empty || row.LineNumber is < 2 or > 1001 || row.ErrorCode is not null)
            return Failure("enterprise_request.import.context_invalid", ErrorType.Validation);
        if (context.SessionBinding is not { } binding || binding.UserId != context.RequestedByUserId || binding.TenantId != tenantId)
            return Failure(ImportTaskAuthorityErrorCodes.Denied, ErrorType.Forbidden);
        var authorized = await authorizer.EnsureCanWriteAsync(tenantId, row.OrganizationUnitId,
            context.RequestedByUserId, token).ConfigureAwait(false);
        if (!authorized.IsSuccess) return Result<Guid>.Failure(authorized.Error!);
        if (!authorized.Value) return Failure("enterprise_request.import.forbidden", ErrorType.Forbidden);
        // 数据范围与来源操作权限分别确认，且权威会话检查必须在本模块行事务之前完成。
        var actor = await sessions.AuthorizeAllAsync(binding, [ImportExportPermissions.ImportTasksExecute, EnterpriseRequestPermissions.Create], token).ConfigureAwait(false);
        if (actor is null || actor.UserId != context.RequestedByUserId || actor.TenantId != tenantId)
            return Failure(ImportTaskAuthorityErrorCodes.Denied, ErrorType.Forbidden);
        string[] fields = [fileId.ToString("D"), context.RequestedByUserId.ToString("D"), row.Request.RequestNumber,
            row.Request.Title, row.Request.TotalAmount.ToString(CultureInfo.InvariantCulture),
            row.Request.ApplicantUserId.ToString("D"), row.OrganizationUnitId.ToString("D")];
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(fields.Select(value => $"{value.Length}:{value}")))));
        var key = new Dictionary<string, object?> { ["TenantId"] = tenantId, ["TaskId"] = taskId, ["LineNumber"] = row.LineNumber };
        async Task<Result<Guid>> Replay(Guid requestId)
        {
            var saved = await queries.QuerySingleOrDefaultAsync<string>(FindDigest, key, token).ConfigureAwait(false);
            return saved == digest ? Result<Guid>.Success(requestId) : Failure("enterprise_request.import.row_conflict", ErrorType.Conflict);
        }
        var existing = await queries.QuerySingleOrDefaultAsync<Guid?>(FindRequest, key, token).ConfigureAwait(false);
        if (existing is { } known) return await Replay(known).ConfigureAwait(false);
        try
        {
            return await transaction.ExecuteResultAsync(async inner =>
            {
                var now = clock.UtcNow;
                var requestId = ids.NewId();
                var receipt = new Dictionary<string, object?>(key)
                {
                    ["Id"] = ids.NewId(), ["Digest"] = digest, ["RequestId"] = requestId, ["CreatedAtUtc"] = now,
                };
                // 唯一键先串行相同行；业务与回执同事务，响应或任务检查点丢失后只读重放。
                if (await commands.ExecuteAsync(InsertReceipt, receipt, inner).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Import receipt must affect one row.");
                var parameters = new Dictionary<string, object?>
                {
                    ["Id"] = requestId, ["TenantId"] = tenantId, ["OrganizationUnitId"] = row.OrganizationUnitId,
                    ["RequestNumber"] = row.Request.RequestNumber, ["Title"] = row.Request.Title, ["Status"] = row.Request.Status,
                    ["TotalAmount"] = row.Request.TotalAmount, ["ApplicantUserId"] = row.Request.ApplicantUserId,
                    ["Version"] = 1L, ["CreatedAtUtc"] = now, ["CreatedById"] = context.RequestedByUserId,
                    ["UpdatedAtUtc"] = null, ["UpdatedById"] = null, ["IsDeleted"] = false, ["DeletedAtUtc"] = null, ["DeletedById"] = null,
                };
                if (await commands.ExecuteAsync(EnterpriseRequestSql.InsertStatement, parameters, inner).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Imported request must affect one row.");
                return Result<Guid>.Success(requestId);
            }, token).ConfigureAwait(false);
        }
        catch (DataCommandException error) when (error.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 并发输家必须在自己的事务回滚后读取赢家；其他业务唯一约束冲突不能冒充成功。
            var winner = await queries.QuerySingleOrDefaultAsync<Guid?>(FindRequest, key, token).ConfigureAwait(false);
            return winner is { } id ? await Replay(id).ConfigureAwait(false) : Failure("enterprise_request.import.row_conflict", ErrorType.Conflict);
        }
    }

    private static Result<Guid> Failure(string code, ErrorType type) => Result<Guid>.Failure(new Error(code, code, type));
}
