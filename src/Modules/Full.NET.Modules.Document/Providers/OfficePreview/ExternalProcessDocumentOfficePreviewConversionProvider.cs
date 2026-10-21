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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.TimeoutSeconds, 5, 3600)));
        var inputPath = Path.Combine(workingDirectory, SanitizeFileName(request.SourceFileName));
        var outputPath = Path.Combine(workingDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".pdf");
        await using (var inputFile = File.Create(inputPath))
        {
            await OfficePreviewInputBudget.CopyAsync(request.SourceContent, inputFile,
                Math.Min(options.CurrentValue.MaxInputBytes, options.CurrentValue.MaxTemporaryBytes), timeoutCts.Token).ConfigureAwait(false);
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

        // 不保留转换器原始输出，持续消费两个管道避免日志写满后子进程阻塞。
        var stdout = DrainAsync(process.StandardOutput);
        var stderr = DrainAsync(process.StandardError);
        using var monitorStop = new CancellationTokenSource();
        var diskBudgetExceeded = false;
        var diskMonitor = MonitorDirectoryAsync(workingDirectory, outputPath, options.CurrentValue,
            () => { diskBudgetExceeded = true; timeoutCts.Cancel(); }, monitorStop.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 进程可能已退出，忽略清理异常。
            }

            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Result<DocumentOfficePreviewConversionOutput>.Failure(
                new Error(
                    DocumentErrorCodes.OfficePreviewConversionFailed,
                    diskBudgetExceeded ? "External process office preview conversion exceeded its disk budget."
                        : "External process office preview conversion timed out.",
                    ErrorType.BusinessRule));
        }

        finally
        {
            await monitorStop.CancelAsync().ConfigureAwait(false);
            await diskMonitor.ConfigureAwait(false);
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
        if (length > options.CurrentValue.MaxOutputBytes)
        {
            await pdfStream.DisposeAsync().ConfigureAwait(false);
            return Result<DocumentOfficePreviewConversionOutput>.Failure(new Error(
                DocumentErrorCodes.OfficePreviewConversionFailed, "Converted PDF exceeds its byte budget.", ErrorType.BusinessRule));
        }
        return Result<DocumentOfficePreviewConversionOutput>.Success(
            new DocumentOfficePreviewConversionOutput(pdfStream, length));
    }

    private static async Task MonitorDirectoryAsync(string directory, string outputPath,
        DocumentOfficePreviewConversionOptions limits, Action exceeded, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            do
            {
                long bytes = 0;
                var entries = 0;
                foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
                { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++entries > 1024) { exceeded(); return; }
                    if (Directory.Exists(path)) continue;
                    var info = new FileInfo(path);
                    if (!info.Exists) continue;
                    bytes = checked(bytes + info.Length);
                    if (bytes > limits.MaxTemporaryBytes || (string.Equals(path, outputPath, StringComparison.OrdinalIgnoreCase)
                        && info.Length > limits.MaxOutputBytes)) { exceeded(); return; }
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { exceeded(); }
        catch (UnauthorizedAccessException) { exceeded(); }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false) != 0) { }
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
