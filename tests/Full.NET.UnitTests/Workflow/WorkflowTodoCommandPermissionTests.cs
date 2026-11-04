using System.Reflection;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>各本人办理入口必须在目录、文件或数据库准备前要求当前精确权限。</summary>
[TestClass]
public sealed class WorkflowTodoCommandPermissionTests
{
    [TestMethod]
    [DataRow("transfer")][DataRow("delegate")][DataRow("recall")][DataRow("countersign")][DataRow("cancel_countersign")]
    [DataRow("claim")][DataRow("assign")][DataRow("release")][DataRow("transfer_signed")][DataRow("release_signed")]
    public async Task Revoked_actor_is_denied_before_business_preparation(string action)
    {
        var actor = Guid.CreateVersion7(); var target = Guid.CreateVersion7(); var resource = Guid.CreateVersion7();
        var authorization = Substitute.For<IWorkflowTodoActorAuthorization>();
        string[]? required = null;
        authorization.AuthorizeAsync(actor, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
        { required = call.ArgAt<IReadOnlyList<string>>(1).ToArray(); return Task.FromResult<WorkflowTodoActor?>(null); });
        var signature = action.EndsWith("_signed", StringComparison.Ordinal) ? (Guid?)Guid.CreateVersion7() : null;
        var result = await (action switch
        {
            "transfer" or "transfer_signed" => Create<WorkflowTodoTransferService>(authorization).TransferAsync(resource, actor, new(target, 1, 1, null, "permission", signature)),
            "delegate" => Create<WorkflowTodoTransferService>(authorization).DelegateAsync(resource, actor, new(target, 1, 1, null, "permission")),
            "recall" => Create<WorkflowTodoDelegationService>(authorization).RecallAsync(resource, actor, new(1, 1, null, "permission"), default),
            "countersign" => Create<WorkflowTodoCountersignService>(authorization).CountersignAsync(resource, actor, new("before", [target], 1, null, "permission")),
            "cancel_countersign" => Create<WorkflowTodoCountersignService>(authorization).CancelAsync(resource, actor, new(1, null, "permission")),
            "claim" => Create<WorkflowClaimPoolService>(authorization).ClaimAsync(resource, actor, new(1, "permission"), default),
            "assign" => Create<WorkflowClaimPoolService>(authorization).AssignAsync(resource, actor, new(1, target, "permission"), default),
            "release" or "release_signed" => Create<WorkflowClaimReleaseService>(authorization).ReleaseAsync(resource, actor, new(1, 1, 1, "permission", SignatureFileId: signature), default),
            _ => throw new InvalidOperationException(action),
        });
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(WorkflowErrorCodes.TodoForbidden, result.Error!.Code);
        var permission = action switch
        {
            "transfer" or "transfer_signed" => WorkflowPermissions.TodosTransfer,
            "delegate" => WorkflowPermissions.TodosDelegate,
            "recall" => WorkflowPermissions.TodosRecallDelegation,
            "countersign" or "cancel_countersign" => WorkflowPermissions.TodosCountersign,
            "claim" => WorkflowPermissions.ClaimPoolsClaim,
            "assign" => WorkflowPermissions.ClaimPoolsAssign,
            _ => WorkflowPermissions.TodosRelease,
        };
        CollectionAssert.AreEqual(signature.HasValue ? new[] { permission, WorkflowPermissions.TodosSign } : [permission], required);
        await authorization.Received(1).AuthorizeAsync(actor, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>故意不提供业务依赖；撤权必须在接触它们前返回，反射仅用于测试装配。</summary>
    private static T Create<T>(IWorkflowTodoActorAuthorization authorization)
    {
        var constructor = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        return (T)constructor.Invoke(constructor.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(IWorkflowTodoActorAuthorization) ? (object)authorization : null).ToArray());
    }
}
