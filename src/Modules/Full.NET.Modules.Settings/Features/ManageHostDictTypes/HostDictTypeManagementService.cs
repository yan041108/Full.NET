using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;

namespace Full.NET.Modules.Settings.Features.ManageHostDictTypes;

/// <summary>Host 数据字典类型创建、更新与禁用。</summary>
internal sealed partial class HostDictTypeManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDictTypeQueryService dictTypeQueries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<DictTypeResponse>> CreateAsync(
        CreateDictTypeRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<DictTypeResponse>> UpdateAsync(
        Guid dictTypeId,
        UpdateDictTypeRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(dictTypeId, request, token),
            cancellationToken);

    public Task<Result<DictTypeResponse>> DisableAsync(
        Guid dictTypeId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(dictTypeId, token),
            cancellationToken);

    private async Task<Result<DictTypeResponse>> CreateCoreAsync(
        CreateDictTypeRequest request,
        CancellationToken cancellationToken)
    {
        var code = NormalizeCode(request.Code);
        if (!CodePattern().IsMatch(code))
        {
            return ValidationFailure(
                "Dictionary type code must be 3-64 lowercase letters, numbers, underscores, or hyphens.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 128)
        {
            return ValidationFailure("Dictionary type name is invalid.");
        }

        var description = NormalizeOptionalText(request.Description, 512);
        if (description is { Length: > 512 })
        {
            return ValidationFailure("Dictionary type description must not exceed 512 characters.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                DictTypeSql.FindByCode,
                new { Code = code },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return CodeExists();
        }

        var now = clock.UtcNow;
        var dictTypeId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                DictTypeSql.Insert,
                new
                {
                    Id = dictTypeId,
                    Code = code,
                    Name = name,
                    Description = description,
                    DisplayOrder = request.DisplayOrder,
                    IsActive = true,
                    CreatedAtUtc = now,
                    Version = 1,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await dictTypeQueries.GetByIdAsync(dictTypeId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<DictTypeResponse>> UpdateCoreAsync(
        Guid dictTypeId,
        UpdateDictTypeRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 128)
        {
            return ValidationFailure("Dictionary type name is invalid.");
        }

        var description = NormalizeOptionalText(request.Description, 512);
        if (description is { Length: > 512 })
        {
            return ValidationFailure("Dictionary type description must not exceed 512 characters.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                DictTypeSql.FindIdentityById,
                new { DictTypeId = dictTypeId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                DictTypeSql.UpdateHostDictType,
                new
                {
                    DictTypeId = dictTypeId,
                    Name = name,
                    Description = description,
                    DisplayOrder = request.DisplayOrder,
                    UpdatedAtUtc = now,
                    request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(dictTypeId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await dictTypeQueries.GetByIdAsync(dictTypeId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<DictTypeResponse>> DisableCoreAsync(
        Guid dictTypeId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                DictTypeSql.FindIdentityById,
                new { DictTypeId = dictTypeId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (!existing.IsActive)
        {
            return await dictTypeQueries.GetByIdAsync(dictTypeId, cancellationToken)
                .ConfigureAwait(false);
        }

        var activeItemCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                DictTypeSql.CountActiveItems,
                new { DictTypeId = dictTypeId },
                cancellationToken)
            .ConfigureAwait(false);
        if (activeItemCount > 0)
        {
            return HasActiveItems();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                DictTypeSql.DisableHostDictType,
                new
                {
                    DictTypeId = dictTypeId,
                    UpdatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
        }

        return await dictTypeQueries.GetByIdAsync(dictTypeId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<DictTypeResponse>> ResolveUpdateFailureAsync(
        Guid dictTypeId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                DictTypeSql.FindIdentityById,
                new { DictTypeId = dictTypeId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        return VersionConflict();
    }

    private static string NormalizeCode(string? code) =>
        code?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    private static Result<DictTypeResponse> ValidationFailure(string message) =>
        Result<DictTypeResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<DictTypeResponse> NotFound() =>
        Result<DictTypeResponse>.Failure(new Error(
            SettingsErrorCodes.DictTypeNotFound,
            "The dictionary type was not found.",
            ErrorType.NotFound));

    private static Result<DictTypeResponse> CodeExists() =>
        Result<DictTypeResponse>.Failure(new Error(
            SettingsErrorCodes.DictTypeCodeExists,
            "A dictionary type with the same code already exists.",
            ErrorType.Conflict));

    private static Result<DictTypeResponse> VersionConflict() =>
        Result<DictTypeResponse>.Failure(new Error(
            SettingsErrorCodes.DictTypeVersionConflict,
            "The dictionary type record was updated concurrently.",
            ErrorType.Conflict));

    private static Result<DictTypeResponse> HasActiveItems() =>
        Result<DictTypeResponse>.Failure(new Error(
            SettingsErrorCodes.DictTypeHasActiveItems,
            "The dictionary type still has active items.",
            ErrorType.BusinessRule));

    [GeneratedRegex(
        "^[a-z][a-z0-9_-]{1,62}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
