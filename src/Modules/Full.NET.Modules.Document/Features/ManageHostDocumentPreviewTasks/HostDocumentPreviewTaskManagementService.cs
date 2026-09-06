using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Domain;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;

/// <summary>创建 Host 文档 Office 预览转换任务。</summary>
internal sealed class HostDocumentPreviewTaskManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IHostFileContentReader hostFileContentReader,
    IClock clock,
    IIdGenerator idGenerator,
    IOptionsMonitor<DocumentOfficePreviewConversionOptions> options)
{
    public async Task<Result<HostDocumentPreviewTaskResponse>> CreateAsync(
        CreateHostDocumentPreviewTaskRequest request,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        var item = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", request.DocumentItemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return Result<HostDocumentPreviewTaskResponse>.Failure(DocumentNotFoundError());
        }

        var fileIdResult = await ResolveFileIdAsync(request, cancellationToken).ConfigureAwait(false);
        if (!fileIdResult.IsSuccess)
        {
            return Result<HostDocumentPreviewTaskResponse>.Failure(fileIdResult.Error!);
        }

        var contentProbe = await hostFileContentReader
            .OpenReadyContentAsync(fileIdResult.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!contentProbe.IsSuccess)
        {
            return Result<HostDocumentPreviewTaskResponse>.Failure(contentProbe.Error!);
        }

        var probe = contentProbe.Value!;
        var taskId = idGenerator.NewId();
        try
        {
            if (!DocumentOfficePreviewSourcePolicy.IsOfficeMime(probe.ContentType))
            {
                return Result<HostDocumentPreviewTaskResponse>.Failure(
                    new Error(
                        DocumentErrorCodes.OfficePreviewUnsupportedSource,
                        "Only Office documents can be submitted for preview conversion.",
                        ErrorType.BusinessRule));
            }

            if (probe.Content.CanSeek && probe.Content.Length > options.CurrentValue.MaxInputBytes)
            {
                return Result<HostDocumentPreviewTaskResponse>.Failure(
                    new Error(
                        DocumentErrorCodes.OfficePreviewInputTooLarge,
                        "Office preview conversion input exceeds configured size limit.",
                        ErrorType.BusinessRule));
            }

            var sourceFileName = probe.OriginalFileName;
            var sourceMimeType = probe.ContentType;
            var now = clock.UtcNow;
            var providerKey = options.CurrentValue.ProviderKey;
            await commandExecutor.ExecuteAsync(
                    DocumentPreviewTaskSql.Insert,
                    DocumentSqlParameters.Create(
                        ("Id", taskId),
                        ("DocumentItemId", request.DocumentItemId),
                        ("VersionId", request.VersionId),
                        ("DocumentTitle", item.Title),
                        ("SourceFileId", fileIdResult.Value),
                        ("SourceFileName", sourceFileName),
                        ("SourceMimeType", sourceMimeType),
                        ("OutputFileId", null),
                        ("StatusKey", HostDocumentPreviewTaskStatusKeys.Pending),
                        ("ProviderKey", providerKey),
                        ("ErrorCode", null),
                        ("RequestedByUserId", requestedByUserId),
                        ("CreatedAtUtc", now),
                        ("StartedAtUtc", null),
                        ("CompletedAtUtc", null),
                        ("Version", 1L)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await probe.Content.DisposeAsync().ConfigureAwait(false);
        }

        var created = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(
                DocumentPreviewTaskSql.FindById,
                DocumentSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        return created is null
            ? Result<HostDocumentPreviewTaskResponse>.Failure(TaskNotFoundError())
            : Result<HostDocumentPreviewTaskResponse>.Success(HostDocumentPreviewTaskMapper.Map(created));
    }

    private async Task<Result<Guid>> ResolveFileIdAsync(
        CreateHostDocumentPreviewTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (request.VersionId is null)
        {
            var item = await queryExecutor
                .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                    DocumentItemSql.FindActiveById,
                    DocumentSqlParameters.Create(("Id", request.DocumentItemId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (item?.FileId is null)
            {
                return Result<Guid>.Failure(NoCurrentVersionError());
            }

            return Result<Guid>.Success(item.FileId.Value);
        }

        var version = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentVersionRecord>(
                DocumentItemSql.FindVersionById,
                DocumentSqlParameters.Create(
                    ("VersionId", request.VersionId.Value),
                    ("DocumentItemId", request.DocumentItemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (version is null)
        {
            return Result<Guid>.Failure(DocumentNotFoundError());
        }

        return Result<Guid>.Success(version.FileId);
    }

    private static Error DocumentNotFoundError() =>
        new(DocumentErrorCodes.NotFound, "Document item was not found.", ErrorType.NotFound);

    private static Error TaskNotFoundError() =>
        new(DocumentErrorCodes.PreviewTaskNotFound, "Document preview task was not found.", ErrorType.NotFound);

    private static Error NoCurrentVersionError() =>
        new(DocumentErrorCodes.NoCurrentVersion, "Document item has no downloadable version.", ErrorType.NotFound);
}
