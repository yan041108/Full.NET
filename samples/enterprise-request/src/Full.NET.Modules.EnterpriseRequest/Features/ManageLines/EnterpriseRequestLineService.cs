using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.ManageLines;

/// <summary>明细整组编辑与主表 CAS 同事务；审批和普通编辑共享主表版本。</summary>
internal sealed class EnterpriseRequestLineService(EnterpriseRequestQueryService requests, IQueryExecutor queries,
    ICommandExecutor commands, ICommandTransaction transaction, ICurrentTenant tenant,
    IOrganizationOwnedEntityWriteAuthorizer authorizer, IIdGenerator ids, IClock clock)
{
    internal const int MaximumLines = 200;
    private const decimal MaximumAmount = 9999999999999999.99m;

    public async Task<Result<EnterpriseRequestLinesResponse>> GetAsync(Guid id, Guid actor, bool isSuperAdministrator,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidContext(id, actor)) return NotFound();
        var read = await requests.GetByIdAsync(id, actor, isSuperAdministrator, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) return Result<EnterpriseRequestLinesResponse>.Failure(read.Error!);
        var parent = read.Value!;
        if (!ValidParent(parent, id)) return NotFound();
        var rows = await queries.QueryAsync<EnterpriseRequestLineRow>(EnterpriseRequestLineSql.Read,
            Parameters(("RequestId", id)), cancellationToken).ConfigureAwait(false);
        // 读取跨越一次明细提交或审批时拒绝混合快照，客户端可显式刷新。
        var after = await requests.GetByIdAsync(id, actor, isSuperAdministrator, cancellationToken).ConfigureAwait(false);
        if (!after.IsSuccess) return Result<EnterpriseRequestLinesResponse>.Failure(after.Error!);
        if (!ValidParent(after.Value!, id)) return NotFound();
        if (after.Value!.Version != parent.Version || after.Value.OrganizationUnitId != parent.OrganizationUnitId ||
            after.Value.Status != parent.Status || rows.Any(row => row.TenantId != tenant.Id || row.RequestId != id || row.Id == Guid.Empty))
            return Conflict();
        return Result<EnterpriseRequestLinesResponse>.Success(new(id, parent.Version, parent.Status, parent.TotalAmount,
            rows.Select(row => new EnterpriseRequestLineResponse(row.Id, row.LineNumber, row.ItemDescription,
                row.Quantity, row.UnitPrice, row.LineAmount)).ToArray()));
    }

    public async Task<Result<EnterpriseRequestLinesResponse>> ReplaceAsync(Guid id, ReplaceEnterpriseRequestLinesRequest input,
        Guid actor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidContext(id, actor)) return NotFound();
        var read = await requests.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) return Result<EnterpriseRequestLinesResponse>.Failure(read.Error!);
        var parent = read.Value!;
        if (!ValidParent(parent, id)) return NotFound();
        // 组织 Port 在本模块事务之外调用，明细写入不能借 Read 权限绕过组织写入授权。
        var access = await authorizer.EnsureCanWriteAsync(tenant.Id!.Value, parent.OrganizationUnitId, actor, cancellationToken).ConfigureAwait(false);
        if (!access.IsSuccess || !access.Value)
            return Result<EnterpriseRequestLinesResponse>.Failure(access.Error ?? new Error(OrganizationErrorCodes.WriteAccessDenied,
                "Write access to the organization unit was denied.", ErrorType.Forbidden));
        if (input.Version != parent.Version || parent.Version <= 0 || parent.Version == long.MaxValue) return Conflict();
        if (parent.Status != EnterpriseRequestStatusKeys.Draft)
            return Result<EnterpriseRequestLinesResponse>.Failure(new Error(EnterpriseRequestWorkflowErrorCodes.InvalidStatus,
                "Only draft requests can have their lines edited.", ErrorType.Conflict));
        var validation = Calculate(input.Items);
        if (!validation.IsSuccess) return Result<EnterpriseRequestLinesResponse>.Failure(validation.Error!);
        var values = validation.Value!;
        var lines = values.Select((line, index) => new EnterpriseRequestLineResponse(ids.NewId(), index + 1,
            line.Input.ItemDescription.Trim(), line.Input.Quantity, line.Input.UnitPrice, line.Amount)).ToArray();
        var total = values.Sum(line => line.Amount);
        var now = clock.UtcNow;
        return await transaction.ExecuteResultAsync(async token => {
            if (await commands.ExecuteAsync(EnterpriseRequestLineSql.UpdateParent, Parameters(("RequestId", id),
                    ("OrganizationUnitId", parent.OrganizationUnitId), ("TotalAmount", total), ("Now", now),
                    ("ActorId", actor), ("ExpectedStatus", EnterpriseRequestStatusKeys.Draft), ("Version", input.Version)), token).ConfigureAwait(false) != 1)
                return Conflict();
            await commands.ExecuteAsync(EnterpriseRequestLineSql.Delete, Parameters(("RequestId", id)), token).ConfigureAwait(false);
            foreach (var line in lines)
                if (await commands.ExecuteAsync(EnterpriseRequestLineSql.Insert, Parameters(("Id", line.Id), ("RequestId", id),
                        ("LineNumber", line.LineNumber), ("ItemDescription", line.ItemDescription), ("Quantity", line.Quantity),
                        ("UnitPrice", line.UnitPrice), ("LineAmount", line.LineAmount), ("Now", now), ("ActorId", actor)), token).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("A request line insert must affect exactly one row.");
            return Result<EnterpriseRequestLinesResponse>.Success(new(id, input.Version + 1, parent.Status, total, lines));
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static Result<IReadOnlyList<CalculatedLine>> Calculate(IReadOnlyList<EnterpriseRequestLineInput>? items)
    {
        if (items is null || items.Count > MaximumLines) return InvalidLines();
        var result = new List<CalculatedLine>(items.Count);
        decimal total = 0;
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.ItemDescription) || item.ItemDescription.Length > 200 ||
                item.Quantity <= 0 || item.Quantity > 99999999999999.9999m || decimal.Round(item.Quantity, 4) != item.Quantity ||
                item.UnitPrice < 0 || item.UnitPrice > MaximumAmount || decimal.Round(item.UnitPrice, 2) != item.UnitPrice)
                return InvalidLines();
            decimal amount;
            try { amount = decimal.Round(checked(item.Quantity * item.UnitPrice), 2, MidpointRounding.AwayFromZero); }
            catch (OverflowException) { return InvalidLines(); }
            if (amount > MaximumAmount || total > MaximumAmount - amount) return InvalidLines();
            total += amount; result.Add(new(item, amount));
        }
        return Result<IReadOnlyList<CalculatedLine>>.Success(result);
    }
    internal sealed record CalculatedLine(EnterpriseRequestLineInput Input, decimal Amount);
    private static Result<IReadOnlyList<CalculatedLine>> InvalidLines() => Result<IReadOnlyList<CalculatedLine>>.Failure(new Error(
        ValidationErrorCodes.Failed, "Lines must contain at most 200 valid descriptions, quantities and prices within decimal storage limits.",
        ErrorType.Validation, new Dictionary<string, string[]> { ["Items"] = ["The request lines are invalid."] }));
    private bool ValidContext(Guid id, Guid actor) => tenant.IsAvailable && !tenant.IsHost && tenant.Id is { } tenantId &&
        tenantId != Guid.Empty && id != Guid.Empty && actor != Guid.Empty;
    private bool ValidParent(EnterpriseRequestResponse parent, Guid id) => parent.Id == id && parent.TenantId == tenant.Id && !parent.IsDeleted;
    private static Result<EnterpriseRequestLinesResponse> NotFound() => Result<EnterpriseRequestLinesResponse>.Failure(new Error(
        EnterpriseRequestErrorCodes.NotFound, "The resource was not found.", ErrorType.NotFound));
    private static Result<EnterpriseRequestLinesResponse> Conflict() => Result<EnterpriseRequestLinesResponse>.Failure(new Error(
        EnterpriseRequestErrorCodes.VersionConflict, "The request snapshot changed. Refresh before retrying.", ErrorType.Conflict));
    private static Dictionary<string, object?> Parameters(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
