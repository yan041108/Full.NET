using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ocr.Persistence;

internal static class OcrIdCardTaskSql
{
    private const string Columns = """
        Id, SourceFileId, StatusKey, RecognizedName, RecognizedIdNumber, RecognizedGender, RecognizedNation,
        RecognizedAddress, RecognizedBirthDate, ConfirmedName, ConfirmedIdNumber, ConfirmedGender,
        ConfirmedNation, ConfirmedAddress, ConfirmedBirthDate, RawResultJson, FailureMessage,
        RecognizedAtUtc, ConfirmedAtUtc, RejectedAtUtc, ConfirmedByUserId, CreatedAtUtc, UpdatedAtUtc,
        CreatedByUserId, Version
        """;

    public static readonly SqlStatement Insert = new(
        "ocr.id_card_task.insert",
        $"""
        INSERT INTO fn_ocr_id_card_task
            ({Columns})
        VALUES
            (@Id, @SourceFileId, @StatusKey, @RecognizedName, @RecognizedIdNumber, @RecognizedGender, @RecognizedNation,
             @RecognizedAddress, @RecognizedBirthDate, @ConfirmedName, @ConfirmedIdNumber, @ConfirmedGender,
             @ConfirmedNation, @ConfirmedAddress, @ConfirmedBirthDate, @RawResultJson, @FailureMessage,
             @RecognizedAtUtc, @ConfirmedAtUtc, @RejectedAtUtc, @ConfirmedByUserId, @CreatedAtUtc, @UpdatedAtUtc,
             @CreatedByUserId, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "ocr.id_card_task.update",
        """
        UPDATE fn_ocr_id_card_task
        SET StatusKey = @StatusKey,
            RecognizedName = @RecognizedName,
            RecognizedIdNumber = @RecognizedIdNumber,
            RecognizedGender = @RecognizedGender,
            RecognizedNation = @RecognizedNation,
            RecognizedAddress = @RecognizedAddress,
            RecognizedBirthDate = @RecognizedBirthDate,
            ConfirmedName = @ConfirmedName,
            ConfirmedIdNumber = @ConfirmedIdNumber,
            ConfirmedGender = @ConfirmedGender,
            ConfirmedNation = @ConfirmedNation,
            ConfirmedAddress = @ConfirmedAddress,
            ConfirmedBirthDate = @ConfirmedBirthDate,
            RawResultJson = @RawResultJson,
            FailureMessage = @FailureMessage,
            RecognizedAtUtc = @RecognizedAtUtc,
            ConfirmedAtUtc = @ConfirmedAtUtc,
            RejectedAtUtc = @RejectedAtUtc,
            ConfirmedByUserId = @ConfirmedByUserId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "ocr.id_card_task.find_by_id",
        $"""
        SELECT {Columns}
        FROM fn_ocr_id_card_task
        WHERE Id = @TaskId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Count = new(
        "ocr.id_card_task.count",
        "SELECT COUNT(1) FROM fn_ocr_id_card_task",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSqlServer = new(
        "ocr.id_card_task.list.sqlserver",
        $"""
        SELECT {Columns}
        FROM fn_ocr_id_card_task
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMySql = new(
        "ocr.id_card_task.list.mysql",
        $"""
        SELECT {Columns}
        FROM fn_ocr_id_card_task
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);
}
