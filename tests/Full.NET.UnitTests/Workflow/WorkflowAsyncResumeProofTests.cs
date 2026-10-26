using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.UnitTests.Workflow;

/// <summary>消费者只能使用原屏障的唯一工作项证明，不能复用其他实例、分支或旧输出。</summary>
[TestClass]
public sealed class WorkflowAsyncResumeProofTests
{
    [TestMethod]
    public void Only_the_original_released_work_and_exact_input_are_accepted()
    {
        var work=Work();var proof=Proof(work);
        Assert.IsTrue(WorkflowActivityWorkProcessor.MatchesResumeProof(work,proof));
        Assert.IsFalse(WorkflowActivityWorkProcessor.MatchesResumeProof(work,null));
        Assert.IsFalse(WorkflowActivityWorkProcessor.MatchesResumeProof(work with{ResumeBarrierId=null},proof));
        foreach(var altered in new[]{proof with{ResumeWorkId=Guid.NewGuid()},proof with{InstanceId=Guid.NewGuid()},proof with{TenantScopeKey="tenant:"+Guid.NewGuid().ToString("N")},
            proof with{NodeKey="other"},proof with{StatusKey="pending"},proof with{OutputHash=new string('a',64)},proof with{OutputFormRevision=2},
            proof with{ParallelJoinId=Guid.NewGuid()},proof with{ParallelBranchKey="another"}})
            Assert.IsFalse(WorkflowActivityWorkProcessor.MatchesResumeProof(work,altered));
    }
    [TestMethod]
    public void A_branch_resume_cannot_be_replayed_as_a_root_resume()
    {
        var work=Work() with{ParallelJoinId=Guid.NewGuid(),ParallelBranchKey="original"};var proof=Proof(work);
        Assert.IsTrue(WorkflowActivityWorkProcessor.MatchesResumeProof(work,proof));
        Assert.IsFalse(WorkflowActivityWorkProcessor.MatchesResumeProof(work with{ParallelJoinId=null,ParallelBranchKey=null},proof));
        Assert.IsFalse(WorkflowActivityWorkProcessor.MatchesResumeProof(work with{InputFormRevision=2},proof));
    }
    private static WorkflowActivityWorkRecord Work()=>new(Guid.NewGuid(),Guid.NewGuid(),"host","consumer","{}",
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("{}"))),1,1,null,null,1,1,Guid.NewGuid());
    private static WorkflowAsyncBarrierRecord Proof(WorkflowActivityWorkRecord work)=>new(work.ResumeBarrierId!.Value,work.InstanceId,work.TenantScopeKey,work.NodeKey,
        "[]",new string('b',64),"{}",work.InputHash,1,work.ParallelJoinId,work.ParallelBranchKey,"released",1,null,null,0,2,work.Id,work.InputJson,work.InputHash,
        work.InputFormRevision,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null);
}
