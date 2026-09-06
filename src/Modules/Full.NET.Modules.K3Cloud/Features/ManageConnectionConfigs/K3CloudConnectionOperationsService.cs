using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.K3Cloud.Connectivity;
using Full.NET.Modules.K3Cloud.Contracts;
using Full.NET.Modules.K3Cloud.Persistence;
using Full.NET.Modules.K3Cloud.Security;

namespace Full.NET.Modules.K3Cloud.Features.ManageConnectionConfigs;

/// <summary>K3Cloud ValidateUser 连通性测试。</summary>
internal sealed class K3CloudConnectionOperationsService(
    ICommandExecutor commandExecutor,
    K3CloudConnectionQueryService queries,
    K3CloudPasswordProtector passwordProtector,
    K3CloudWebApiClient webApiClient,
    IClock clock)
{
    public async Task<Result<TestK3CloudConnectionConfigResult>> TestConnectivityAsync(
        Guid connectionConfigId,
        CancellationToken cancellationToken = default)
    {
        var record = await queries.FindRecordAsync(connectionConfigId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result<TestK3CloudConnectionConfigResult>.Failure(new Error(
                K3CloudErrorCodes.ConnectionNotFound,
                "The K3Cloud connection configuration was not found.",
                ErrorType.NotFound));
        }

        if (!record.IsEnabled)
        {
            return Result<TestK3CloudConnectionConfigResult>.Failure(new Error(
                K3CloudErrorCodes.ConnectionInvalid,
                "The K3Cloud connection configuration is disabled.",
                ErrorType.Validation));
        }

        var password = passwordProtector.Unprotect(record.PasswordProtected);
        var outcome = await webApiClient
            .ValidateUserAsync(record, password, cancellationToken)
            .ConfigureAwait(false);
        var statusKey = outcome.Succeeded ? "succeeded" : "failed";
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                K3CloudConnectionSql.UpdateTestResult,
                K3CloudSqlParameters.Create([
                    ("ConnectionConfigId", connectionConfigId),
                    ("LastTestedAtUtc", now),
                    ("LastTestStatusKey", statusKey),
                    ("LastTestMessage", outcome.Message),
                    ("UpdatedAtUtc", now),
                    ("Version", record.Version)]),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<TestK3CloudConnectionConfigResult>.Success(
            new TestK3CloudConnectionConfigResult(outcome.Succeeded, outcome.Message));
    }
}
