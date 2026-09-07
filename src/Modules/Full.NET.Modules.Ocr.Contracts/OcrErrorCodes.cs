namespace Full.NET.Modules.Ocr.Contracts;

/// <summary>OCR 模块稳定业务错误码。</summary>
public static class OcrErrorCodes
{
    /// <summary>Provider 配置不存在。</summary>
    public const string ProviderNotFound = "ocr.provider.not_found";

    /// <summary>Provider 配置无效或未启用。</summary>
    public const string ProviderInvalid = "ocr.provider.invalid";

    /// <summary>Provider 测试失败。</summary>
    public const string ProviderTestFailed = "ocr.provider.test_failed";

    /// <summary>源文件无效或不可用。</summary>
    public const string SourceFileInvalid = "ocr.source_file.invalid";

    /// <summary>身份证 OCR 任务不存在。</summary>
    public const string IdCardTaskNotFound = "ocr.id_card_task.not_found";

    /// <summary>身份证 OCR 任务状态不允许当前操作。</summary>
    public const string IdCardTaskStateInvalid = "ocr.id_card_task.state_invalid";

    /// <summary>身份证 OCR 识别失败。</summary>
    public const string IdCardRecognitionFailed = "ocr.id_card.recognition_failed";

    /// <summary>身份证 OCR 确认载荷无效。</summary>
    public const string IdCardConfirmInvalid = "ocr.id_card.confirm_invalid";

    /// <summary>OCR 远程调用失败。</summary>
    public const string RemoteCallFailed = "ocr.remote.call_failed";

    /// <summary>OCR 远程调用结果未知，本地任务意图已提交且不得当作失败回滚。</summary>
    public const string RemoteCallUnknown = "ocr.remote.call_unknown";
}
