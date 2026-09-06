namespace Full.NET.Modules.Ocr.Contracts;

/// <summary>OCR Provider 配置响应。</summary>
public sealed record OcrProviderConfigResponse(
    Guid Id,
    string ProviderKey,
    string Name,
    string BaseUrl,
    bool HasApiKey,
    bool IsEnabled,
    DateTimeOffset? LastTestedAtUtc,
    string? LastTestStatusKey,
    string? LastTestMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>更新 OCR Provider 配置请求。</summary>
public sealed record UpdateOcrProviderConfigRequest(
    string Name,
    string BaseUrl,
    string? ApiKey,
    bool IsEnabled,
    int Version);

/// <summary>OCR Provider 测试结果。</summary>
public sealed record TestOcrProviderConfigResult(
    bool Succeeded,
    string Message);

/// <summary>身份证 OCR 任务响应；识别字段在未确认前可能脱敏。</summary>
public sealed record OcrIdCardTaskResponse(
    Guid Id,
    Guid SourceFileId,
    string StatusKey,
    string? RecognizedName,
    string? RecognizedIdNumber,
    string? RecognizedGender,
    string? RecognizedNation,
    string? RecognizedAddress,
    string? RecognizedBirthDate,
    string? ConfirmedName,
    string? ConfirmedIdNumber,
    string? ConfirmedGender,
    string? ConfirmedNation,
    string? ConfirmedAddress,
    string? ConfirmedBirthDate,
    string? FailureMessage,
    DateTimeOffset? RecognizedAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? RejectedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    Guid CreatedByUserId,
    int Version);

/// <summary>创建身份证 OCR 任务请求。</summary>
public sealed record CreateOcrIdCardTaskRequest(
    Guid SourceFileId);

/// <summary>确认身份证 OCR 识别结果请求；确认值不自动写入身份权威档案。</summary>
public sealed record ConfirmOcrIdCardTaskRequest(
    string Name,
    string IdNumber,
    string? Gender,
    string? Nation,
    string? Address,
    string? BirthDate,
    int Version);
