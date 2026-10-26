using Full.NET.Modules.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.ArchitectureTests;

/// <summary>锁定 Recovery Worker HostedService 只在 Worker 后台入口注册。</summary>
[TestClass]
public sealed class WorkflowRecoveryBoundaryTests
{
    [TestMethod]
    public void Connector_dispatch_loop_is_registered_once_only_for_worker()
    {
        var module=new WorkflowModule(); var configuration=new ConfigurationBuilder().Build();
        var api=new ServiceCollection(); module.AddServices(api,configuration);
        var worker=new ServiceCollection(); module.AddBackgroundServices(worker,configuration);
        static bool IsConnector(ServiceDescriptor descriptor)=>descriptor.ServiceType==typeof(IHostedService) &&
            descriptor.ImplementationType?.FullName=="Full.NET.Modules.Workflow.Execution.Connectors.WorkflowConnectorHostedProcessor";
        Assert.IsFalse(api.Any(IsConnector)); Assert.AreEqual(1,worker.Count(IsConnector));
        static bool IsBarrier(ServiceDescriptor descriptor)=>descriptor.ServiceType==typeof(IHostedService) &&
            descriptor.ImplementationType?.FullName=="Full.NET.Modules.Workflow.Execution.WorkflowAsyncBarrierHostedProcessor";
        Assert.IsFalse(api.Any(IsBarrier)); Assert.AreEqual(1,worker.Count(IsBarrier));
        static bool IsSla(ServiceDescriptor descriptor) => descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType?.FullName == "Full.NET.Modules.Workflow.Execution.WorkflowSlaHostedProcessor";
        Assert.IsFalse(api.Any(IsSla)); Assert.AreEqual(1, worker.Count(IsSla));
        static bool IsSubprocess(ServiceDescriptor descriptor) => descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType?.FullName == "Full.NET.Modules.Workflow.Execution.WorkflowSubprocessHostedProcessor";
        Assert.IsFalse(api.Any(IsSubprocess)); Assert.AreEqual(1, worker.Count(IsSubprocess));

    }

    /// <summary>API AddServices 禁止启动领取循环，Worker AddBackgroundServices 必须注册 HostedService。</summary>
    [TestMethod]
    public void Recovery_hosted_processor_is_registered_only_on_worker_background_entry()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Workflow",
            "WorkflowModule.cs"));
        var addServicesIndex = moduleSource.IndexOf("public void AddServices", StringComparison.Ordinal);
        var addBackgroundIndex = moduleSource.IndexOf(
            "public void AddBackgroundServices",
            StringComparison.Ordinal);
        Assert.IsTrue(addServicesIndex >= 0 && addBackgroundIndex > addServicesIndex);
        var addServicesBody = moduleSource[addServicesIndex..addBackgroundIndex];

        Assert.IsFalse(
            addServicesBody.Contains("AddHostedService", StringComparison.Ordinal),
            "API AddServices 禁止启动 Recovery HostedService。");
        Assert.IsFalse(
            addServicesBody.Contains("AddBackgroundServices(", StringComparison.Ordinal),
            "API AddServices 不得再调用 AddBackgroundServices，以免把领取循环带进 API。");
        StringAssert.Contains(moduleSource, "AddHostedService<WorkflowRecoveryHostedProcessor>");
    }
}
