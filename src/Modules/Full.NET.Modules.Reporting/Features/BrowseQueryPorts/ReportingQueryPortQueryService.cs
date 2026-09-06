using Full.NET.Abstractions.Results;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.Modules.Reporting.Features.BrowseQueryPorts;

/// <summary>静态 Query Port 目录只读查询。</summary>
internal sealed class ReportingQueryPortQueryService
{
    public Result<IReadOnlyList<ReportingQueryPortDefinition>> ListAsync() =>
        Result<IReadOnlyList<ReportingQueryPortDefinition>>.Success(ReportingQueryPortCatalog.List());

    public Result<ReportingQueryPortDefinition> GetByKeyAsync(string queryPortKey)
    {
        var definition = ReportingQueryPortCatalog.TryGet(queryPortKey);
        return definition is null
            ? Result<ReportingQueryPortDefinition>.Failure(new Error(
                ReportingErrorCodes.QueryPortNotFound,
                "The query port was not found.",
                ErrorType.NotFound))
            : Result<ReportingQueryPortDefinition>.Success(definition);
    }
}
