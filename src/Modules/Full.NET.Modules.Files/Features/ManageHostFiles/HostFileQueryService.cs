using Full.NET.Abstractions.Results;

using Full.NET.Data.Abstractions;

using Full.NET.Modules.Files.Contracts;

using Full.NET.Modules.Files.Persistence;

using Microsoft.Extensions.Options;



namespace Full.NET.Modules.Files.Features.ManageHostFiles;



/// <summary>Host 文件元数据分页列表与详情只读查询。</summary>

internal sealed class HostFileQueryService(

    IQueryExecutor queryExecutor,

    IOptions<DatabaseOptions> databaseOptions)

{

    public async Task<Result<PagedResult<HostFileResponse>>> ListAsync(

        int page,

        int pageSize,

        CancellationToken cancellationToken = default)

    {

        page = Math.Max(page, 1);

        pageSize = Math.Clamp(pageSize, 1, 100);

        var offset = ((long)page - 1) * pageSize;

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(

                HostFileSql.CountActiveHostFiles,

                cancellationToken: cancellationToken)

            .ConfigureAwait(false);

        var statement = databaseOptions.Value.Provider switch

        {

            DatabaseProvider.SqlServer => HostFileSql.ListActiveHostFilesSqlServer,

            DatabaseProvider.MySql => HostFileSql.ListActiveHostFilesMySql,

            _ => throw new InvalidOperationException(

                "The configured database provider is not supported."),

        };

        var rows = await queryExecutor.QueryAsync<HostFileListRecord>(

                statement,

                new { Offset = offset, PageSize = pageSize },

                cancellationToken)

            .ConfigureAwait(false);

        var items = rows.Select(Map).ToArray();

        return Result<PagedResult<HostFileResponse>>.Success(

            new PagedResult<HostFileResponse>(items, page, pageSize, total));

    }



    public async Task<Result<HostFileResponse>> GetByIdAsync(

        Guid fileId,

        CancellationToken cancellationToken = default)

    {

        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(

                HostFileSql.FindActiveById,

                new { FileId = fileId },

                cancellationToken)

            .ConfigureAwait(false);

        if (record is null)

        {

            return NotFound();

        }



        return Result<HostFileResponse>.Success(Map(record));

    }



    public async Task<Result<HostFileDetailRecord>> GetDetailByIdAsync(

        Guid fileId,

        CancellationToken cancellationToken = default)

    {

        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(

                HostFileSql.FindActiveById,

                new { FileId = fileId },

                cancellationToken)

            .ConfigureAwait(false);

        return record is null

            ? Result<HostFileDetailRecord>.Failure(new Error(

                FilesErrorCodes.FileNotFound,

                "The file was not found.",

                ErrorType.NotFound))

            : Result<HostFileDetailRecord>.Success(record);

    }



    internal static HostFileResponse Map(HostFileListRecord record) =>

        new(

            record.Id,

            record.OriginalFileName,

            record.ContentType,

            record.SizeBytes,

            record.ContentHash,

            record.CreatedAtUtc,

            record.CreatedByUserId);



    internal static HostFileResponse Map(HostFileDetailRecord record) =>

        new(

            record.Id,

            record.OriginalFileName,

            record.ContentType,

            record.SizeBytes,

            record.ContentHash,

            record.CreatedAtUtc,

            record.CreatedByUserId);



    private static Result<HostFileResponse> NotFound() =>

        Result<HostFileResponse>.Failure(new Error(

            FilesErrorCodes.FileNotFound,

            "The file was not found.",

            ErrorType.NotFound));

}

