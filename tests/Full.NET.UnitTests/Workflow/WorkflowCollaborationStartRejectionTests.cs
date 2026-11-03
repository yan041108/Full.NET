using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;

namespace Full.NET.UnitTests.Workflow;

/// <summary>固定输入或子树预算拒绝必须收敛；不能把暂态修订竞争当作永久失败。</summary>
[TestClass]
public sealed class WorkflowCollaborationStartRejectionTests
{
    [TestMethod]
    [DataRow(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation, true)]
    [DataRow(WorkflowErrorCodes.SubprocessActivationInvalid, ErrorType.Conflict, true)]
    [DataRow(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict, false)]
    [DataRow(WorkflowErrorCodes.InvalidTransition, ErrorType.Conflict, false)]
    public void Rejection_is_classified_by_the_closed_failure_boundary(string code, ErrorType type, bool closes) =>
        Assert.AreEqual(closes, WorkflowCollaborationRelay.ClosesRejectedStart(new(code, "refused", type)));
}
