using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.PreviewTasks;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>只使用 Worker 入口装配，关闭转换仍要执行持久化引用及审计维护。</summary>
[TestClass]
public sealed class DocumentPreviewWorkerCompositionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Worker_only_registration_resolves_preview_runner_and_keeps_maintenance(bool enabled)
    {
        var services = new ServiceCollection(); services.AddLogging();
        IQueryExecutor queries = new EmptyQueries();
        var commands = Substitute.For<ICommandExecutor>();
        ICommandTransaction transaction = new InlineTransaction();
        var tenant = Substitute.For<ICurrentTenantContextWriter>();
        services.AddSingleton(queries); services.AddSingleton(commands); services.AddSingleton(transaction); services.AddSingleton(tenant);
        services.AddSingleton<IClock, SystemClock>(); services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        services.AddSingleton(Substitute.For<IOutboxWriter>());
        services.AddSingleton(Substitute.For<IHostFileContentReader>()); services.AddSingleton(Substitute.For<IHostFileUploadWriter>());
        services.AddSingleton(Substitute.For<IHostFileReferenceClaimService>());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Document:OfficePreviewConversion:Enabled"] = enabled.ToString(),
            ["Document:OfficePreviewConversion:ProviderKey"] = "external_http",
            ["Document:OfficePreviewConversion:ExternalHttp:BaseUrl"] = "https://example.test/convert"
        }).Build();
        new DocumentModule().AddBackgroundServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
        var worker = new DocumentPreviewTaskHostedProcessor(provider.GetRequiredService<IServiceScopeFactory>(), monitor, NullLogger<DocumentPreviewTaskHostedProcessor>.Instance);
        Assert.AreEqual(0, await worker.ProcessOnceAsync(CancellationToken.None));
        await commands.Received(1).ExecuteAsync(Arg.Is<SqlStatement>(sql => sql != null && sql.Name == "document.deletion_fact.clean.sqlserver"), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        tenant.Received(1).SetHost(); tenant.Received(1).Clear();
        Assert.AreEqual("external_http", provider.GetRequiredService<DocumentOfficePreviewConversionProviderResolver>().ResolveCurrent().ProviderKey);
    }
    /// <summary>使用显式空查询，不为内部投影类型创建运行时动态代理。</summary>
    private sealed class EmptyQueries : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
    }
    private sealed class InlineTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
    }

}
