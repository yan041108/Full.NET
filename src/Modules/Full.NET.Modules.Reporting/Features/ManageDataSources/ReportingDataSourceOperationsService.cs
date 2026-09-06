using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Connectivity;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Reporting.Security;

namespace Full.NET.Modules.Reporting.Features.ManageDataSources;

/// <summary>报表数据源连接测试。</summary>
internal sealed class ReportingDataSourceOperationsService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ReportingDataSourceSecretProtector secretProtector,
    IClock clock)
{
    /// <summary>测试已保存数据源的连接可用性。</summary>
    /// <param name="dataSourceId">数据源标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>测试结果并持久化最近测试摘要。</returns>
    public async Task<Result<TestReportingDataSourceResult>> TestConnectionAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDataSourceRecord>(
                ReportingDataSourceSql.FindById,
                ReportingSqlParameters.Create(("DataSourceId", dataSourceId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return Result<TestReportingDataSourceResult>.Failure(new Error(
                ReportingErrorCodes.DataSourceNotFound,
                "The reporting data source was not found.",
                ErrorType.NotFound));
        }

        if (string.IsNullOrWhiteSpace(row.PasswordProtected))
        {
            return Result<TestReportingDataSourceResult>.Failure(new Error(
                ReportingErrorCodes.DataSourcePasswordRequired,
                "Password is not configured for this reporting data source.",
                ErrorType.Validation));
        }

        var password = secretProtector.Unprotect(row.PasswordProtected);
        var outcome = await ReportingDataSourceConnectionTester
            .TestAsync(row, password, cancellationToken)
            .ConfigureAwait(false);
        var statusKey = outcome.Succeeded
            ? ReportingDataSourceTestStatusKeys.Succeeded
            : ReportingDataSourceTestStatusKeys.Failed;
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                ReportingDataSourceSql.UpdateTestResult,
                ReportingSqlParameters.Create(
                    ("DataSourceId", dataSourceId),
                    ("LastTestedAtUtc", now),
                    ("LastTestStatusKey", statusKey),
                    ("LastTestMessage", outcome.Message),
                    ("UpdatedAtUtc", now),
                    ("Version", row.Version)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<TestReportingDataSourceResult>.Success(
            new TestReportingDataSourceResult(outcome.Succeeded, outcome.Message));
    }
}
