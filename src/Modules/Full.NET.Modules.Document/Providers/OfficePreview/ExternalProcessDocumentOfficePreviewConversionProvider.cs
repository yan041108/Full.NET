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

        // 文件名只由受控 MIME 决定；上传名称中的引号和路径不得成为转换器参数。
        var inputPath = Path.Combine(workingDirectory, DocumentPreviewProcessArguments.InputFileName(request.SourceMimeType));
        var outputPath = Path.Combine(workingDirectory, "output.pdf");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(snapshot.TimeoutSeconds, 5, 3600)));
        try
        {
            DocumentPreviewContentBudget.CheckDeclaredLength(request.SourceLength, snapshot.MaxInputBytes, DocumentErrorCodes.OfficePreviewInputTooLarge);
            using var input = new DocumentPreviewBudgetStream(request.SourceContent, snapshot.MaxInputBytes, DocumentErrorCodes.OfficePreviewInputTooLarge);
            await using (var inputFile = File.Create(inputPath))
                await input.CopyToAsync(inputFile, timeoutCts.Token).ConfigureAwait(false);
            var startInfo = new ProcessStartInfo
            {
                FileName = processOptions.ExecutablePath, WorkingDirectory = workingDirectory,
                RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            };
            foreach (var argument in DocumentPreviewProcessArguments.Build(processOptions.ArgumentTemplate, inputPath, outputPath))
                startInfo.ArgumentList.Add(argument);
            if (await DocumentPreviewProcessLifetime.RunAsync(startInfo, timeoutCts.Token).ConfigureAwait(false) != 0)
                return Failure(DocumentErrorCodes.OfficePreviewConversionFailed);
            var pdfStream = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            try
            {
                DocumentPreviewContentBudget.CheckDeclaredLength(pdfStream.Length, snapshot.MaxOutputBytes, DocumentErrorCodes.OfficePreviewOutputTooLarge);
                if (pdfStream.Length == 0) throw new InvalidDataException("Preview output is empty.");
                timeoutCts.Token.ThrowIfCancellationRequested();
                return Result<DocumentOfficePreviewConversionOutput>.Success(new(pdfStream, pdfStream.Length));
            }
            catch { await pdfStream.DisposeAsync().ConfigureAwait(false); throw; }
        }
        catch (DocumentPreviewContentLimitException exception) { return Failure(exception.Code); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Failure(DocumentErrorCodes.OfficePreviewConversionFailed); }
        catch (InvalidDataException) { return Failure(DocumentErrorCodes.OfficePreviewConversionFailed); }
        catch (FileNotFoundException) { return Failure(DocumentErrorCodes.OfficePreviewConversionFailed); }
    }
    private static Result<DocumentOfficePreviewConversionOutput> Failure(string code) =>
        Result<DocumentOfficePreviewConversionOutput>.Failure(new(code, "Office preview conversion failed or exceeded its configured budget.", ErrorType.BusinessRule));
}
