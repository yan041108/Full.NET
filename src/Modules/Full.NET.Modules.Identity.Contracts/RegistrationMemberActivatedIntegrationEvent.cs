using global::MemoryPack;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>Identity 拥有的注册成员激活事实；组织模块只消费已固定的组织分配意图。</summary>
public static class RegistrationMemberIntegrationEventTypes
{
    /// <summary>成员与注册方式业务角色已在同一本地事务激活。</summary>
    public const string Activated = "fullnet.identity.registration_member.activated";
}

/// <summary>激活时使用的注册方式快照；不携带邮箱、密码或邀请凭据。</summary>
/// <param name="OperationId">持久席位操作标识，也是分配意图幂等键。</param>
/// <param name="TenantId">成员所属可信租户。</param>
/// <param name="MemberId">成员关系标识。</param>
/// <param name="MemberVersion">激活后的成员版本；后续人工变更使旧意图失效。</param>
/// <param name="UserId">成员关联的账号。</param>
/// <param name="OrganizationUnitId">激活时配置的机构单元。</param>
/// <param name="PositionId">激活时配置的可选职位。</param>
/// <param name="OccurredAtUtc">激活发生时间。</param>
[MemoryPackable]
public partial record RegistrationMemberActivatedIntegrationEvent(
    Guid OperationId, Guid TenantId, Guid MemberId, int MemberVersion, Guid UserId,
    Guid OrganizationUnitId, Guid? PositionId, DateTimeOffset OccurredAtUtc);
