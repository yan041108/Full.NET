using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Persistence;

namespace Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;

/// <summary>只读查询 Host 流水号规则，供 DataApproval 桥接使用以避免与写入服务形成 DI 环。</summary>
internal sealed class HostSerialRuleReader(IQueryExecutor queryExecutor)
{
    public async Task<Result<SerialNumberRuleResponse>> GetAsync(
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(ruleId, cancellationToken).ConfigureAwait(false);
        return row is null
            ? NotFound()
            : Result<SerialNumberRuleResponse>.Success(HostSerialRuleService.Map(row));
    }

    internal Task<SerialNumberRuleRecord?> FindAsync(
        Guid ruleId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<SerialNumberRuleRecord>(
            SerialNumberSql.FindRuleById,
            SerialNumbersSqlParameters.Create(("Id", ruleId)),
            cancellationToken);

    private static Result<SerialNumberRuleResponse> NotFound() =>
        Result<SerialNumberRuleResponse>.Failure(new Error(
            SerialNumberErrorCodes.RuleNotFound,
            "The serial number rule was not found.",
            ErrorType.NotFound));
}