using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Cryptography.Configuration;
using Full.NET.Modules.Cryptography.Contracts;
using Full.NET.Modules.Cryptography.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Cryptography.Features.ManageGmKeys;

/// <summary>国密密钥目录查询。</summary>
internal sealed class CryptographyKeyQueryService(
    IQueryExecutor queryExecutor,
    IOptions<CryptographyOptions> options)
{
    /// <summary>列出全部国密密钥目录项。</summary>
    public async Task<Result<IReadOnlyList<CryptographyKeyResponse>>> ListAsync(
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<CryptographyKeyRecord>(
                CryptographySql.ListKeys,
                CryptographySqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<CryptographyKeyResponse>>.Success(
            rows.Select(Map).ToArray());
    }

    /// <summary>按标识查询单条国密密钥。</summary>
    public async Task<Result<CryptographyKeyResponse>> GetByIdAsync(
        Guid keyId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<CryptographyKeyRecord>(
                CryptographySql.FindKeyById,
                CryptographySqlParameters.Create(("Id", keyId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<CryptographyKeyResponse>.Failure(new Error(
                CryptographyErrorCodes.KeyNotFound,
                "The cryptography key was not found.",
                ErrorType.NotFound))
            : Result<CryptographyKeyResponse>.Success(Map(record));
    }

    internal async Task<Result<CryptographyKeyRecord>> FindByKeyKeyAsync(
        string keyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyKey))
        {
            return Result<CryptographyKeyRecord>.Failure(new Error(
                CryptographyErrorCodes.SignValidationFailed,
                "The cryptography key key is required.",
                ErrorType.Validation));
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<CryptographyKeyRecord>(
                CryptographySql.FindKeyByKeyKey,
                CryptographySqlParameters.Create(("KeyKey", keyKey.Trim())),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<CryptographyKeyRecord>.Failure(new Error(
                CryptographyErrorCodes.KeyNotFound,
                "The cryptography key was not found.",
                ErrorType.NotFound))
            : Result<CryptographyKeyRecord>.Success(record);
    }

    private CryptographyKeyResponse Map(CryptographyKeyRecord record) =>
        new(
            record.Id,
            record.KeyKey,
            record.DisplayName,
            record.Description,
            record.Algorithm,
            record.Purpose,
            record.PublicKeyHex,
            record.PublicKeyFingerprint,
            record.Status,
            IsPrivateKeyConfigured(record.KeyKey),
            record.SortOrder,
            record.CreatedAtUtc,
            record.UpdatedAtUtc);

    private bool IsPrivateKeyConfigured(string keyKey) =>
        options.Value.Sm2PrivateKeys.TryGetValue(keyKey, out var privateKeyHex)
        && !string.IsNullOrWhiteSpace(privateKeyHex);
}
