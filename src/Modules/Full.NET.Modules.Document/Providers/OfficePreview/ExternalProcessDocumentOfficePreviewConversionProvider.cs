using System.Diagnostics;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Providers.OfficePreview;

/// <summary>在隔离临时目录中调用外部进程完成 Office→PDF 转换。</summary>
internal sealed class ExternalProcessDocumentOfficePreviewConversionProvider(
    IOptionsMonitor<DocumentOfficePreviewConversionOptions> options) : IDocumentOfficePreviewConversionProvider
{
    public string ProviderKey => DocumentOfficePreviewConversionProviderKeys.ExternalProcess;

    public async Task<Result<DocumentOfficePreviewConversionOutput>> ConvertAsync(
        DocumentOfficePreviewConversionInput request,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var processOptions = options.CurrentValue.ExternalProcess;
        if (string.IsNullOrWhiteSpace(processOptions.ExecutablePath))
        {
            return Result<DocumentOfficePreviewConversionOutput>.Failure(
                new Error(
                    DocumentErrorCodes.OfficePreviewProviderDisabled,
                    "External process office preview conversion executable is not configured.",
                    ErrorType.BusinessRule));
        }

        var inputPath = Path.Combine(workingDirectory, SanitizeFileName(request.SourceFileName));
        var outputPath = Path.Combine(workingDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".pdf");
        await using (var inputFile = File.Create(inputPath))
        {
            await request.SourceContent.CopyToAsync(inputFile, cancellationToken).ConfigureAwait(false);
        }

        var arguments = processOptions.ArgumentTemplate
            .Replace("{inputPath}", inputPath, StringComparison.Ordinal)
            .Replace("{outputPath}", outputPath, StringComparison.Ordinal);
        var startInfo = new ProcessStartInfo
        {
            FileName = processOptions.ExecutablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return Result<DocumentOfficePreviewConversionOutput>.Failure(
                new Error(
                    DocumentErrorCodes.OfficePreviewConversionFailed,
                    "External process office preview conversion could not start.",
                    ErrorType.BusinessRule));
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.TimeoutSeconds, 5, 3600)));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 进程可能已退出，忽略清理异常。
            }

            return Result<DocumentOfficePreviewConversionOutput>.Failure(
                new Error(
                    DocumentErrorCodes.OfficePreviewConversionFailed,
                    "External process office preview conversion timed out.",
                    ErrorType.BusinessRule));
        }

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            return Result<DocumentOfficePreviewConversionOutput>.Failure(
                new Error(
                    DocumentErrorCodes.OfficePreviewConversionFailed,
                    "External process office preview conversion returned a non-zero exit code.",
                    ErrorType.BusinessRule));
        }

        var pdfStream = File.OpenRead(outputPath);
        var length = pdfStream.Length;
        return Result<DocumentOfficePreviewConversionOutput>.Success(
            new DocumentOfficePreviewConversionOutput(pdfStream, length));
    }

    private static string SanitizeFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return "source.bin";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalid, '_');
        }

        return safeName;
    }
}
