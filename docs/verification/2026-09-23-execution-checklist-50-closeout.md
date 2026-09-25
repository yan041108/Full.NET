# 执行清单 50 — OAuth/OIDC Provider 登录回调与绑定 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **50**（首个外部 OAuth/OIDC Provider 的授权跳转、回调、登录会话与「我的」绑定/解绑；**不**按未验证邮箱自动合并账号）。

## 交付锚点

| 层 | 位置 |
|----|------|
| Provider 管理 | `/api/v1/identity/oauth-providers`；`OAuthProvidersView`；`OAuthClientSecretProtector`（响应无 `ClientSecret`） |
| 公开目录 | `GET /api/v1/identity/oauth/providers`；`LoginView` 第三方登录按钮 |
| 授权跳转 | `GET /api/v1/identity/oauth/{providerKey}/authorize?mode=login|bind`；`OAuthFlowService.BeginAuthorizeAsync`（**state**、**nonce**、**PKCE** `code_verifier`/`code_challenge` 落库） |
| 回调 | `GET /api/v1/identity/oauth/callback`；`HttpOidcClient.ExchangeCodeAsync`（校验 nonce）；`OAuthCallbackView` |
| 登录语义 | **仅**已绑定 `fn_identity_oauth_user_link` 可登录；无链接 → `oauth_account_not_linked`（**不**按邮箱自动建号/合并） |
| 绑定/冲突 | bind 模式需已登录；他人已占用同一 `subject` → `oauth_account_conflict` |
| 我的链接 | `GET/DELETE /api/v1/identity/me/oauth-links`；`SecuritySettingsView` 绑定/解绑 |
| 契约 | `identity-oauth-providers-v1` / `identity-oauth-links-v1` OpenAPI + node 契约测试 |
| 数据 | 迁移 151（provider/link/state 表）；152 动作权限（OAuth 管理） |

## 清单 50 验收结论

- **已有**：PKCE + 一次性 state；returnUrl 白名单；login/bind 分模式；邮箱仅作链接元数据存储，不参与自动合并。
- **本槽**：`phase-c-50-oauth-provider-callback-links.spec.mjs`（公开目录、缺失 Provider/无效回调 fail-closed、ClientSecret 不回显、`me/oauth-links`、管理页与安全设置 OAuth 区）。
- **未验**：真实 IdP（Azure AD/Google 等）完整 authorization code 与 id_token 验签链路（需厂商实验室）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`OAuthProvider`…`OAuthPkce`…`OAuthReturnUrl` | **4/4**（`d40de4e6`） |
| `node --test` `identity-oauth-providers-contract` + `identity-oauth-links-contract` | **4/4** |
| real-stack | `phase-c-50-oauth-provider-callback-links.spec.mjs`（需本地 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
