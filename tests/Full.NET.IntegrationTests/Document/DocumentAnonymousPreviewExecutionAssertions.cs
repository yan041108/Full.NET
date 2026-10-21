using System.Net;
using System.Net.Http.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.PreviewTasks;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Document;

/// <summary>仅替换外部转换器，真实领取、文件上传与匿名内容读取均沿生产双库路径执行。</summary>
internal static class DocumentAnonymousPreviewExecutionAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, HttpClient client,
        HostDocumentItemResponse document, Guid taskId, string statusPath,
        AccessHostDocumentShareRequest grant, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var monitor = new FixedMonitor(new DocumentOfficePreviewConversionOptions
            { Enabled = true, BatchSize = 1, ProviderKey = "test" });
        var query = services.GetRequiredService<IQueryExecutor>();
        var runner = new DocumentPreviewTaskRunner(query, services.GetRequiredService<ICommandExecutor>(),
            services.GetRequiredService<ICommandTransaction>(), services.GetRequiredService<IHostFileContentReader>(),
            services.GetRequiredService<IHostFileUploadWriter>(),
            new DocumentOfficePreviewConversionProviderResolver([new PdfConverter()], monitor),
            services.GetRequiredService<IClock>(), services.GetRequiredService<IOptions<DatabaseOptions>>(), monitor);
        Assert.AreEqual(1, await runner.ProcessPendingAsync(cancellationToken));
        var task = await query.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.FindById,
            new Dictionary<string, object?> { ["Id"] = taskId }, cancellationToken);
        Assert.IsNotNull(task);
        Assert.AreEqual(HostDocumentPreviewTaskStatusKeys.Succeeded, task.StatusKey, task.ErrorCode);
        Assert.IsNull(task.RequestedByUserId);
        Assert.IsNotNull(task.OutputFileId);
        var owner = await query.QuerySingleOrDefaultAsync<Guid>(new SqlStatement("test.document_preview.output_owner",
            "SELECT CreatedByUserId FROM fn_files_file WHERE TenantId IS NULL AND Id=@Id", SqlDataScope.HostOnly),
            new Dictionary<string, object?> { ["Id"] = task.OutputFileId }, cancellationToken);
        Assert.AreEqual(document.CreatedByUserId, owner, "匿名派生文件继承文档归属，不能伪造发起用户。");
        using var response = await client.PostAsJsonAsync(statusPath + "/content", grant, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        CollectionAssert.AreEqual(PdfConverter.Bytes, await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private sealed class PdfConverter : IDocumentOfficePreviewConversionProvider
    {
        internal static readonly byte[] Bytes = "%PDF-1.7\nanonymous-preview\n%%EOF"u8.ToArray();
        public string ProviderKey => "test";
        public Task<Result<DocumentOfficePreviewConversionOutput>> ConvertAsync(DocumentOfficePreviewConversionInput request,
            string workingDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<DocumentOfficePreviewConversionOutput>.Success(new(new MemoryStream(Bytes, writable: false), Bytes.Length)));
    }

    private sealed class FixedMonitor(DocumentOfficePreviewConversionOptions value) : IOptionsMonitor<DocumentOfficePreviewConversionOptions>
    {
        public DocumentOfficePreviewConversionOptions CurrentValue => value;
        public DocumentOfficePreviewConversionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<DocumentOfficePreviewConversionOptions, string?> listener) => null;
    }
}
