namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>微信小程序 OpenId 绑定的验证状态；code2session 成功后固定为 verified。</summary>
public static class WeChatMiniProgramBindingStatusKeys
{
    /// <summary>已通过 js_code 交换并完成身份确认。</summary>
    public const string Verified = "verified";

    /// <summary>绑定已撤销，不再用于投递。</summary>
    public const string Revoked = "revoked";
}

/// <summary>订阅消息授权状态；只记录客户端 subscribeMessage 结果。</summary>
public static class WeChatMiniProgramSubscriptionStatusKeys
{
    /// <summary>用户接受订阅。</summary>
    public const string Accepted = "accepted";

    /// <summary>用户拒绝订阅。</summary>
    public const string Rejected = "rejected";
}

/// <summary>微信小程序绑定与订阅管理的独立稳定权限码。</summary>
public static class WeChatMiniProgramBindingPermissions
{
    /// <summary>查询绑定与订阅授权列表。</summary>
    public const string Read = "notifications.wechat_miniprogram_bindings.read";

    /// <summary>通过 js_code 交换 OpenId 并完成绑定。</summary>
    public const string Bind = "notifications.wechat_miniprogram_bindings.bind";

    /// <summary>登记或更新订阅消息授权结果。</summary>
    public const string RecordSubscription = "notifications.wechat_miniprogram_bindings.record_subscription";
}

/// <summary>小程序端 js_code 交换绑定请求。</summary>
/// <param name="ProviderProfileVersionId">已发布且启用的 im.wechat_miniprogram Profile 版本。</param>
/// <param name="JsCode">wx.login 返回的一次性授权码。</param>
public sealed record ExchangeWeChatMiniProgramBindingRequest(
    Guid ProviderProfileVersionId,
    string JsCode);

/// <summary>登记订阅消息授权结果。</summary>
/// <param name="TemplateId">微信订阅模板标识。</param>
/// <param name="StatusKey">accepted 或 rejected。</param>
public sealed record RecordWeChatMiniProgramSubscriptionRequest(
    string TemplateId,
    string StatusKey);

/// <summary>微信小程序 OpenId 绑定响应；OpenId 只以掩码返回。</summary>
public sealed record WeChatMiniProgramBindingResponse(
    Guid Id,
    Guid UserId,
    string AppId,
    Guid ProviderProfileVersionId,
    string OpenIdMask,
    string VerificationStatusKey,
    Guid? RecipientEndpointId,
    IReadOnlyList<WeChatMiniProgramSubscriptionResponse> Subscriptions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>单条订阅模板授权记录。</summary>
public sealed record WeChatMiniProgramSubscriptionResponse(
    string TemplateId,
    string StatusKey,
    DateTimeOffset AuthorizedAtUtc);
