using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.CrossModulePorts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowCrossModulePortRegistrationTests
{
    [TestMethod]
    public void WorkflowModule_registers_cross_module_ports_as_scoped()
    {
        var services = new ServiceCollection();
        new Full.NET.Modules.Workflow.WorkflowModule().AddServices(
            services,
            new ConfigurationBuilder().Build());

        Assert.IsNotNull(services.SingleOrDefault(
            item => item.ServiceType == typeof(IWorkflowPublishedDefinitionDirectory) &&
                    item.ImplementationType == typeof(WorkflowPublishedDefinitionDirectoryAdapter)));
        Assert.IsNotNull(services.SingleOrDefault(
            item => item.ServiceType == typeof(IWorkflowInstanceStarter) &&
                    item.ImplementationType == typeof(WorkflowInstanceStarterAdapter)));
        Assert.IsNotNull(services.SingleOrDefault(
            item => item.ServiceType == typeof(IWorkflowInstanceCanceller) &&
                    item.ImplementationType == typeof(WorkflowInstanceCancellerAdapter)));
    }
}
