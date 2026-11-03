using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Messaging;
using Full.NET.Agents.Tools;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.AI.Abstractions.Tools;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>正式后台入口必须自行闭合工具目录依赖，不能借 API 装配掩盖缺项。</summary>
[TestClass]
public sealed class AiWorkerToolRegistryCompositionTests
{
    [TestMethod]
    public async Task Worker_resolves_remote_definition_source_without_api_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton(Substitute.For<IQueryExecutor>());
        services.AddSingleton(Substitute.For<ICommandExecutor>());
        services.AddSingleton(Substitute.For<ICommandTransaction>());
        services.AddSingleton(Substitute.For<IDataTransactionState>());
        services.AddSingleton(Substitute.For<IOutboxWriter>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton(Substitute.For<IBackgroundSessionAuthorization>());
        services.AddSingleton(Substitute.For<IPersonalScheduleCompletion>());
        new AiModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        // 只验证构造闭包，不调用远端目录、读取令牌或启动网络。
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IRemoteAgentToolDefinitionSource>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IAgentToolRegistrySource>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IAgentToolExecutor>());
    }
}
