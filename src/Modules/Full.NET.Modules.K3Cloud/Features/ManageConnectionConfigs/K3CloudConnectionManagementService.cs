using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.K3Cloud.Contracts;
using Full.NET.Modules.K3Cloud.Domain;
using Full.NET.Modules.K3Cloud.Persistence;
using Full.NET.Modules.K3Cloud.Security;

namespace Full.NET.Modules.K3Cloud.Features.ManageConnectionConfigs;

/// <summary>K3Cloud 连接配置维护。</summary>
internal sealed class K3CloudConnectionManagementService(
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    K3CloudConnectionQueryService queries,
    K3CloudPasswordProtector passwordProtector,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<K3CloudConnectionConfigResponse>> CreateAsync(
        CreateK3CloudConnectionConfigRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => CreateCoreAsync(request, token), cancellationToken);

    public Task<Result<K3CloudConnectionConfigResponse>> UpdateAsync(
        Guid connectionConfigId,
        UpdateK3CloudConnectionConfigRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(connectionConfigId, request, token),
            cancellationToken);

    private async Task<Result<K3CloudConnectionConfigResponse>> CreateCoreAsync(
        CreateK3CloudConnectionConfigRequest request,
        CancellationToken cancellationToken)
    {
        var validation = K3CloudFieldValidator.ValidateConnectionMetadata(
            request.Name,
            request.BaseUrl,
            request.AcctId,
            request.Username,
            request.Lcid);
        if (!validation.IsSuccess)
        {
            return Result<K3CloudConnectionConfigResponse>.Failure(validation.Error!);
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return ValidationFailure("Password is required when creating a K3Cloud connection.");
        }

        var id = idGenerator.NewId();
        var now = clock.UtcNow;
        if (request.IsDefault)
        {
            await commandExecutor.ExecuteAsync(
                    K3CloudConnectionSql.ClearDefaultExcept,
                    K3CloudSqlParameters.Create([
                        ("ExceptId", id),
                        ("UpdatedAtUtc", now)]),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await commandExecutor.ExecuteAsync(
                K3CloudConnectionSql.Insert,
                K3CloudSqlParameters.Create([
                    ("Id", id),
                    ("Name", request.Name.Trim()),
                    ("BaseUrl", request.BaseUrl.Trim()),
                    ("AcctId", request.AcctId.Trim()),
                    ("Username", request.Username.Trim()),
                    ("PasswordProtected", passwordProtector.Protect(request.Password.Trim())),
                    ("Lcid", request.Lcid),
                    ("IsDefault", request.IsDefault),
                    ("IsEnabled", request.IsEnabled),
                    ("LastTestedAtUtc", null),
                    ("LastTestStatusKey", null),
                    ("LastTestMessage", null),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", null),
                    ("Version", 1)]),
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<K3CloudConnectionConfigResponse>> UpdateCoreAsync(
        Guid connectionConfigId,
        UpdateK3CloudConnectionConfigRequest request,
        CancellationToken cancellationToken)
    {
        var validation = K3CloudFieldValidator.ValidateConnectionMetadata(
            request.Name,
            request.BaseUrl,
            request.AcctId,
            request.Username,
            request.Lcid);
        if (!validation.IsSuccess)
        {
            return Result<K3CloudConnectionConfigResponse>.Failure(validation.Error!);
        }

        var record = await queries.FindRecordAsync(connectionConfigId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.Version != request.Version)
        {
            return VersionConflict();
        }

        var passwordProtected = string.IsNullOrWhiteSpace(request.Password)
            ? record.PasswordProtected
            : passwordProtector.Protect(request.Password.Trim());
        var now = clock.UtcNow;
        if (request.IsDefault)
        {
            await commandExecutor.ExecuteAsync(
                    K3CloudConnectionSql.ClearDefaultExcept,
                    K3CloudSqlParameters.Create([
                        ("ExceptId", connectionConfigId),
                        ("UpdatedAtUtc", now)]),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var affected = await commandExecutor.ExecuteAsync(
                K3CloudConnectionSql.Update,
                K3CloudSqlParameters.Create([
                    ("Id", connectionConfigId),
                    ("Name", request.Name.Trim()),
                    ("BaseUrl", request.BaseUrl.Trim()),
                    ("AcctId", request.AcctId.Trim()),
                    ("Username", request.Username.Trim()),
                    ("PasswordProtected", passwordProtected),
                    ("Lcid", request.Lcid),
                    ("IsDefault", request.IsDefault),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(connectionConfigId, cancellationToken).ConfigureAwait(false);
    }

    private static Result<K3CloudConnectionConfigResponse> NotFound() =>
        Result<K3CloudConnectionConfigResponse>.Failure(new Error(
            K3CloudErrorCodes.ConnectionNotFound,
            "The K3Cloud connection configuration was not found.",
            ErrorType.NotFound));

    private static Result<K3CloudConnectionConfigResponse> VersionConflict() =>
        Result<K3CloudConnectionConfigResponse>.Failure(new Error(
            K3CloudErrorCodes.ConnectionConcurrencyConflict,
            "The K3Cloud connection configuration was modified by another request.",
            ErrorType.Conflict));

    private static Result<K3CloudConnectionConfigResponse> ValidationFailure(string message) =>
        Result<K3CloudConnectionConfigResponse>.Failure(new Error(
            K3CloudErrorCodes.ConnectionInvalid,
            message,
            ErrorType.Validation));
}
