using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Ocr.Domain;

/// <summary>身份证 OCR 源文件校验。</summary>
internal static class OcrSourceFileValidator
{
    /// <summary>校验 Files 描述信息是否可作为身份证 OCR 输入。</summary>
    public static bool IsSupportedImage(HostFileDescriptor descriptor) =>
        descriptor.SizeBytes > 0
        && descriptor.SizeBytes <= OcrIdCardPolicy.MaxImageBytes
        && OcrIdCardPolicy.AllowedContentTypePrefixes.Any(prefix =>
            descriptor.ContentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
