namespace Full.NET.Modules.Ocr.Persistence;

internal sealed class OcrProviderConfigRecord
{
    public Guid Id { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiKeyProtected { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset? LastTestedAtUtc { get; set; }
    public string? LastTestStatusKey { get; set; }
    public string? LastTestMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

internal sealed class OcrIdCardTaskRecord
{
    public Guid Id { get; set; }
    public Guid SourceFileId { get; set; }
    public string StatusKey { get; set; } = string.Empty;
    public string? RecognizedName { get; set; }
    public string? RecognizedIdNumber { get; set; }
    public string? RecognizedGender { get; set; }
    public string? RecognizedNation { get; set; }
    public string? RecognizedAddress { get; set; }
    public string? RecognizedBirthDate { get; set; }
    public string? ConfirmedName { get; set; }
    public string? ConfirmedIdNumber { get; set; }
    public string? ConfirmedGender { get; set; }
    public string? ConfirmedNation { get; set; }
    public string? ConfirmedAddress { get; set; }
    public string? ConfirmedBirthDate { get; set; }
    public string? RawResultJson { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset? RecognizedAtUtc { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public DateTimeOffset? RejectedAtUtc { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public int Version { get; set; }
}
