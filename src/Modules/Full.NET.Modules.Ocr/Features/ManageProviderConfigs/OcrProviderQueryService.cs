using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ocr.Contracts;
using Full.NET.Modules.Ocr.Persistence;

/// <summary>Ocr 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Ocr.Features.ManageProviderConfigs;

internal sealed class OcrProviderQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<OcrProviderConfigResponse>> GetByKeyAsync(
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var row = await FindRecordByKeyAsync(providerKey, cancellationToken).ConfigureAwait(false);
        return row is null
            ? NotFound()
            : Result<OcrProviderConfigResponse>.Success(OcrProviderMapper.Map(row));
    }

    internal async Task<OcrProviderConfigRecord?> FindRecordByKeyAsync(
        string providerKey,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<OcrProviderConfigRecord>(
                OcrProviderSql.FindByKey,
                OcrSqlParameters.Create([("ProviderKey", providerKey)]),
                cancellationToken)
            .ConfigureAwait(false);

    private static Result<OcrProviderConfigResponse> NotFound() =>
        Result<OcrProviderConfigResponse>.Failure(new Error(
            OcrErrorCodes.ProviderNotFound,
            "The OCR provider configuration was not found.",
            ErrorType.NotFound));
}
