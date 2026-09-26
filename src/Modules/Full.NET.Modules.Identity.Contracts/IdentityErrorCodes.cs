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

    /// <summary>历史错误码；OIDC 切租户已由 <see cref="SessionContextConflict"/> / <see cref="SessionNotActive"/> 等边界表达。</summary>
    [Obsolete("OIDC context switch is implemented; kept for backward-compatible clients only.")]
    public const string OidcContextSwitchNotSupported = "identity.oidc_context_switch_not_supported";

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

    /// <summary>MFA 恢复码无效、已消费或并发冲突。</summary>
    public const string MfaRecoveryCodeInvalid = "identity.mfa.recovery_code_invalid";

    /// <summary>Host 用户名在作用域内已存在。</summary>
    public const string UsernameExists = "identity.users.username_exists";

    /// <summary>目标 Host 用户不存在。</summary>
    public const string UserNotFound = "identity.users.not_found";

    /// <summary>目标 Host 用户当前不存在可解除的登录锁定状态。</summary>
    public const string LoginNotLocked = "identity.users.login_not_locked";

    /// <summary>已禁用用户须通过启用操作恢复，禁止借解锁间接启用。</summary>
    public const string UnlockInactiveUserRejected = "identity.users.unlock_inactive_user_rejected";

    /// <summary>Host 用户已退役，不能再启用或更新。</summary>
    public const string HostUserAlreadyRetired = "identity.users.already_retired";

    /// <summary>存在活动租户成员关系时不能退役 Host 用户。</summary>
    public const string HostUserActiveTenantMemberships =
        "identity.users.active_tenant_memberships";

    /// <summary>当前账号必须先完成改密后才能访问普通业务 API。</summary>
    public const string PasswordChangeRequired = "identity.password_change_required";

    /// <summary>导入行试图授予超级管理员，已拒绝。</summary>
    public const string SuperAdministratorImportRejected =
        "identity.users.super_administrator_import_rejected";

    /// <summary>Host 角色编码在作用域内已存在。</summary>
    public const string RoleCodeExists = "identity.roles.code_exists";

    /// <summary>目标 Host 角色不存在。</summary>
    public const string RoleNotFound = "identity.roles.not_found";

    /// <summary>系统角色受保护，禁止变更。</summary>
    public const string RoleSystemLocked = "identity.roles.system_locked";

    /// <summary>超级管理员角色不能作为复制来源。</summary>
    public const string RoleCopySourceNotAllowed = "identity.roles.copy_source_not_allowed";

    /// <summary>角色仍有关联成员，禁止删除。</summary>
    public const string RoleHasMembers = "identity.roles.has_members";

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

    /// <summary>目标 OIDC 客户端不存在。</summary>
    public const string OidcClientNotFound = "identity.oidc_clients.not_found";

    /// <summary>OIDC client_id 在作用域内已存在。</summary>
    public const string OidcClientIdConflict = "identity.oidc_clients.client_id_conflict";

    /// <summary>OIDC 客户端已停用。</summary>
    public const string OidcClientDisabled = "identity.oidc_clients.disabled";

    /// <summary>目标 OIDC 授权授予不存在。</summary>
    public const string OidcAuthorizationNotFound = "identity.oidc_authorizations.not_found";

    /// <summary>OIDC 签名密钥无法激活（缺失、未知或无私钥材料）。</summary>
    public const string OidcSigningKeyNotActivatable = "identity.oidc_signing_keys.not_activatable";

    /// <summary>目标 OpenAccess 接入方应用不存在或已停用。</summary>
    public const string OpenAccessClientNotFound = "identity.open_access_clients.not_found";

    /// <summary>OpenAccess 接入方应用乐观并发版本冲突。</summary>
    public const string OpenAccessClientVersionConflict = "identity.open_access_clients.version_conflict";

    /// <summary>OpenAccess 接入方应用当日配额已用尽。</summary>
    public const string OpenAccessClientQuotaExceeded = "identity.open_access_clients.quota_exceeded";

    /// <summary>OpenAccess 签名调试请求无效或超出受控边界。</summary>
    public const string OpenAccessClientSignatureDebugInvalid =
        "identity.open_access_clients.signature_debug_invalid";

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

    /// <summary>Host 用户权威资料格式或字段组合无效。</summary>
    public const string UserProfileInvalid = "identity.users.profile_invalid";

    /// <summary>客户端提交了掩码占位值，禁止写回权威档案。</summary>
    public const string ProfileMaskedValueRejected = "identity.users.profile_masked_value_rejected";

    /// <summary>自助改密时当前密码校验失败。</summary>
    public const string CurrentPasswordInvalid = "identity.password.current_invalid";

    /// <summary>自助资料更新仅适用于 Host 参与者范围。</summary>
    public const string SelfServiceProfileHostOnly = "identity.self_service_profile.host_only";

    /// <summary>自助资料更新提交了只读敏感或内部字段。</summary>
    public const string SelfServiceProfileReadOnlyFieldRejected =
        "identity.self_service_profile.read_only_field_rejected";

    /// <summary>自助资料媒体文件类型或体积不符合策略。</summary>
    public const string SelfServiceProfileMediaInvalid =
        "identity.self_service_profile.media_invalid";

    /// <summary>自助资料媒体文件不存在或未绑定到当前用户。</summary>
    public const string SelfServiceProfileMediaNotFound =
        "identity.self_service_profile.media_not_found";

    /// <summary>新密码与当前密码相同。</summary>
    public const string NewPasswordSameAsCurrent = "identity.password.same_as_current";

    /// <summary>Host 用户手机号已被目录内其他用户占用。</summary>
    public const string UserPhoneNumberExists = "identity.users.phone_number_exists";

    /// <summary>Host 用户邮箱已被目录内其他用户占用。</summary>
    public const string UserEmailExists = "identity.users.email_exists";

    /// <summary>Host 用户工号已被目录内其他用户占用。</summary>
    public const string UserEmployeeNumberExists = "identity.users.employee_number_exists";

    /// <summary>Host 用户证件类型与号码组合已被目录内其他用户占用。</summary>
    public const string UserIdCardExists = "identity.users.id_card_exists";

    /// <summary>注册策略单例不存在。</summary>
    public const string RegistrationPolicyNotFound = "identity.registration_policy.not_found";

    /// <summary>注册策略并发版本冲突。</summary>
    public const string RegistrationPolicyVersionConflict =
        "identity.registration_policy.version_conflict";

    /// <summary>公开注册入口已关闭。</summary>
    public const string PublicRegistrationDisabled = "identity.registration.public_disabled";

    /// <summary>注册功能已禁用。</summary>
    public const string RegistrationDisabled = "identity.registration.disabled";

    /// <summary>账号挑战无效、已过期或已消费。</summary>
    public const string AccountChallengeInvalid = "identity.account_challenge.invalid";

    /// <summary>账号挑战尝试次数已耗尽。</summary>
    public const string AccountChallengeAttemptsExceeded = "identity.account_challenge.attempts_exceeded";

    /// <summary>账号挑战投递失败。</summary>
    public const string AccountChallengeDeliveryFailed = "identity.account_challenge.delivery_failed";

    /// <summary>注册邀请无效、已过期或已撤销。</summary>
    public const string RegistrationInvitationInvalid = "identity.registration_invitation.invalid";

    /// <summary>注册邮箱已被占用。</summary>
    public const string RegistrationEmailAlreadyExists = "identity.registration.email_already_exists";

    /// <summary>密码恢复请求已受理。</summary>
    public const string PasswordRecoveryAccepted = "identity.password_recovery.accepted";

    /// <summary>注册方式不存在。</summary>
    public const string RegistrationWayNotFound = "identity.registration_ways.not_found";

    /// <summary>注册方式并发版本冲突。</summary>
    public const string RegistrationWayVersionConflict =
        "identity.registration_ways.version_conflict";

    /// <summary>注册方式机器码无效。</summary>
    public const string RegistrationWayInvalidCode = "identity.registration_ways.invalid_code";

    /// <summary>注册方式机器码在租户内已存在。</summary>
    public const string RegistrationWayCodeExists = "identity.registration_ways.code_exists";

    /// <summary>注册方式引用的租户不存在或未激活。</summary>
    public const string RegistrationWayTenantInactive = "identity.registration_ways.tenant_inactive";

    /// <summary>注册方式引用的角色不存在、未激活或跨租户。</summary>
    public const string RegistrationWayRoleNotFound = "identity.registration_ways.role_not_found";

    /// <summary>注册方式引用的机构单元不存在、未激活或跨租户。</summary>
    public const string RegistrationWayOrganizationUnitNotFound =
        "identity.registration_ways.organization_unit_not_found";

    /// <summary>注册方式引用的职位不存在、未激活或跨租户。</summary>
    public const string RegistrationWayPositionNotFound =
        "identity.registration_ways.position_not_found";

    /// <summary>租户成员不存在。</summary>
    public const string TenantMemberNotFound = "identity.tenant_members.not_found";

    /// <summary>租户成员并发版本冲突。</summary>
    public const string TenantMemberVersionConflict = "identity.tenant_members.version_conflict";

    /// <summary>租户成员角色无效。</summary>
    public const string TenantMemberRoleInvalid = "identity.tenant_members.role_invalid";

    /// <summary>租户所有者受保护。</summary>
    public const string TenantOwnerProtected = "identity.tenant_members.owner_protected";

    /// <summary>用户已是活动租户成员。</summary>
    public const string TenantMemberAlreadyActive = "identity.tenant_members.already_active";

    /// <summary>目标租户已暂停或未激活，不能执行成员写操作。</summary>
    public const string TenantMembershipTenantInactive = "identity.tenant_members.tenant_inactive";

    /// <summary>租户邀请无效、已过期或已撤销。</summary>
    public const string TenantInvitationInvalid = "identity.tenant_invitations.invalid";

    /// <summary>租户邀请不存在。</summary>
    public const string TenantInvitationNotFound = "identity.tenant_invitations.not_found";

    /// <summary>租户席位配额服务不可用（Tenancy 未启用）。</summary>
    public const string TenantSeatQuotaUnavailable = "identity.tenant_members.seat_quota_unavailable";

    /// <summary>LDAP 连接不存在。</summary>
    public const string LdapConnectionNotFound = "identity.ldap_connections.not_found";

    /// <summary>LDAP 连接并发版本冲突。</summary>
    public const string LdapConnectionVersionConflict =
        "identity.ldap_connections.version_conflict";

    /// <summary>同一租户或 Host 作用域已存在 LDAP 连接。</summary>
    public const string LdapConnectionScopeAlreadyConfigured =
        "identity.ldap_connections.scope_already_configured";

    /// <summary>LDAP 连接引用的租户不存在或未激活。</summary>
    public const string LdapConnectionTenantInactive =
        "identity.ldap_connections.tenant_inactive";

    /// <summary>同步搜索根 DN 不在目录根 DN 允许范围内。</summary>
    public const string LdapConnectionInvalidSyncSearchBase =
        "identity.ldap_connections.invalid_sync_search_base";

    /// <summary>LDAP 连接元数据无效。</summary>
    public const string LdapConnectionInvalidMetadata =
        "identity.ldap_connections.invalid_metadata";

    /// <summary>创建 LDAP 连接时缺少绑定密码。</summary>
    public const string LdapConnectionBindPasswordRequired =
        "identity.ldap_connections.bind_password_required";

    /// <summary>LDAP 预览搜索根 DN 不在白名单范围内。</summary>
    public const string LdapConnectionPreviewSearchBaseOutOfScope =
        "identity.ldap_connections.preview_search_base_out_of_scope";

    /// <summary>OAuth 提供程序不存在。</summary>
    public const string OAuthProviderNotFound = "identity.oauth_providers.not_found";

    /// <summary>OAuth 提供程序并发版本冲突。</summary>
    public const string OAuthProviderVersionConflict =
        "identity.oauth_providers.version_conflict";

    /// <summary>OAuth 提供程序机器码无效。</summary>
    public const string OAuthProviderInvalidKey = "identity.oauth_providers.invalid_key";

    /// <summary>OAuth 提供程序机器码已存在。</summary>
    public const string OAuthProviderKeyExists = "identity.oauth_providers.key_exists";

    /// <summary>OAuth 提供程序元数据无效。</summary>
    public const string OAuthProviderInvalidMetadata =
        "identity.oauth_providers.invalid_metadata";

    /// <summary>创建 OAuth 提供程序时缺少客户端密钥。</summary>
    public const string OAuthProviderClientSecretRequired =
        "identity.oauth_providers.client_secret_required";

    /// <summary>OAuth 提供程序未启用。</summary>
    public const string OAuthProviderDisabled = "identity.oauth_providers.disabled";

    /// <summary>OAuth 授权模式无效。</summary>
    public const string OAuthInvalidMode = "identity.oauth.invalid_mode";

    /// <summary>OAuth 回调 returnUrl 无效。</summary>
    public const string OAuthInvalidReturnUrl = "identity.oauth.invalid_return_url";

    /// <summary>OAuth 外部主体已绑定其他用户。</summary>
    public const string OAuthAccountConflict = "identity.oauth.account_conflict";

    /// <summary>OAuth 用户绑定不存在。</summary>
    public const string OAuthUserLinkNotFound = "identity.oauth.user_link_not_found";

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
        LoginNotLocked,
        UnlockInactiveUserRejected,
        HostUserAlreadyRetired,
        HostUserActiveTenantMemberships,
        PasswordChangeRequired,
        SuperAdministratorImportRejected,
        RoleCodeExists,
        RoleNotFound,
        RoleSystemLocked,
        RoleCopySourceNotAllowed,
        RoleHasMembers,
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
        OidcClientNotFound,
        OidcClientIdConflict,
        OidcClientDisabled,
        OidcAuthorizationNotFound,
        OidcSigningKeyNotActivatable,
        OpenAccessClientNotFound,
        OpenAccessClientVersionConflict,
        OpenAccessClientQuotaExceeded,
        OpenAccessClientSignatureDebugInvalid,
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
        UserProfileInvalid,
        ProfileMaskedValueRejected,
        CurrentPasswordInvalid,
        SelfServiceProfileHostOnly,
        SelfServiceProfileReadOnlyFieldRejected,
        SelfServiceProfileMediaInvalid,
        SelfServiceProfileMediaNotFound,
        NewPasswordSameAsCurrent,
        UserPhoneNumberExists,
        UserEmailExists,
        UserEmployeeNumberExists,
        UserIdCardExists,
        RegistrationPolicyNotFound,
        RegistrationPolicyVersionConflict,
        PublicRegistrationDisabled,
        RegistrationDisabled,
        AccountChallengeInvalid,
        AccountChallengeAttemptsExceeded,
        AccountChallengeDeliveryFailed,
        RegistrationInvitationInvalid,
        RegistrationEmailAlreadyExists,
        RegistrationWayNotFound,
        RegistrationWayVersionConflict,
        RegistrationWayInvalidCode,
        RegistrationWayCodeExists,
        RegistrationWayTenantInactive,
        RegistrationWayRoleNotFound,
        RegistrationWayOrganizationUnitNotFound,
        RegistrationWayPositionNotFound,
        TenantMemberNotFound,
        TenantMemberVersionConflict,
        TenantMemberRoleInvalid,
        TenantOwnerProtected,
        TenantMemberAlreadyActive,
        TenantMembershipTenantInactive,
        TenantInvitationInvalid,
        TenantInvitationNotFound,
        TenantSeatQuotaUnavailable,
        LdapConnectionNotFound,
        LdapConnectionVersionConflict,
        LdapConnectionScopeAlreadyConfigured,
        LdapConnectionTenantInactive,
        LdapConnectionInvalidSyncSearchBase,
        LdapConnectionInvalidMetadata,
        LdapConnectionBindPasswordRequired,
        LdapConnectionPreviewSearchBaseOutOfScope,
        OAuthProviderNotFound,
        OAuthProviderVersionConflict,
        OAuthProviderInvalidKey,
        OAuthProviderKeyExists,
        OAuthProviderInvalidMetadata,
        OAuthProviderClientSecretRequired,
        OAuthProviderDisabled,
        OAuthInvalidMode,
        OAuthInvalidReturnUrl,
        OAuthAccountConflict,
        OAuthUserLinkNotFound,
    ]);
}
