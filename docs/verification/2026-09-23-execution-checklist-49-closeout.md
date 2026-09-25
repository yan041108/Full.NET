# 执行清单 49 — LDAP 连接/认证测试/同步预览 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **49**（LDAP 连接配置、连接与用户认证测试、**只读**同步预览；**不**包含有审计的目录同步应用）。

## 交付锚点

| 层 | 位置 |
|----|------|
| CRUD + 禁用 | `/api/v1/identity/ldap-connections`（`identity.ldap_connections.*`） |
| 连接测试 | `POST …/{id}/test-connection` |
| 认证测试 | `POST …/{id}/test-authentication`（请求体仅本次携带用户密码，响应不回显 Bind 凭据） |
| 同步预览 | `POST …/{id}/preview-sync`；`LdapDnScopeValidator` 白名单；**无** Apply/Sync 写路径 |
| 凭据 | `LdapBindPasswordProtector` + Data Protection；`LdapConnectionResponse` **无** `BindPassword` |
| Vue | `LdapConnectionsView`（CRUD、测试、认证对话框、预览对话框） |
| 契约 | `contracts/openapi/identity-ldap-connections-v1.json`；`tests/openapi/identity-ldap-connections-contract.test.mjs` |
| 数据 | 迁移 149 表 + 150 动作权限恢复（`Migration149*` / `Migration150*`） |
| 目录客户端 | `DirectoryServicesLdapClient` / `ILdapDirectoryClient`（预览只读） |

## 清单 49 验收结论

- **已有**：元数据校验、DN 范围白名单、预览条目上限；默认不同步写入 Identity/Organization。
- **本槽**：`phase-c-49-ldap-connections.spec.mjs`（创建后响应无 `bindPassword`、越界预览 `preview_search_base_out_of_scope`、管理页冒烟）。
- **未验**：真实 AD/OpenLDAP 实例上的 bind、认证与预览条目（需目录实验室；本机 real-stack 未跑）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`LdapConnection`…`LdapDnScope` | **5/5**（`d40de4e6`） |
| `node --test` `identity-ldap-connections-contract.test.mjs` | **2/2** |
| `pnpm test:openapi`（含 identity-ldap-connections-contract） | 历史 175/175；本槽未全量重跑 |
| real-stack | `phase-c-49-ldap-connections.spec.mjs`（需本地 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
