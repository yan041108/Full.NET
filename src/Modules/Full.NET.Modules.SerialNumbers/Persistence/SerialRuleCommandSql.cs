using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.SerialNumbers.Persistence;

/// <summary>审批命令凭据与规则只在SerialNumbers本地事务中共同提交；不存在跨模块外键。</summary>
internal static class SerialRuleCommandSql
{
    private const string Projection = "Id,ApprovalRequestId,RuleId,RequestedByUserId,ScenarioKey,InputHash,BeforeSnapshotJson,StatusKey,AppliedVersion,CompensatedVersion,Revision,CreatedAtUtc,UpdatedAtUtc";
    public static readonly SqlStatement Find = new("serial_numbers.approval_command.find",
        "SELECT " + Projection + " FROM fn_serialnumbers_approval_cmd WHERE Id=@Id", SqlDataScope.HostOnly);
    public static readonly SqlStatement LockSqlServer = new("serial_numbers.approval_command.lock_sqlserver",
        "SELECT " + Projection + " FROM fn_serialnumbers_approval_cmd WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id", SqlDataScope.HostOnly);
    public static readonly SqlStatement LockMySql = new("serial_numbers.approval_command.lock_mysql",
        "SELECT " + Projection + " FROM fn_serialnumbers_approval_cmd WHERE Id=@Id FOR UPDATE", SqlDataScope.HostOnly);
    public static readonly SqlStatement Insert = new("serial_numbers.approval_command.insert", """
        INSERT INTO fn_serialnumbers_approval_cmd
            (Id,ApprovalRequestId,RuleId,RequestedByUserId,ScenarioKey,InputHash,BeforeSnapshotJson,StatusKey,
             AppliedVersion,CompensatedVersion,Revision,CreatedAtUtc,UpdatedAtUtc)
        VALUES (@Id,@ApprovalRequestId,@RuleId,@RequestedByUserId,@ScenarioKey,@InputHash,@BeforeSnapshotJson,@StatusKey,
                @AppliedVersion,NULL,1,@Now,@Now)
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement CompleteCompensation = new("serial_numbers.approval_command.complete_compensation", """
        UPDATE fn_serialnumbers_approval_cmd
        SET StatusKey='compensated',CompensatedVersion=@CompensatedVersion,Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND InputHash=@InputHash AND StatusKey='applied' AND Revision=@Revision
        """, SqlDataScope.HostOnly);
}

/// <summary>投影顺序由显式AOT物化器固定；应用前取消以无写入版本的墓碑表示。</summary>
internal sealed record SerialRuleCommandRecord(Guid Id, Guid ApprovalRequestId, Guid RuleId, Guid RequestedByUserId,
    string ScenarioKey, string InputHash, string? BeforeSnapshotJson, string StatusKey, long? AppliedVersion,
    long? CompensatedVersion, long Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
