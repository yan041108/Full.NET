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
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = options.CurrentValue;
        var processOptions = snapshot.ExternalProcess;
        if (string.IsNullOrWhiteSpace(processOptions.ExecutablePath))
        {
            return Result<DocumentOfficePreviewConversionOutput>.Failure(
                new Error(
                    DocumentErrorCodes.OfficePreviewProviderDisabled,
                    "External process office preview conversion executable is not configured.",
                    ErrorType.BusinessRule));
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(snapshot.TimeoutSeconds, 5, 3600)));
        try
        {
            var inputBudget = Math.Min(snapshot.MaxInputBytes, snapshot.MaxTemporaryBytes);
            DocumentPreviewContentBudget.CheckDeclaredLength(request.SourceLength, inputBudget, DocumentErrorCodes.OfficePreviewInputTooLarge);
            workingDirectory = Path.GetFullPath(workingDirectory);
            var inputPath = Path.Combine(workingDirectory, DocumentPreviewProcessArguments.InputFileName(request.SourceMimeType));
            var outputPath = Path.Combine(workingDirectory, "output.pdf");
            using (var source = new DocumentPreviewBudgetStream(request.SourceContent, inputBudget, DocumentErrorCodes.OfficePreviewInputTooLarge))
            await using (var input = File.Create(inputPath))
                await source.CopyToAsync(input, deadline.Token).ConfigureAwait(false);

            var startInfo = new ProcessStartInfo
            {
                FileName = processOptions.ExecutablePath,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // 文件名与参数边界由模块控制，上传名称不能注入转换器选项。
            foreach (var argument in DocumentPreviewProcessArguments.Build(processOptions.ArgumentTemplate, inputPath, outputPath))
                startInfo.ArgumentList.Add(argument);

            var exitCode = await ConvertWithDiskBudgetAsync(startInfo, outputPath, snapshot, deadline.Token).ConfigureAwait(false);
            var output = new FileInfo(outputPath);
            if (exitCode != 0 || !output.Exists || output.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return Failure(DocumentErrorCodes.OfficePreviewConversionFailed, "External process did not produce a usable preview PDF.");
            return await OpenResultAsync(output, snapshot.MaxOutputBytes, deadline.Token).ConfigureAwait(false);
        }
        catch (DocumentPreviewContentLimitException limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(limit.Code, limit.Message);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(DocumentErrorCodes.OfficePreviewConversionFailed, "External process office preview conversion timed out.");
        }
    }

    private static async Task<Result<DocumentOfficePreviewConversionOutput>> OpenResultAsync(FileInfo output, long budget, CancellationToken token)
    {
        var stream = output.OpenRead();
        var transferred = false;
        try
        {
            token.ThrowIfCancellationRequested();
            var length = stream.Length;
            DocumentPreviewContentBudget.CheckDeclaredLength(length, budget, DocumentErrorCodes.OfficePreviewOutputTooLarge);
            if (length == 0)
                return Failure(DocumentErrorCodes.OfficePreviewConversionFailed, "External process produced an empty preview PDF.");
            transferred = true;
            return Result<DocumentOfficePreviewConversionOutput>.Success(new(stream, length));
        }
        finally
        {
            if (!transferred) await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<int> ConvertWithDiskBudgetAsync(ProcessStartInfo startInfo, string outputPath,
        DocumentOfficePreviewConversionOptions limits, CancellationToken token)
    {
        using var processCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var monitorStop = new CancellationTokenSource();
        var monitor = MonitorDirectoryAsync(startInfo.WorkingDirectory, outputPath, limits, processCancellation, monitorStop.Token);
        int exitCode;
        try
        {
            exitCode = await DocumentPreviewProcessLifetime.RunAsync(startInfo, processCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            await monitorStop.CancelAsync().ConfigureAwait(false);
            await monitor.ConfigureAwait(false);
        }
        // 转换器可能在两次采样之间写完并退出；最终验收必须再核对磁盘和 PDF 大小。
        CheckDirectoryBudget(startInfo.WorkingDirectory, outputPath, limits, token);
        token.ThrowIfCancellationRequested();
        return exitCode;
    }

    private static async Task MonitorDirectoryAsync(string directory, string outputPath,
        DocumentOfficePreviewConversionOptions limits, CancellationTokenSource processCancellation, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            do
            {
                CheckDirectoryBudget(directory, outputPath, limits, token);
            }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            await processCancellation.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void CheckDirectoryBudget(string directory, string outputPath,
        DocumentOfficePreviewConversionOptions limits, CancellationToken token)
    {
        long bytes = 0;
        var entries = 0;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", new EnumerationOptions
            { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
        {
            token.ThrowIfCancellationRequested();
            if (++entries > 1024)
                throw new DocumentPreviewContentLimitException(DocumentErrorCodes.OfficePreviewOutputTooLarge);
            if (entry is not FileInfo file || !file.Exists) continue;
            var length = file.Length;
            if (length > limits.MaxTemporaryBytes - bytes
                || (string.Equals(file.FullName, outputPath, comparison) && length > limits.MaxOutputBytes))
                throw new DocumentPreviewContentLimitException(DocumentErrorCodes.OfficePreviewOutputTooLarge);
            bytes += length;
        }
    }

    private static Result<DocumentOfficePreviewConversionOutput> Failure(string code, string message) =>
        Result<DocumentOfficePreviewConversionOutput>.Failure(new Error(code, message, ErrorType.BusinessRule));
}
