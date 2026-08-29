namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 定义 Identity 模块对外返回的稳定错误码。
/// </summary>
public static class IdentityErrorCodes
{
    /// <summary>
    /// Identity 错误码前缀。
    /// </summary>
    public const string Prefix = "identity.";

    /// <summary>初始化管理员密码不符合安全策略。</summary>
    public const string BootstrapInvalidPassword = "identity.bootstrap.invalid_password";

    /// <summary>初始化管理员资料无效。</summary>
    public const string BootstrapInvalidProfile = "identity.bootstrap.invalid_profile";

    /// <summary>会话请求的 CSRF 校验失败。</summary>
    public const string CsrfValidationFailed = "identity.csrf_validation_failed";

    /// <summary>当前身份的参与者范围不允许切换上下文。</summary>
    public const string InvalidActorScope = "identity.invalid_actor_scope";

    /// <summary>登录凭据无效。</summary>
    public const string InvalidCredentials = "identity.invalid_credentials";

    /// <summary>刷新令牌无效或已过期。</summary>
    public const string InvalidRefreshToken = "identity.invalid_refresh_token";

    /// <summary>浏览器请求来源不在允许列表中。</summary>
    public const string OriginNotAllowed = "identity.origin_not_allowed";

    /// <summary>账号资料已经被其他请求更新。</summary>
    public const string ProfileVersionConflict = "identity.profile_version_conflict";

    /// <summary>密码长度小于最低安全要求。</summary>
    public const string PasswordMinimumLength = "identity.password.minimum_length";

    /// <summary>密码缺少大写字母。</summary>
    public const string PasswordUppercaseRequired =
        "identity.password.uppercase_required";

    /// <summary>密码缺少小写字母。</summary>
    public const string PasswordLowercaseRequired =
        "identity.password.lowercase_required";

    /// <summary>密码缺少数字。</summary>
    public const string PasswordDigitRequired = "identity.password.digit_required";

    /// <summary>密码缺少非字母数字字符。</summary>
    public const string PasswordNonAlphanumericRequired =
        "identity.password.non_alphanumeric_required";

    /// <summary>检测到刷新令牌重复使用并撤销会话族。</summary>
    public const string RefreshTokenReuseDetected = "identity.refresh_token_reuse_detected";

    /// <summary>会话上下文发生并发冲突。</summary>
    public const string SessionContextConflict = "identity.session_context_conflict";

    /// <summary>当前会话已失效。</summary>
    public const string SessionNotActive = "identity.session_not_active";

    /// <summary>目标在线会话不存在或已下线。</summary>
    public const string OnlineSessionNotFound = "identity.online_session_not_found";

    /// <summary>认证会话写请求超过允许速率。</summary>
    public const string AuthenticationRateLimited = "identity.authentication.rate_limited";

    /// <summary>超级管理员远程写操作当前未启用。</summary>
    public const string SuperAdministratorRemoteManagementDisabled =
        "identity.super_administrator.remote_management_disabled";

    /// <summary>超级管理员高风险操作的当前密码重认证失败。</summary>
    public const string SuperAdministratorReauthenticationFailed =
        "identity.super_administrator.reauthentication_failed";

    /// <summary>执行人不是当前有效的超级管理员。</summary>
    public const string SuperAdministratorOperatorRequired =
        "identity.super_administrator.operator_required";

    /// <summary>目标不是有效的 Host 账号。</summary>
    public const string SuperAdministratorTargetNotFound =
        "identity.super_administrator.target_not_found";

    /// <summary>最后一名有效超级管理员受系统保护。</summary>
    public const string SuperAdministratorLastRemaining =
        "identity.super_administrator.last_remaining";

    /// <summary>Production 强认证路径要求提供 TOTP 验证码。</summary>
    public const string MfaTotpRequired = "identity.mfa.totp_required";

    /// <summary>TOTP 验证码无效或已过期。</summary>
    public const string MfaTotpInvalid = "identity.mfa.totp_invalid";

    /// <summary>操作者尚未确认启用 TOTP。</summary>
    public const string MfaTotpNotEnrolled = "identity.mfa.not_enrolled";

    /// <summary>Host 用户名在作用域内已存在。</summary>
    public const string UsernameExists = "identity.users.username_exists";

    /// <summary>目标 Host 用户不存在。</summary>
    public const string UserNotFound = "identity.users.not_found";

    /// <summary>导入行试图授予超级管理员，已拒绝。</summary>
    public const string SuperAdministratorImportRejected =
        "identity.users.super_administrator_import_rejected";

    /// <summary>Host 角色编码在作用域内已存在。</summary>
    public const string RoleCodeExists = "identity.roles.code_exists";

    /// <summary>目标 Host 角色不存在。</summary>
    public const string RoleNotFound = "identity.roles.not_found";

    /// <summary>系统角色受保护，禁止变更。</summary>
    public const string RoleSystemLocked = "identity.roles.system_locked";

    /// <summary>操作权限缺少父页面读取权限。</summary>
    public const string ActionRequiresPage = "identity.roles.action_requires_page";

    /// <summary>字段投影资源或字段键不在服务端稳定目录中。</summary>
    public const string FieldProjectionInvalid = "identity.field_projection.invalid";

    /// <summary>角色字段授权发生并发版本冲突。</summary>
    public const string FieldProjectionVersionConflict = "identity.field_projection.version_conflict";

    /// <summary>数据范围种类无效。</summary>
    public const string DataScopeInvalidKind = "identity.data_scope.invalid_kind";

    /// <summary>自定义数据范围缺少机构单元。</summary>
    public const string DataScopeCustomUnitsRequired = "identity.data_scope.custom_units_required";

    /// <summary>自定义数据范围缺少显式目标租户。</summary>
    public const string DataScopeTenantContextRequired = "identity.data_scope.tenant_context_required";

    /// <summary>数据范围引用的机构单元不存在。</summary>
    public const string DataScopeUnitNotFound = "identity.data_scope.unit_not_found";

    /// <summary>用户角色分配引用的角色不存在。</summary>
    public const string UserRolesRoleNotFound = "identity.user_roles.role_not_found";

    /// <summary>用户角色分配包含不可分配角色。</summary>
    public const string UserRolesRoleNotAssignable = "identity.user_roles.role_not_assignable";

    /// <summary>Host 菜单路由名在作用域内已存在。</summary>
    public const string MenuRouteNameExists = "identity.menus.route_name_exists";

    /// <summary>目标 Host 菜单不存在。</summary>
    public const string MenuNotFound = "identity.menus.not_found";

    /// <summary>系统菜单受保护，禁止变更。</summary>
    public const string MenuSystemLocked = "identity.menus.system_locked";

    /// <summary>目标 API Key 不存在或已禁用。</summary>
    public const string ApiKeyNotFound = "identity.api_keys.not_found";

    /// <summary>API Key 权限列表无效。</summary>
    public const string ApiKeyInvalidPermissions = "identity.api_keys.invalid_permissions";

    /// <summary>API Key 绑定的 Host 用户不存在。</summary>
    public const string ApiKeyUserNotFound = "identity.api_keys.user_not_found";

    /// <summary>API Key 绑定的 Host 用户已禁用。</summary>
    public const string ApiKeyUserInactive = "identity.api_keys.user_inactive";

    /// <summary>签名认证请求头不完整。</summary>
    public const string SignatureMissingHeaders = "identity.signature.missing_headers";

    /// <summary>签名认证请求头重复。</summary>
    public const string SignatureDuplicateHeaders = "identity.signature.duplicate_headers";

    /// <summary>签名请求体超过允许上限。</summary>
    public const string SignatureRequestBodyTooLarge = "identity.signature.request_body_too_large";

    /// <summary>签名协议版本无效。</summary>
    public const string SignatureInvalidVersion = "identity.signature.invalid_version";

    /// <summary>签名时间戳格式无效。</summary>
    public const string SignatureInvalidTimestamp = "identity.signature.invalid_timestamp";

    /// <summary>签名时间戳已过期。</summary>
    public const string SignatureTimestampExpired = "identity.signature.timestamp_expired";

    /// <summary>签名时间戳超出未来窗口。</summary>
    public const string SignatureTimestampInFuture = "identity.signature.timestamp_in_future";

    /// <summary>签名 Nonce 无效。</summary>
    public const string SignatureInvalidNonce = "identity.signature.invalid_nonce";

    /// <summary>检测到签名 Nonce 重放。</summary>
    public const string SignatureReplayDetected = "identity.signature.replay_detected";

    /// <summary>路径或 Query 编码不符合签名规范。</summary>
    public const string SignatureInvalidEncoding = "identity.signature.invalid_encoding";

    /// <summary>请求签名无效。</summary>
    public const string SignatureInvalidSignature = "identity.signature.invalid_signature";

    /// <summary>Access Key 不存在。</summary>
    public const string SignatureAccessKeyNotFound = "identity.signature.access_key_not_found";

    /// <summary>Access Key 已禁用或轮换。</summary>
    public const string SignatureAccessKeyDisabled = "identity.signature.access_key_disabled";

    /// <summary>Access Key 已过期。</summary>
    public const string SignatureAccessKeyExpired = "identity.signature.access_key_expired";

    /// <summary>Access Key 与租户作用域不匹配。</summary>
    public const string SignatureTenantScopeMismatch = "identity.signature.tenant_scope_mismatch";

    /// <summary>目标模块不在只读清单中。</summary>
    public const string ModuleCatalogNotFound = "identity.modules.not_found";

    /// <summary>机构投影对账租户标识无效。</summary>
    public const string OrganizationUnitProjectionInvalidTenant =
        "identity.organization_unit_projection.invalid_tenant";

    /// <summary>机构投影对账模式无效。</summary>
    public const string OrganizationUnitProjectionInvalidMode =
        "identity.organization_unit_projection.invalid_mode";

    /// <summary>机构投影对账分页大小超出 1-100 有界范围。</summary>
    public const string OrganizationUnitProjectionInvalidPageSize =
        "identity.organization_unit_projection.invalid_page_size";

    /// <summary>用户导入工作簿格式、大小或内容无效。</summary>
    public const string UserImportWorkbookInvalid =
        "identity.user_import.workbook_invalid";

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        BootstrapInvalidPassword,
        BootstrapInvalidProfile,
        CsrfValidationFailed,
        InvalidActorScope,
        InvalidCredentials,
        InvalidRefreshToken,
        OriginNotAllowed,
        ProfileVersionConflict,
        PasswordMinimumLength,
        PasswordUppercaseRequired,
        PasswordLowercaseRequired,
        PasswordDigitRequired,
        PasswordNonAlphanumericRequired,
        RefreshTokenReuseDetected,
        SessionContextConflict,
        SessionNotActive,
        AuthenticationRateLimited,
        SuperAdministratorRemoteManagementDisabled,
        SuperAdministratorReauthenticationFailed,
        SuperAdministratorOperatorRequired,
        SuperAdministratorTargetNotFound,
        SuperAdministratorLastRemaining,
        MfaTotpRequired,
        MfaTotpInvalid,
        MfaTotpNotEnrolled,
        UsernameExists,
        UserNotFound,
        SuperAdministratorImportRejected,
        RoleCodeExists,
        RoleNotFound,
        RoleSystemLocked,
        ActionRequiresPage,
        FieldProjectionInvalid,
        FieldProjectionVersionConflict,
        DataScopeInvalidKind,
        DataScopeCustomUnitsRequired,
        DataScopeTenantContextRequired,
        DataScopeUnitNotFound,
        UserRolesRoleNotFound,
        UserRolesRoleNotAssignable,
        MenuRouteNameExists,
        MenuNotFound,
        MenuSystemLocked,
        ApiKeyNotFound,
        ApiKeyInvalidPermissions,
        ApiKeyUserNotFound,
        ApiKeyUserInactive,
        SignatureMissingHeaders,
        SignatureDuplicateHeaders,
        SignatureRequestBodyTooLarge,
        SignatureInvalidVersion,
        SignatureInvalidTimestamp,
        SignatureTimestampExpired,
        SignatureTimestampInFuture,
        SignatureInvalidNonce,
        SignatureReplayDetected,
        SignatureInvalidEncoding,
        SignatureInvalidSignature,
        SignatureAccessKeyNotFound,
        SignatureAccessKeyDisabled,
        SignatureAccessKeyExpired,
        SignatureTenantScopeMismatch,
        ModuleCatalogNotFound,
        OrganizationUnitProjectionInvalidTenant,
        OrganizationUnitProjectionInvalidMode,
        OrganizationUnitProjectionInvalidPageSize,
        UserImportWorkbookInvalid,
    ]);
}
