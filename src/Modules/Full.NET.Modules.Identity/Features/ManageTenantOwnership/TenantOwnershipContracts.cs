using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.ManageTenantOwnership;

/// <summary>当前企业受保护的 Owner 职责；目录历史字段不参与此状态。</summary>
internal sealed record TenantOwnershipStateResponse(Guid TenantId, TenantMemberResponse? Owner,
    bool CanInitialize, bool CanTransfer, bool RequiresTotp);

/// <summary>明确源/接收成员版本与操作标识；强认证凭据只用于本次验证，不进入持久回执。</summary>
internal sealed record TransferTenantOwnerRequest(Guid OperationId, Guid? SourceMemberId, int? SourceVersion,
    Guid TargetMemberId, int TargetVersion, string CurrentPassword, string? TotpCode)
{
    /// <summary>诊断字符串不展开强认证凭据，避免记录请求对象时泄露密码或 TOTP。</summary>
    public override string ToString() => nameof(TransferTenantOwnerRequest);
}

/// <summary>交接事务中的不可变事实回执，供超时后人工核对；不等于给目标账号授予所有业务权限。</summary>
internal sealed record TenantOwnerTransferReceipt(Guid TenantId, Guid OperationId, Guid ActorUserId,
    Guid? SourceMemberId, Guid? SourceUserId, int? SourcePreviousVersion, int? SourceVersion,
    Guid TargetMemberId, Guid TargetUserId, int TargetPreviousVersion, int TargetVersion, DateTimeOffset CreatedAtUtc);

/// <summary>包含内部请求摘要的回执存储投影；摘要只绑定非敏感目标和版本。</summary>
internal sealed record TenantOwnerTransferRow(Guid TenantId, Guid OperationId, Guid ActorUserId,
    Guid? SourceMemberId, Guid? SourceUserId, int? SourcePreviousVersion, int? SourceVersion,
    Guid TargetMemberId, Guid TargetUserId, int TargetPreviousVersion, int TargetVersion, DateTimeOffset CreatedAtUtc,
    string RequestDigest)
{
    internal TenantOwnerTransferReceipt ToReceipt() => new(TenantId, OperationId, ActorUserId, SourceMemberId,
        SourceUserId, SourcePreviousVersion, SourceVersion, TargetMemberId, TargetUserId,
        TargetPreviousVersion, TargetVersion, CreatedAtUtc);
}
