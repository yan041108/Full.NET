using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>绑定失败必须使外层根事务回滚；只有明确实际步骤可替换节点绑定。</summary>
[TestClass]
public sealed class WorkflowActivationBindingWriterTests
{
    [TestMethod]
    [DataRow("root")]
    [DataRow("epoch")]
    [DataRow("step")]
    [DataRow("scope")]
    public async Task Missing_trusted_identity_fails_before_writes(string missing)
    {
        var command=Substitute.For<ICommandExecutor>();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>WorkflowActivationBindingWriter.BindAsync(command,
            missing=="root"?Guid.Empty:Guid.CreateVersion7(),missing=="scope"?" ":"host",
            missing=="epoch"?Guid.Empty:Guid.CreateVersion7(),missing=="step"?Guid.Empty:Guid.CreateVersion7(),DateTimeOffset.UtcNow,default));
        Assert.AreEqual(0,command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task A_missing_owned_step_throws_after_replacement_attempt_so_the_transaction_cannot_commit()
    {
        var command=Substitute.For<ICommandExecutor>();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>WorkflowActivationBindingWriter.BindAsync(command,
            Guid.CreateVersion7(),"host",Guid.CreateVersion7(),Guid.CreateVersion7(),DateTimeOffset.UtcNow,default));
        var statements=command.ReceivedCalls().Select(call=>(SqlStatement)call.GetArguments()[0]!).ToArray();
        CollectionAssert.AreEqual(new[]{WorkflowActivationBindingSql.DeleteForStep,WorkflowActivationBindingSql.BindStep},statements);
    }
}
