using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>凭据末次复核在业务回调之后、提交之前；失败和异常沿正式事务边界回滚。</summary>
[TestClass]
public sealed class WorkflowTodoAuthorizedTransactionTests
{
    [TestMethod]
    [DataRow("success")][DataRow("revoked")][DataRow("changed_credential")]
    [DataRow("business_failure")][DataRow("authority_failure")][DataRow("cancel")]
    public async Task Transaction_commits_only_the_original_authorized_actor(string scenario)
    {
        var original = new WorkflowTodoActor(Guid.CreateVersion7(), null, Guid.CreateVersion7(), false);
        var authorization = Substitute.For<IWorkflowTodoActorAuthorization>();
        var transaction = new RecordingTransaction(); var reads = 0;
        using var cancellation = new CancellationTokenSource();
        var required = new[] { WorkflowPermissions.TodosTransfer, WorkflowPermissions.TodosSign };
        authorization.AuthorizeAsync(original.UserId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            reads++; transaction.Events.Add("authorize");
            CollectionAssert.AreEqual(new[] { WorkflowPermissions.TodosTransfer, WorkflowPermissions.TodosSign }, call.ArgAt<IReadOnlyList<string>>(1).ToArray());
            call.ArgAt<CancellationToken>(2).ThrowIfCancellationRequested();
            if (scenario == "authority_failure") throw new InvalidOperationException("authority failed");
            WorkflowTodoActor? actor = scenario == "revoked" ? null : scenario == "changed_credential"
                ? original with { CredentialId = Guid.CreateVersion7() } : original;
            return Task.FromResult(actor);
        });
        Task<Result<int>> Execute() => WorkflowTodoAuthorizedTransaction.ExecuteAsync(authorization, transaction, original, required, token =>
        {
            transaction.Events.Add("command");
            // 业务回调不能删掉提交复核原来要求的签名权限。
            required[1] = "changed";
            if (scenario == "cancel") cancellation.Cancel();
            return Task.FromResult(scenario == "business_failure"
                ? Result<int>.Failure(new("business.failed", "failed", ErrorType.Conflict)) : Result<int>.Success(42));
        }, cancellation.Token);
        if (scenario == "authority_failure") await Assert.ThrowsExactlyAsync<InvalidOperationException>(Execute);
        else if (scenario == "cancel") await Assert.ThrowsExactlyAsync<OperationCanceledException>(Execute);
        else
        {
            var result = await Execute();
            Assert.AreEqual(scenario == "success", result.IsSuccess);
            if (result.IsSuccess) Assert.AreEqual(42, result.Value);
            else Assert.AreEqual(scenario == "business_failure" ? "business.failed" : WorkflowErrorCodes.TodoForbidden, result.Error!.Code);
        }
        Assert.AreEqual(scenario == "business_failure" ? 0 : 1, reads);
        Assert.AreEqual(scenario == "success", transaction.Committed);
        Assert.AreEqual(scenario != "success", transaction.RolledBack);
        CollectionAssert.AreEqual(scenario == "business_failure" ? new[] { "command", "rollback" }
            : scenario == "success" ? ["command", "authorize", "commit"] : ["command", "authorize", "rollback"], transaction.Events);
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public List<string> Events { get; } = [];
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
        {
            try
            {
                var result = await action(token); Events.Add("commit"); Committed = true; return result;
            }
            catch { Events.Add("rollback"); RolledBack = true; throw; }
        }
    }
}
