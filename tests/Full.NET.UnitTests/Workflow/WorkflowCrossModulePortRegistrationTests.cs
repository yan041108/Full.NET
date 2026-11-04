using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.CrossModulePorts;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Workflow;

/// <summary>单元测试（工作流）：Workflow Cross Module Port Registration。</summary>
[TestClass]
public sealed class WorkflowCrossModulePortRegistrationTests
{
    [TestMethod]
    public void Background_registration_does_not_require_interactive_todo_credentials_or_upload_services()
    {
        var background=new ServiceCollection();new Full.NET.Modules.Workflow.WorkflowModule().AddBackgroundServices(background,new ConfigurationBuilder().Build());
        Assert.IsFalse(background.Any(service=>service.ServiceType==typeof(IWorkflowTodoActorAuthorization)||service.ServiceType==typeof(WorkflowTodoManagementService)||service.ServiceType==typeof(WorkflowFormAttachmentUploadService)));
        var api=new ServiceCollection();new Full.NET.Modules.Workflow.WorkflowModule().AddServices(api,new ConfigurationBuilder().Build());
        Assert.IsTrue(api.Any(service=>service.ServiceType==typeof(IWorkflowTodoActorAuthorization)));Assert.IsTrue(api.Any(service=>service.ServiceType==typeof(WorkflowTodoManagementService)));Assert.IsTrue(api.Any(service=>service.ServiceType==typeof(WorkflowFormAttachmentUploadService)));
        Assert.IsFalse(background.Any(service => service.ServiceType == typeof(WorkflowVoteTodoService)));
        Assert.IsTrue(background.Any(service => service.ServiceType == typeof(WorkflowVoteActionReader)));
        Assert.IsTrue(api.Any(service => service.ServiceType == typeof(WorkflowVoteTodoService)));
        foreach (var command in new[] { typeof(WorkflowTodoTransferService), typeof(WorkflowTodoDelegationService),
                     typeof(WorkflowTodoCountersignService), typeof(WorkflowClaimPoolService), typeof(WorkflowClaimReleaseService) })
        {
            Assert.IsFalse(background.Any(service => service.ServiceType == command));
            Assert.IsTrue(api.Any(service => service.ServiceType == command));
        }
    }
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
        Assert.IsNotNull(services.SingleOrDefault(item => item.ServiceType == typeof(IWorkflowBusinessStartProofDirectory) &&
            item.ImplementationType == typeof(WorkflowBusinessStartProofDirectoryAdapter) && item.Lifetime == ServiceLifetime.Scoped));
    }
}
