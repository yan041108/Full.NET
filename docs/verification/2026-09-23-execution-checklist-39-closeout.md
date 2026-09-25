# 执行清单 39 — OpenAccess 接入方应用 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **39**（接入方应用 CRUD、密钥轮换/停用、权限绑定与 ApiKey 即时生效；访问日志/配额/签名调试归 **40**）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `/api/v1/identity/open-access-clients`（create、update、rotate、disable） |
| 模块 | `ManageOpenAccessClients`（`Full.NET.Modules.Identity`） |
| 认证 | `ApiKeyAuthenticationService` + 接入方权限交集 |
| Vue | `OpenAccessClientsView.vue` |
| 迁移 | `138`–`139`（表与动作权限，双库） |
| 集成 | `IdentityOpenAccessClientAssertions`（创建/轮换/停用切片） |
| 单元 | `OpenAccessClientManagementServiceTests`、`OpenAccessClientRequestValidationTests` |

## OA01 核验结论（39 子集）

- **已有**：独立于用户 API Key 的接入方应用面；明文密钥仅创建/轮换响应返回一次；轮换与停用后旧密钥立即 401。
- **本槽**：新增 real-stack `host-open-access-clients.spec.mjs`（API + Vue 轮换/停用 + Viewer 403）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~OpenAccessClient`（UnitTests） | **9/9** |
| `dotnet test` …`IdentityOpenAccessClientAssertions` | 本机 Docker 不可用则跳过 |
| real-stack | `host-open-access-clients.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
