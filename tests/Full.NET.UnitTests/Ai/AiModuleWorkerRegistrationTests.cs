using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Tools;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Features.ManageAgentTools;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>Worker Profile 必须装配完整工具执行栈与运行绑定上下文。</summary>
[TestClass]
public sealed class AiModuleWorkerRegistrationTests
{
    [TestMethod]
    public void AddBackgroundServices_registers_agent_tool_execution_stack()
    {
        var services = new ServiceCollection();
        new AiModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());

        AssertService<IAgentRunExecutionContext, AgentRunExecutionContext>(services);
        AssertService<IAgentToolExecutor, AgentToolExecutor>(services);
        AssertService<IAgentApprovalPort, AiAgentApprovalPort>(services); // AiAgentApprovalPort is in Security namespace
        AssertService<IAgentToolRegistrySource, AgentToolRegistrySource>(services);
        AssertService<IToolAuthorizationPort, AiBackgroundToolAuthorizationPort>(services);
        AssertService<IToolAuditPort, AiBackgroundToolAuditPort>(services);
        AssertService<AiAgentRunCoordinator>(services);
    }

    private static void AssertService<TService, TImplementation>(IServiceCollection services) =>
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(TService)
            && descriptor.ImplementationType == typeof(TImplementation)
            && descriptor.Lifetime == ServiceLifetime.Scoped));

    private static void AssertService<TService>(IServiceCollection services) =>
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(TService)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
}
