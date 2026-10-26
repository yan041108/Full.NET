#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>Workflow Native AOT 行物化器；读取顺序必须与 WorkflowSql 的显式投影保持一致。</summary>
internal sealed class WorkflowDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    /// <summary>注册 Workflow 所有显式 SQL 投影对应的 Native AOT 行物化器。</summary>
    /// <param name="registrar">应用级 Dapper AOT 物化器注册表。</param>
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<WorkflowSlaRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 5), reader.IsDBNull(6) ? null : reader.GetInt64(6), AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            reader.GetString(8), reader.GetInt64(9), AotDataReaderExtensions.ReadDateTimeOffset(reader, 10), AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            reader.GetString(12), reader.GetString(13), reader.GetInt64(14), AotDataReaderExtensions.ReadNullableGuid(reader, 15),
            AotDataReaderExtensions.ReadNullableString(reader, 16), reader.GetString(17)));
        registrar.Register<WorkflowWorkingDelayRecord>(reader=>new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),AotDataReaderExtensions.ReadDateTimeOffset(reader,3),
            reader.IsDBNull(4)?null:reader.GetInt64(4),AotDataReaderExtensions.ReadNullableDateTimeOffset(reader,5),reader.GetInt64(6),AotDataReaderExtensions.ReadNullableGuid(reader,7),
            AotDataReaderExtensions.ReadNullableString(reader,8),AotDataReaderExtensions.ReadNullableString(reader,9),reader.GetString(10)));
        registrar.Register<WorkflowCalendarRecord>(reader=>new(reader.GetGuid(0),AotDataReaderExtensions.ReadNullableGuid(reader,1),reader.GetString(2),reader.GetString(3),
            reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetInt64(7),reader.IsDBNull(8)?null:reader.GetInt32(8),reader.GetGuid(9),reader.GetGuid(10),
            AotDataReaderExtensions.ReadDateTimeOffset(reader,11),AotDataReaderExtensions.ReadDateTimeOffset(reader,12),AotDataReaderExtensions.ReadNullableGuid(reader,13)));
        registrar.Register<WorkflowCalendarSummaryRecord>(reader=>new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetInt64(3),reader.IsDBNull(4)?null:reader.GetInt32(4),
            AotDataReaderExtensions.ReadNullableGuid(reader,5),AotDataReaderExtensions.ReadNullableString(reader,6),AotDataReaderExtensions.ReadDateTimeOffset(reader,7),AotDataReaderExtensions.ReadDateTimeOffset(reader,8)));
        registrar.Register<WorkflowCalendarVersionRecord>(reader=>new(reader.GetGuid(0),reader.GetGuid(1),reader.GetInt32(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetGuid(6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader,7),reader.GetString(8)));
        registrar.Register<WorkflowCalendarVersionSummaryRecord>(reader=>new(reader.GetGuid(0),reader.GetGuid(1),reader.GetInt32(2),reader.GetString(3),reader.GetString(4),AotDataReaderExtensions.ReadDateTimeOffset(reader,5)));
        registrar.Register<WorkflowDefinitionRecord>(ReadDefinition);
        registrar.Register<WorkflowStarterSelectionRecord>(reader => new(reader.GetString(0), reader.GetString(1)));
        registrar.Register<WorkflowPreviousActorRecord>(reader => new(reader.GetString(0), reader.GetGuid(1), reader.GetGuid(2)));
        registrar.Register<WorkflowAssigneeSnapshotRecord>(reader => new(reader.GetString(0), reader.GetString(1)));
        registrar.Register<WorkflowManualReminderTargetRecord>(reader => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
            reader.GetGuid(4), reader.GetGuid(5), reader.GetInt64(6)));
        registrar.Register<WorkflowManualReminderReceiptRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt64(3),
            reader.GetInt64(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7), AotDataReaderExtensions.ReadDateTimeOffset(reader, 8)));
        registrar.Register<WorkflowManualReminderBudgetRecord>(reader => new(reader.GetInt32(0), AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 1)));
        registrar.Register<Domain.WorkflowWithdrawalActivityBoundary>(reader => new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        registrar.Register<WorkflowClaimPoolRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt64(3),
            reader.GetString(4), reader.GetInt64(5), reader.GetInt32(6), reader.GetBoolean(7),
            AotDataReaderExtensions.ReadNullableGuid(reader, 8), AotDataReaderExtensions.ReadNullableGuid(reader, 9),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 10), AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12), AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
            reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16), AotDataReaderExtensions.ReadNullableGuid(reader, 17),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 18), AotDataReaderExtensions.ReadNullableGuid(reader, 19),
            AotDataReaderExtensions.ReadNullableString(reader, 20)));
        registrar.Register<WorkflowClaimCandidateRecord>(reader => new(reader.GetGuid(0), reader.GetInt32(1)));
        registrar.Register<WorkflowClaimPoolListRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), AotDataReaderExtensions.ReadNullableString(reader, 6), reader.GetInt64(7), reader.GetInt32(8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9), AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10)));
        registrar.Register<WorkflowClaimAttemptRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
            reader.GetGuid(4), reader.GetGuid(5), reader.GetGuid(6), reader.GetString(7), reader.GetInt32(8), reader.GetString(9), reader.GetInt64(10)));
        registrar.Register<WorkflowClaimTodoScheduleRecord>(reader => new(AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 0),
            reader.GetInt32(1), AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 2)));
        registrar.Register<WorkflowVoteFormBindingRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        registrar.Register<WorkflowVoteFormCandidateRecord>(reader => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        registrar.Register<WorkflowVoteRoundRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetBoolean(6),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7), reader.GetString(8), reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetInt32(12), reader.IsDBNull(13) ? null : reader.GetInt32(13)));
        registrar.Register<WorkflowVoteBallotRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
            reader.GetString(4), AotDataReaderExtensions.ReadNullableString(reader, 5), reader.GetString(6), reader.GetString(7), reader.GetInt64(8)));
        registrar.Register<WorkflowVoteCountsRecord>(reader => new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4)));
        registrar.Register<WorkflowDueVoteRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
        registrar.Register<WorkflowVoteDeadlineMemberRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1)));
        registrar.Register<WorkflowVoteAttachmentViewRecord>(reader => new(AotDataReaderExtensions.ReadNullableGuid(reader, 0), reader.GetString(1), reader.GetString(2)));
        registrar.Register<WorkflowBusinessCommandRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), AotDataReaderExtensions.ReadInt32(reader, 6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
            reader.GetString(10), AotDataReaderExtensions.ReadBoolean(reader, 11), AotDataReaderExtensions.ReadInt32(reader, 12),
            AotDataReaderExtensions.ReadNullableString(reader, 13), reader.IsDBNull(14) ? null : AotDataReaderExtensions.ReadInt64(reader, 14),
            reader.IsDBNull(15) ? null : AotDataReaderExtensions.ReadInt64(reader, 15), AotDataReaderExtensions.ReadNullableString(reader, 16),
            AotDataReaderExtensions.ReadNullableString(reader, 17), AotDataReaderExtensions.ReadInt64(reader, 18),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 19), AotDataReaderExtensions.ReadDateTimeOffset(reader, 20)));
        registrar.Register<WorkflowSubprocessCallRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetGuid(5), reader.GetGuid(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
            AotDataReaderExtensions.ReadInt64(reader, 11), AotDataReaderExtensions.ReadNullableGuid(reader, 12), AotDataReaderExtensions.ReadNullableString(reader, 13),
            AotDataReaderExtensions.ReadInt32(reader, 14), AotDataReaderExtensions.ReadInt32(reader, 15), reader.GetString(16),
            AotDataReaderExtensions.ReadBoolean(reader, 17), AotDataReaderExtensions.ReadNullableString(reader, 18), AotDataReaderExtensions.ReadInt64(reader, 19),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 20), AotDataReaderExtensions.ReadDateTimeOffset(reader, 21)));
        registrar.Register<WorkflowSubprocessItemRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
            AotDataReaderExtensions.ReadInt32(reader, 5), reader.GetGuid(6), reader.GetString(7), reader.GetString(8), AotDataReaderExtensions.ReadNullableGuid(reader, 9),
            reader.GetString(10), AotDataReaderExtensions.ReadNullableString(reader, 11), AotDataReaderExtensions.ReadNullableString(reader, 12),
            reader.IsDBNull(13) ? null : AotDataReaderExtensions.ReadInt64(reader, 13), AotDataReaderExtensions.ReadInt64(reader, 14),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 15), AotDataReaderExtensions.ReadDateTimeOffset(reader, 16)));
        registrar.Register<WorkflowMultiInstanceSetRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            AotDataReaderExtensions.ReadInt32(reader, 5), AotDataReaderExtensions.ReadInt32(reader, 6),
            reader.IsDBNull(7) ? null : AotDataReaderExtensions.ReadInt32(reader, 7), reader.IsDBNull(8) ? null : AotDataReaderExtensions.ReadInt32(reader, 8),
            AotDataReaderExtensions.ReadNullableString(reader, 9), AotDataReaderExtensions.ReadNullableString(reader, 10),
            AotDataReaderExtensions.ReadNullableString(reader, 11), AotDataReaderExtensions.ReadNullableString(reader, 12), AotDataReaderExtensions.ReadNullableString(reader, 13),
            reader.GetString(14), AotDataReaderExtensions.ReadInt64(reader, 15), AotDataReaderExtensions.ReadDateTimeOffset(reader, 16), AotDataReaderExtensions.ReadDateTimeOffset(reader, 17)));
        registrar.Register<WorkflowExecutionBudgetRecord>(reader => new(reader.GetGuid(0), AotDataReaderExtensions.ReadInt32(reader, 1),
            AotDataReaderExtensions.ReadInt32(reader, 2), AotDataReaderExtensions.ReadInt64(reader, 3)));
        registrar.Register<WorkflowDefinitionDraftRecord>(ReadDefinitionDraft);
        registrar.Register<WorkflowDefinitionVersionRecord>(ReadDefinitionVersion);
        registrar.Register<WorkflowFormDefinitionRecord>(ReadFormDefinition);
        registrar.Register<WorkflowFormVersionRecord>(ReadFormVersion);
        registrar.Register<WorkflowDesignerAssetRecord>(reader => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6)));
        registrar.Register<WorkflowInstanceRecord>(ReadInstance);
        registrar.Register<WorkflowTodoViewRecord>(reader => new(reader.GetString(0), reader.GetInt64(1),
            AotDataReaderExtensions.ReadNullableString(reader, 2), reader.IsDBNull(3) ? null : reader.GetInt64(3)));
        registrar.Register<WorkflowAttachmentReferenceRecord>(reader => new(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2),
            AotDataReaderExtensions.ReadNullableGuid(reader, 3)));
        registrar.Register<WorkflowAttachmentFileOwnerRecord>(reader => new(AotDataReaderExtensions.ReadNullableGuid(reader, 0)));
        registrar.Register<WorkflowMappedAttachmentFileOwnerRecord>(reader => new(reader.GetString(0), AotDataReaderExtensions.ReadNullableGuid(reader, 1)));
        registrar.Register<WorkflowForkContextRecord>(reader => new(AotDataReaderExtensions.ReadNullableGuid(reader, 0),
            AotDataReaderExtensions.ReadNullableString(reader, 1)));
        registrar.Register<WorkflowBranchStateRecord>(reader => new(reader.GetString(0), reader.GetString(1),
            reader.GetInt64(2), reader.GetString(3), reader.GetInt64(4)));
        registrar.Register<WorkflowActivityWorkRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1),
            reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6),
            reader.GetInt64(7), AotDataReaderExtensions.ReadNullableGuid(reader, 8),
            AotDataReaderExtensions.ReadNullableString(reader, 9), reader.GetInt64(10), reader.GetInt32(11), AotDataReaderExtensions.ReadNullableGuid(reader, 12)));
        registrar.Register<WorkflowAsyncBarrierRecord>(reader => new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),
            reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetInt64(8),AotDataReaderExtensions.ReadNullableGuid(reader,9),
            AotDataReaderExtensions.ReadNullableString(reader,10),reader.GetString(11),reader.GetInt64(12),AotDataReaderExtensions.ReadNullableString(reader,13),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader,14),reader.GetInt32(15),reader.GetInt64(16),AotDataReaderExtensions.ReadNullableGuid(reader,17),
            AotDataReaderExtensions.ReadNullableString(reader,18),AotDataReaderExtensions.ReadNullableString(reader,19),reader.IsDBNull(20)?null:reader.GetInt64(20),
            AotDataReaderExtensions.ReadDateTimeOffset(reader,21),AotDataReaderExtensions.ReadDateTimeOffset(reader,22),AotDataReaderExtensions.ReadNullableDateTimeOffset(reader,23),
            AotDataReaderExtensions.ReadNullableString(reader,24)));
        registrar.Register<WorkflowStartReceiptRecord>(reader => new(reader.GetString(0), reader.GetString(1)));
        registrar.Register<WorkflowConnectorResolutionRecord>(reader => new(reader.GetString(0),reader.GetString(1)));
        registrar.Register<WorkflowConnectorLatestResolutionRecord>(reader => new(reader.GetGuid(0),reader.GetString(1),AotDataReaderExtensions.ReadDateTimeOffset(reader,2)));
        registrar.Register<WorkflowConnectorAttemptSummaryRecord>(reader => new(reader.GetGuid(0),reader.GetGuid(1),reader.GetInt64(2),reader.GetString(3),
            AotDataReaderExtensions.ReadNullableString(reader,4),reader.IsDBNull(5)?null:reader.GetInt32(5),AotDataReaderExtensions.ReadNullableString(reader,6),
            AotDataReaderExtensions.ReadNullableString(reader,7),AotDataReaderExtensions.ReadDateTimeOffset(reader,8),AotDataReaderExtensions.ReadNullableDateTimeOffset(reader,9)));
        registrar.Register<WorkflowConnectorCallRecord>(reader => new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),
            reader.GetString(5),reader.GetInt32(6),reader.GetString(7),reader.GetString(8),reader.GetString(9),reader.GetString(10),reader.GetInt64(11),
            reader.GetString(12),reader.GetString(13),AotDataReaderExtensions.ReadNullableGuid(reader,14),AotDataReaderExtensions.ReadNullableString(reader,15),
            reader.GetString(16),AotDataReaderExtensions.ReadNullableGuid(reader,17),AotDataReaderExtensions.ReadNullableGuid(reader,18),reader.GetInt64(19),
            AotDataReaderExtensions.ReadNullableString(reader,20),AotDataReaderExtensions.ReadNullableDateTimeOffset(reader,21),reader.GetInt32(22),reader.GetInt32(23),
            AotDataReaderExtensions.ReadNullableString(reader,24),AotDataReaderExtensions.ReadDateTimeOffset(reader,25),AotDataReaderExtensions.ReadNullableDateTimeOffset(reader,26),
            reader.GetInt64(27),AotDataReaderExtensions.ReadDateTimeOffset(reader,28),AotDataReaderExtensions.ReadNullableDateTimeOffset(reader,29),reader.IsDBNull(30)?null:reader.GetInt64(30)));
        registrar.Register<WorkflowConnectorAttemptRecord>(reader => new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetInt64(3),reader.GetString(4),
            reader.GetString(5),reader.GetString(6),AotDataReaderExtensions.ReadNullableString(reader,7),AotDataReaderExtensions.ReadNullableString(reader,8),
            AotDataReaderExtensions.ReadNullableString(reader,9),reader.IsDBNull(10)?null:reader.GetInt32(10),AotDataReaderExtensions.ReadNullableString(reader,11),
            AotDataReaderExtensions.ReadDateTimeOffset(reader,12),AotDataReaderExtensions.ReadNullableDateTimeOffset(reader,13)));
        registrar.Register<WorkflowDelayRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1),
            reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.GetInt64(7), AotDataReaderExtensions.ReadNullableGuid(reader, 8),
            AotDataReaderExtensions.ReadNullableString(reader, 9), reader.GetInt64(10)));
        registrar.Register<WorkflowAttachmentReadViewRecord>(reader => new(reader.GetString(0), reader.GetString(1), AotDataReaderExtensions.ReadNullableString(reader, 2)));
        registrar.Register<WorkflowTodoAttachmentReadViewRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), AotDataReaderExtensions.ReadNullableString(reader, 4)));
        registrar.Register<WorkflowSignatureEvidenceRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
            reader.GetGuid(4), reader.GetInt64(5), reader.GetString(6), reader.GetGuid(7), reader.GetString(8), AotDataReaderExtensions.ReadDateTimeOffset(reader, 9)));
        registrar.Register<WorkflowTodoRecord>(ReadTodo);
        registrar.Register<WorkflowActiveWorkRecord>(ReadActiveWork);
        registrar.Register<WorkflowTodoRuntimeRecord>(ReadTodoRuntime);
        registrar.Register<WorkflowApprovalSlotRecord>(ReadApprovalSlot);
        registrar.Register<WorkflowTodoTransferLineageRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2)));
        registrar.Register<WorkflowTodoDelegationRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), AotDataReaderExtensions.ReadDateTimeOffset(reader, 4)));
        registrar.Register<WorkflowTaskContextRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2),
            reader.GetInt32(3), reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt64(7)));
        registrar.Register<WorkflowTaskTallyRecord>(reader => new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
        registrar.Register<WorkflowPendingTaskMemberRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetInt64(3)));
        registrar.Register<WorkflowApprovalGroupContextRecord>(reader => new(reader.GetGuid(0), reader.GetString(1),
            AotDataReaderExtensions.ReadNullableString(reader, 2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt64(6), reader.GetInt32(7)));
        registrar.Register<WorkflowPendingGroupApprovalSlotRecord>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetInt64(3), reader.GetGuid(4)));
        registrar.Register<WorkflowApprovalGroupProgressRecord>(reader => new(reader.GetGuid(0), reader.GetString(1),
            AotDataReaderExtensions.ReadNullableString(reader, 2), reader.GetInt32(3), reader.GetString(4), reader.GetInt32(5),
            reader.GetInt32(6), reader.GetString(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10), reader.GetInt64(11), reader.GetInt64(12)));
        registrar.Register<WorkflowPendingApprovalSlotRecord>(reader =>
            new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetInt64(3)));
        registrar.Register<WorkflowApprovalTallyRecord>(ReadApprovalTally);
        registrar.Register<WorkflowInstanceApprovalProgressRecord>(ReadInstanceApprovalProgress);
        registrar.Register<WorkflowTodoReturnTargetRecord>(ReadTodoReturnTarget);
        registrar.Register<WorkflowRuntimeAssetRecord>(ReadRuntimeAsset);
        registrar.Register<WorkflowFormSubmissionRecord>(ReadFormSubmission);
        registrar.Register<WorkflowActionReceiptRecord>(ReadActionReceipt);
        registrar.Register<WorkflowExecutionLogRecord>(ReadExecutionLog);
        registrar.Register<WorkflowCcRecord>(ReadCc);
        registrar.Register<WorkflowRecoveryTaskRecord>(ReadRecoveryTask);
        registrar.Register<WorkflowRecoveryScanCandidate>(ReadRecoveryScanCandidate);
        registrar.Register<WorkflowTodoTimeoutCandidateRecord>(ReadTodoTimeoutCandidate);
        registrar.Register<WorkflowTodoTimeoutSummaryRecord>(ReadTodoTimeoutSummary);
        registrar.Register<WorkflowCountersignChainRecord>(ReadCountersignChain);
        registrar.Register<WorkflowCountersignItemRecord>(ReadCountersignItem);
        registrar.Register<WorkflowCountersignItemContextRecord>(ReadCountersignItemContext);
        registrar.Register<WorkflowParallelJoinRecord>(ReadParallelJoin);
        registrar.Register<WorkflowParallelJoinStatusRecord>(ReadParallelJoinStatus);
    }

    private static WorkflowDefinitionRecord ReadDefinition(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            AotDataReaderExtensions.ReadNullableGuid(reader, 6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            reader.GetString(8),
            reader.GetGuid(9),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 10),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            reader.GetInt64(12));

    private static WorkflowDefinitionDraftRecord ReadDefinitionDraft(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetGuid(5),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 6));

    private static WorkflowDefinitionVersionRecord ReadDefinitionVersion(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            reader.GetGuid(8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9));

    private static WorkflowFormDefinitionRecord ReadFormDefinition(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt64(6),
            AotDataReaderExtensions.ReadNullableGuid(reader, 7),
            reader.GetString(8),
            reader.GetInt64(9),
            reader.GetGuid(10),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12));

    private static WorkflowFormVersionRecord ReadFormVersion(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetGuid(9),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 10));

    private static WorkflowInstanceRecord ReadInstance(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetGuid(4),
            AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            reader.GetString(6),
            reader.GetString(7),
            AotDataReaderExtensions.ReadNullableString(reader, 8),
            reader.GetString(9),
            reader.GetInt64(10),
            reader.GetGuid(11),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 12),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
            AotDataReaderExtensions.ReadNullableGuid(reader, 14),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 15),
            AotDataReaderExtensions.ReadNullableString(reader, 16),
            AotDataReaderExtensions.ReadNullableString(reader, 17),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 18));

    private static WorkflowTodoRecord ReadTodo(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            reader.GetInt64(8));

    private static WorkflowActiveWorkRecord ReadActiveWork(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetInt64(1),
            reader.GetGuid(2),
            reader.GetInt64(3));

    private static WorkflowTodoRuntimeRecord ReadTodoRuntime(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            reader.GetInt64(8),
            reader.GetString(9),
            reader.GetInt64(10),
            AotDataReaderExtensions.ReadNullableString(reader, 11),
            reader.IsDBNull(12) ? null : reader.GetInt32(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13),
            reader.IsDBNull(14) ? null : reader.GetGuid(14),
            AotDataReaderExtensions.ReadNullableString(reader, 15),
            AotDataReaderExtensions.ReadNullableString(reader, 16), reader.GetString(17));

    /// <summary>按显式 SQL 投影顺序物化审批席位。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>审批席位标识及修订号。</returns>
    private static WorkflowApprovalSlotRecord ReadApprovalSlot(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetInt64(1));

    /// <summary>按显式 SQL 投影顺序物化审批票数。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>当前步骤的赞成、反对与未决票数。</returns>
    private static WorkflowApprovalTallyRecord ReadApprovalTally(DbDataReader reader) =>
        new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));

    /// <summary>按显式 SQL 投影顺序物化实例活动多人审批进度。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>当前活动步骤的节点键、模式与权威票数。</returns>
    private static WorkflowInstanceApprovalProgressRecord ReadInstanceApprovalProgress(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5));

    /// <summary>按显式 SQL 投影顺序物化审批退回目标。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>Native AOT 安全的退回目标投影。</returns>
    private static WorkflowTodoReturnTargetRecord ReadTodoReturnTarget(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetInt64(3),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 4),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 5));

    private static WorkflowRuntimeAssetRecord ReadRuntimeAsset(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            reader.GetString(5));

    private static WorkflowFormSubmissionRecord ReadFormSubmission(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetGuid(6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 7));

    private static WorkflowActionReceiptRecord ReadActionReceipt(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetInt64(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            AotDataReaderExtensions.ReadNullableGuid(reader, 7));

    private static WorkflowExecutionLogRecord ReadExecutionLog(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadNullableGuid(reader, 2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            reader.GetString(5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 8));

    /// <summary>按显式 SQL 投影顺序物化“我的抄送”记录。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>Native AOT 安全的抄送持久化投影。</returns>
    private static WorkflowCcRecord ReadCc(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadNullableGuid(reader, 2),
            reader.GetString(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 8),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9));

    /// <summary>按显式 SQL 投影顺序物化恢复任务。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>Native AOT 安全的恢复任务投影。</returns>
    private static WorkflowRecoveryTaskRecord ReadRecoveryTask(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetGuid(4),
            AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetInt32(8),
            reader.GetInt64(9),
            AotDataReaderExtensions.ReadNullableString(reader, 10),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            reader.GetInt32(12),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
            AotDataReaderExtensions.ReadNullableString(reader, 14),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 15),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 16));

    /// <summary>按扫描 SQL 投影顺序物化恢复候选。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>Native AOT 安全的扫描候选投影。</returns>
    private static WorkflowRecoveryScanCandidate ReadRecoveryScanCandidate(DbDataReader reader) =>
        new(
            AotDataReaderExtensions.ReadNullableGuid(reader, 0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetGuid(3),
            AotDataReaderExtensions.ReadNullableGuid(reader, 4));

    /// <summary>按超时扫描投影顺序物化候选。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>Native AOT 安全的超时候选。</returns>
    private static WorkflowTodoTimeoutCandidateRecord ReadTodoTimeoutCandidate(DbDataReader reader) =>
        new(
            AotDataReaderExtensions.ReadNullableGuid(reader, 0), reader.GetString(1),
            reader.GetString(2), reader.GetGuid(3), reader.GetGuid(4), reader.GetGuid(5),
            reader.GetGuid(6), reader.GetString(7), reader.GetString(8), reader.GetInt64(9),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14),
            AotDataReaderExtensions.ReadNullableGuid(reader, 15),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 16),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 17));

    /// <summary>按实例详情投影顺序物化待办超时摘要。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>Native AOT 安全的超时摘要。</returns>
    private static WorkflowTodoTimeoutSummaryRecord ReadTodoTimeoutSummary(DbDataReader reader) =>
        new(reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 1),
            reader.GetInt32(2),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 3));

    private static WorkflowCountersignChainRecord ReadCountersignChain(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
            reader.GetString(4), reader.GetString(5), reader.GetGuid(6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 7));

    private static WorkflowCountersignItemRecord ReadCountersignItem(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetGuid(3),
            AotDataReaderExtensions.ReadNullableGuid(reader, 4), reader.GetString(5));

    private static WorkflowCountersignItemContextRecord ReadCountersignItemContext(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetGuid(3),
            AotDataReaderExtensions.ReadNullableGuid(reader, 4), reader.GetString(5),
            reader.GetString(6), reader.GetGuid(7), reader.GetGuid(8), reader.GetGuid(9),
            reader.GetString(10));

    /// <summary>按显式 SQL 投影顺序物化并行汇合状态。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>Native AOT 安全的汇合状态投影。</returns>
    private static WorkflowParallelJoinRecord ReadParallelJoin(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.GetInt64(8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10));

    /// <summary>按实例详情投影顺序物化并行汇合与分支到达状态。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>Native AOT 安全的汇合分支投影。</returns>
    private static WorkflowParallelJoinStatusRecord ReadParallelJoinStatus(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8));
}
#endif
