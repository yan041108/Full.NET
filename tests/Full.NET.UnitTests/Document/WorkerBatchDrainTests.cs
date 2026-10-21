using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.PreviewTasks;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.Reporting.Configuration;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>三个任务消费者满批排空，空批及故障退避，并观察热配置变化。</summary>
[TestClass]
public sealed class WorkerBatchDrainTests
{
    [TestMethod]
    public void Workers_drain_full_batches_and_observe_updated_poll_and_batch_limits()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var document = Substitute.For<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
        var imports = Substitute.For<IOptionsMonitor<ImportExportOptions>>();
        var reporting = Substitute.For<IOptionsMonitor<ReportingExportOptions>>();
        document.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions { BatchSize = 3, PollSeconds = 15 });
        imports.CurrentValue.Returns(new ImportExportOptions { BatchSize = 3, PollSeconds = 15 });
        reporting.CurrentValue.Returns(new ReportingExportOptions { BatchSize = 3, PollSeconds = 15 });
        Func<int, TimeSpan>[] policies = [
            new DocumentPreviewTaskHostedProcessor(scopes, document, NullLogger<DocumentPreviewTaskHostedProcessor>.Instance).GetDelayAfterBatch,
            new ImportExportTaskHostedProcessor(scopes, imports, NullLogger<ImportExportTaskHostedProcessor>.Instance).GetDelayAfterBatch,
            new ReportingExportTaskHostedProcessor(scopes, reporting, NullLogger<ReportingExportTaskHostedProcessor>.Instance).GetDelayAfterBatch];
        foreach (var policy in policies)
        {
            Assert.AreEqual(TimeSpan.Zero, policy(3));
            Assert.AreEqual(TimeSpan.FromSeconds(15), policy(2));
            Assert.AreEqual(TimeSpan.FromSeconds(15), policy(0));
        }
        document.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions { BatchSize = 4, PollSeconds = 30 });
        imports.CurrentValue.Returns(new ImportExportOptions { BatchSize = 4, PollSeconds = 30 });
        reporting.CurrentValue.Returns(new ReportingExportOptions { BatchSize = 4, PollSeconds = 30 });
        foreach (var policy in policies)
        {
            Assert.AreEqual(TimeSpan.FromSeconds(30), policy(3));
            Assert.AreEqual(TimeSpan.Zero, policy(4));
        }
    }
}
