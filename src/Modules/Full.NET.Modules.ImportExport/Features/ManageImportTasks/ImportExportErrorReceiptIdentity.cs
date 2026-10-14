using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>按失败结果识别可恢复回执，存储身份与对外下载名分开。</summary>
internal static class ImportExportErrorReceiptIdentity
{
    /// <summary>源生成 JSON 保持结果摘要稳定，不使用含 ZIP 时间戳的工作簿字节作为身份。</summary>
    public static string BuildStorageFileName(string? sourceFileName, IReadOnlyList<StaticImportRowExecutionResult> failedRows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failedRows);
        ImportExportErrorReceiptBudget.Validate(failedRows, cancellationToken);
        var identity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(ImportExportTaskMapper.SerializeExecutionRows(failedRows))));
        cancellationToken.ThrowIfCancellationRequested();
        return BuildFileName(sourceFileName, $"-errors-{identity}.xlsx");
    }

    /// <summary>保持原下载命名约定，不把内部恢复摘要暴露到用户文件名。</summary>
    public static string BuildDownloadFileName(string? sourceFileName) => BuildFileName(sourceFileName, "-errors.xlsx");

    /// <summary>扩展后名称不超过 Files 的 255 字符上限，截断时保留完整 UTF-16 代理对。</summary>
    private static string BuildFileName(string? sourceFileName, string suffix)
    {
        // Files Port 会去除上传名首尾空格；先统一基名，确保恢复匹配与实际存储身份一致。
        var baseName = Path.GetFileNameWithoutExtension((sourceFileName ?? string.Empty).Replace('\\', '/')).Trim();
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "import-task";
        var maximum = 255 - suffix.Length;
        if (baseName.Length > maximum)
        {
            if (char.IsHighSurrogate(baseName[maximum - 1])) maximum--;
            baseName = baseName[..maximum];
        }
        return baseName + suffix;
    }
}
