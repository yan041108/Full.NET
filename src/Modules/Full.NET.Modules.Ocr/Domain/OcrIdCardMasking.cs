namespace Full.NET.Modules.Ocr.Domain;

/// <summary>身份证 OCR 响应脱敏；未确认前不返回完整证件号。</summary>
internal static class OcrIdCardMasking
{
    /// <summary>对证件号做展示脱敏。</summary>
    public static string? MaskIdNumber(string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber))
        {
            return idNumber;
        }

        var normalized = idNumber.Trim();
        if (normalized.Length <= 8)
        {
            return new string('*', normalized.Length);
        }

        return string.Concat(
            normalized.AsSpan(0, 3),
            new string('*', normalized.Length - 7),
            normalized.AsSpan(normalized.Length - 4));
    }
}
