namespace Full.NET.Modules.Ocr.Contracts;

/// <summary>OCR Provider 配置响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 ProviderKey、LastTestStatusKey 为稳定机器码；API Key 不出现在响应中。</remarks>
/// <param name="Id">Provider 配置标识（UUID v7）。</param>
/// <param name="ProviderKey">Provider 类型稳定机器码，决定调用适配器。</param>
/// <param name="Name">配置展示名。</param>
/// <param name="BaseUrl">Provider 服务基础 URL。</param>
/// <param name="HasApiKey">是否已配置 API Key；不暴露 Key 本身。</param>
/// <param name="IsEnabled">配置是否启用。</param>
/// <param name="LastTestedAtUtc">最近连接测试时间（UTC）；未测试为 <see langword="null"/>。</param>
/// <param name="LastTestStatusKey">最近测试结果稳定状态键；未测试为 <see langword="null"/>。</param>
/// <param name="LastTestMessage">最近测试附加说明；未测试为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；未更新为 <see langword="null"/>。</param>
/// <param name="Version">乐观并发版本号，用于 CAS 守卫。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 ApiKey 为 <see langword="null"/> 时表示保持原值。</remarks>
/// <param name="Name">配置展示名。</param>
/// <param name="BaseUrl">Provider 服务基础 URL。</param>
/// <param name="ApiKey">新 API Key；为 <see langword="null"/> 时保持原 Key。</param>
/// <param name="IsEnabled">配置是否启用。</param>
/// <param name="Version">期望的当前版本号，不匹配时返回 CAS 冲突错误。</param>
public sealed record UpdateOcrProviderConfigRequest(
    string Name,
    string BaseUrl,
    string? ApiKey,
    bool IsEnabled,
    int Version);

/// <summary>OCR Provider 测试结果。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Succeeded">连接测试是否成功。</param>
/// <param name="Message">测试结果附加说明；成功时可能为空字符串。</param>
public sealed record TestOcrProviderConfigResult(
    bool Succeeded,
    string Message);

/// <summary>身份证 OCR 任务响应；识别字段在未确认前可能脱敏。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 StatusKey 为稳定机器码；Recognized* 字段未脱敏前不得外发，确认值不自动写入身份权威档案。</remarks>
/// <param name="Id">OCR 任务标识（UUID v7）。</param>
/// <param name="SourceFileId">源图片在文件域的标识。</param>
/// <param name="StatusKey">任务当前状态稳定机器码。</param>
/// <param name="RecognizedName">识别姓名；未识别或脱敏时为 <see langword="null"/>。</param>
/// <param name="RecognizedIdNumber">识别身份证号；未识别或脱敏时为 <see langword="null"/>。</param>
/// <param name="RecognizedGender">识别性别；未识别或脱敏时为 <see langword="null"/>。</param>
/// <param name="RecognizedNation">识别民族；未识别或脱敏时为 <see langword="null"/>。</param>
/// <param name="RecognizedAddress">识别地址；未识别或脱敏时为 <see langword="null"/>。</param>
/// <param name="RecognizedBirthDate">识别出生日期文本；未识别或脱敏时为 <see langword="null"/>。</param>
/// <param name="ConfirmedName">用户确认姓名；未确认为 <see langword="null"/>。</param>
/// <param name="ConfirmedIdNumber">用户确认身份证号；未确认为 <see langword="null"/>。</param>
/// <param name="ConfirmedGender">用户确认性别；未确认为 <see langword="null"/>。</param>
/// <param name="ConfirmedNation">用户确认民族；未确认为 <see langword="null"/>。</param>
/// <param name="ConfirmedAddress">用户确认地址；未确认为 <see langword="null"/>。</param>
/// <param name="ConfirmedBirthDate">用户确认出生日期文本；未确认为 <see langword="null"/>。</param>
/// <param name="FailureMessage">任务级失败可读说明；无错误为 <see langword="null"/>。</param>
/// <param name="RecognizedAtUtc">识别完成时间（UTC）；未识别为 <see langword="null"/>。</param>
/// <param name="ConfirmedAtUtc">用户确认时间（UTC）；未确认为 <see langword="null"/>。</param>
/// <param name="RejectedAtUtc">用户驳回时间（UTC）；未驳回为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">任务创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；未更新为 <see langword="null"/>。</param>
/// <param name="CreatedByUserId">发起任务的用户标识。</param>
/// <param name="Version">乐观并发版本号，用于 CAS 守卫。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="SourceFileId">源图片在文件域的标识。</param>
public sealed record CreateOcrIdCardTaskRequest(
    Guid SourceFileId);

/// <summary>确认身份证 OCR 识别结果请求；确认值不自动写入身份权威档案。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。确认后任务进入终态，再次修改需走新的 OCR 任务。</remarks>
/// <param name="Name">用户确认姓名。</param>
/// <param name="IdNumber">用户确认身份证号。</param>
/// <param name="Gender">用户确认性别；缺省时不更新。</param>
/// <param name="Nation">用户确认民族；缺省时不更新。</param>
/// <param name="Address">用户确认地址；缺省时不更新。</param>
/// <param name="BirthDate">用户确认出生日期文本；缺省时不更新。</param>
/// <param name="Version">期望的当前版本号，不匹配时返回 CAS 冲突错误。</param>
public sealed record ConfirmOcrIdCardTaskRequest(
    string Name,
    string IdNumber,
    string? Gender,
    string? Nation,
    string? Address,
    string? BirthDate,
    int Version);
