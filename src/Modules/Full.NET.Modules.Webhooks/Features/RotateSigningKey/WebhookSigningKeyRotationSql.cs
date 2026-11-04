using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;

namespace Full.NET.Modules.Webhooks.Features.RotateSigningKey;

/// <summary>准备与完成均锁定原订阅修订；秘密只在准备期间保留，审计元数据不删除。</summary>
internal static class WebhookSigningKeyRotationSql
{
    private const string Columns = "rotation.Id,rotation.TenantId,rotation.OperationId,rotation.ActorUserId,rotation.ActorSessionId,rotation.SubscriptionId,rotation.PreviousKeyId,rotation.PreviousVersion,rotation.PreparedVersion,rotation.StateKey,rotation.ProtectedSecret,rotation.ProtectedFingerprint,rotation.PreparedAtUtc,rotation.CompletedAtUtc,rotation.CompletedVersion,rotation.CompletedByUserId,rotation.CompletedBySessionId";
    internal static readonly SqlStatement Find = new("webhooks.signing_keys.find_rotation",$"SELECT {Columns} FROM fn_webhooks_key_rotation rotation WHERE rotation.TenantId=@TenantId AND rotation.OperationId=@OperationId",SqlDataScope.TenantRequired,SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Pending = new("webhooks.signing_keys.find_pending",$"SELECT {Columns} FROM fn_webhooks_key_rotation rotation INNER JOIN fn_webhooks_subscription subscription ON subscription.PreparedRotationId=rotation.Id AND subscription.Id=rotation.SubscriptionId AND subscription.TenantId=rotation.TenantId WHERE rotation.TenantId=@TenantId AND rotation.SubscriptionId=@SubscriptionId AND rotation.StateKey='prepared'",SqlDataScope.TenantRequired,SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Reserve = new("webhooks.signing_keys.prepare","""
        UPDATE fn_webhooks_subscription SET PreparedRotationId=@Id,Version=Version+1,UpdatedAtUtc=@Now
        WHERE Id=@SubscriptionId AND TenantId=@TenantId AND Version=@PreviousVersion
          AND SigningKeyId=@PreviousKeyId AND PreparedRotationId IS NULL
        """,SqlDataScope.TenantRequired,SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Insert = new("webhooks.signing_keys.insert_rotation","""
        INSERT INTO fn_webhooks_key_rotation
          (Id,TenantId,OperationId,ActorUserId,ActorSessionId,SubscriptionId,PreviousKeyId,PreviousVersion,PreparedVersion,StateKey,ProtectedSecret,ProtectedFingerprint,PreparedAtUtc)
        VALUES (@Id,@TenantId,@OperationId,@ActorUserId,@ActorSessionId,@SubscriptionId,@PreviousKeyId,@PreviousVersion,@PreparedVersion,'prepared',@ProtectedSecret,@ProtectedFingerprint,@Now)
        """,SqlDataScope.TenantRequired,SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Activate = new("webhooks.signing_keys.activate","""
        UPDATE fn_webhooks_subscription SET SigningKeyId=@Id,SigningSecretHash=@ProtectedSecret,PreparedRotationId=NULL,Version=Version+1,UpdatedAtUtc=@Now
        WHERE Id=@SubscriptionId AND TenantId=@TenantId AND Version=@Version AND SigningKeyId=@PreviousKeyId AND PreparedRotationId=@Id
        """,SqlDataScope.TenantRequired,SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Cancel = new("webhooks.signing_keys.cancel","""
        UPDATE fn_webhooks_subscription SET PreparedRotationId=NULL,Version=Version+1,UpdatedAtUtc=@Now
        WHERE Id=@SubscriptionId AND TenantId=@TenantId AND Version=@Version AND SigningKeyId=@PreviousKeyId AND PreparedRotationId=@Id
        """,SqlDataScope.TenantRequired,SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Complete = new("webhooks.signing_keys.complete_rotation","""
        UPDATE fn_webhooks_key_rotation SET StateKey=@StateKey,ProtectedSecret=NULL,CompletedAtUtc=@Now,
          CompletedVersion=@CompletedVersion,CompletedByUserId=@ActorUserId,CompletedBySessionId=@ActorSessionId
        WHERE TenantId=@TenantId AND Id=@Id AND SubscriptionId=@SubscriptionId AND StateKey='prepared'
        """,SqlDataScope.TenantRequired,SqlTenantBinding.CurrentTenantId);
}

/// <summary>域内持久化模型；加密指纹用于相同操作重试时核对原输入。</summary>
internal sealed record WebhookSigningKeyRotationRecord(Guid Id,Guid TenantId,Guid OperationId,Guid ActorUserId,Guid ActorSessionId,
    Guid SubscriptionId,Guid PreviousKeyId,int PreviousVersion,int PreparedVersion,string StateKey,string? ProtectedSecret,string ProtectedFingerprint,
    DateTimeOffset PreparedAtUtc,DateTimeOffset? CompletedAtUtc,int? CompletedVersion,Guid? CompletedByUserId,Guid? CompletedBySessionId)
{
    internal WebhookSigningKeyRotationReceipt Receipt()=>new(OperationId,TenantId,SubscriptionId,PreviousKeyId,Id,PreviousVersion,PreparedVersion,StateKey,PreparedAtUtc,CompletedAtUtc,CompletedVersion);
}
