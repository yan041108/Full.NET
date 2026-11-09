using Full.NET.Abstractions.Results;
using Full.NET.Modules.Document.Providers.OfficePreview;

namespace Full.NET.Host.Worker;

/// <summary>独立 Office Worker 使用受控转换器，任务领取、引用和文件写入使用正式实现。</summary>
internal static class OfficePreviewRecoveryFixture
{
    internal static bool TryRegister(IServiceCollection services, IConfiguration configuration,
        HashSet<ServiceDescriptor> existingHostedServices)
    {
        var mode = configuration["RecoveryFixture:Pause"];
        if (mode is not ("office-conversion" or "office-recovery")) return false;
        foreach (var descriptor in services.Where(item => item.ServiceType == typeof(IHostedService)
            && !existingHostedServices.Contains(item)
            && item.ImplementationType?.Name != "DocumentPreviewTaskHostedProcessor").ToArray())
            services.Remove(descriptor);
        var provider = services.Single(item => item.ServiceType == typeof(IDocumentOfficePreviewConversionProvider)
            && item.ImplementationType?.Name == "ExternalHttpDocumentOfficePreviewConversionProvider");
        services.Remove(provider);
        var marker = configuration["RecoveryFixture:GateFile"] ?? throw new InvalidOperationException("缺少 Office 转换标记。");
        services.AddSingleton<IDocumentOfficePreviewConversionProvider>(new Converter(marker, mode == "office-conversion"));
        return true;
    }

    private sealed class Converter(string marker, bool pause) : IDocumentOfficePreviewConversionProvider
    {
        public string ProviderKey => "external_http";

        public async Task<Result<DocumentOfficePreviewConversionOutput>> ConvertAsync(DocumentOfficePreviewConversionInput request,
            string workingDirectory, CancellationToken cancellationToken = default)
        {
            await File.AppendAllTextAsync(marker, request.SourceFileName + Environment.NewLine, cancellationToken);
            if (pause) await Task.Delay(Timeout.Infinite, cancellationToken);
            var pdf = "%PDF-1.7\nrecovery\n%%EOF"u8.ToArray();
            return Result<DocumentOfficePreviewConversionOutput>.Success(new(new MemoryStream(pdf, writable: false), pdf.Length));
        }
    }
}
