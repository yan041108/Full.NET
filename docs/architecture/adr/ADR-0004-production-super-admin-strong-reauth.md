# ADR-0004：Production 远程超管强认证与 TOTP Provider

- 状态：已批准
- 日期：2026-07-21
- 决策者：项目所有者在当前任务中明确确认（按计划交付）
- 适用范围：Identity 远程超级管理员授予/撤销、Production 配置门禁、强认证 Provider 扩展点
- 正式规格：[超级管理员设计](../../superpowers/specs/2026-07-18-super-administrator-design.md)

## 背景

远程超级管理员授予/撤销属于平台级高风险写操作。第一阶段仅允许 Development/Testing 在显式配置下以当前密码重认证开放；配置验证器在 Production 一律拒绝开启，避免“只改配置”绕过安全边界。

要在 Production 开放该入口，必须同时具备可验证的第二因子与明确的架构决策，而不能把密码重认证伪装成强认证。

## 候选方案

### 方案一：仅抽出 Provider 缝合，Production 继续关闭

能统一 Dev/Test 与未来因子接口，但不交付合格第二因子，无法关闭生产可控性缺口。

### 方案二：TOTP（RFC 6238）作为 Production 合格强认证因子（采用）

自实现 TOTP（HMAC-SHA1、30 秒步长、6 位、±1 窗口），密钥经 ASP.NET Core Data Protection 加密后持久化到 `fn_identity_user_totp`。不引入第三方 OTP 包，以降低许可与供应链审查成本。

### 方案三：WebAuthn / 短信 OTP

安全与可用性更强或不同，但登记、浏览器依赖与运维复杂度显著更高，不适合作为关闭当前 P0 缺口的第一切片。

## 决策

1. **Production 解锁三条件（缺一不可）**
   - `Identity:EnableRemoteSuperAdministratorManagement=true`；
   - `Identity:EnableTotpStrongReauthentication=true`（注册 Production 合格 TOTP Provider）；
   - 操作者 Host 账号已确认启用 TOTP，且请求携带当前密码与有效 TOTP 验证码。
2. **禁止**仅通过修改配置或放宽验证器消息绕过上述条件。
3. Development/Testing 可继续使用仅密码的重认证 Provider（`IsProductionEligible=false`）；Production 运行时若 Provider 不合格，必须拒绝远程写。
4. TOTP 密钥禁止明文落库、进入日志、审计或 ProblemDetails；确认前以受保护密文暂存，`IsEnabled=false`，确认成功后启用。
5. 双管理端登记/确认 UI 与真实栈 E2E 可后置；本 ADR 批准后端门禁与 TOTP Provider，不授权将能力标记为完整 `Verified`。

## 后果

- `IdentityOptionsValidator` 在 Production 开启远程写时，必须同时看到 TOTP 强认证开关。
- 授予/撤销契约可携带可选 `totpCode`；Production 路径下缺失或错误码返回稳定机器码。
- 后续若引入 WebAuthn 等因子，须新增 ADR 或修订本 ADR，并保持同一 `IStrongReauthenticationProvider` 扩展点。
