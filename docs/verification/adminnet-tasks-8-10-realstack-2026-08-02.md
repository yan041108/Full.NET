# Admin.NET 吸收 Tasks 8–10 真实栈验收记录（2026-08-02）

## 结论

在真实 API Host + Testcontainers 双库环境下，请求签名认证（Task 8）、出站调用审计（Task 9）与只读模块目录（Task 10）均已获得新鲜、可定位的自动化证据。三项能力**维持 `Build-verified`**，不升级为 `Verified`：OpenAccess 产品化、生产多副本出站审计保留清理所有权、签名/出站/模块目录专属真实栈浏览器 E2E 仍属明确缺口。

## 验收边界

- 工作区基线：`33a5e99`（含未提交 Task 1/2 硬化改动；本验收不扩展产品功能）
- 任务快照：`adminnet-tasks-8-10-realstack-20260802`
- Task 8：覆盖规范化、空/非空正文、并发重放、密钥轮换/禁用、租户不匹配、失败审计脱敏、请求体上限与损坏 KeyHash 失败关闭；**不**覆盖 OpenAccess 产品化
- Task 9：覆盖 043 索引恢复、探针写入、分页查询、权限拒绝、敏感载荷不落库、出站类别保留清理；多副本清理无所有权证据时保持 `Build-verified`
- Task 10：覆盖权限拒绝、只读列表/详情、OpenAPI 契约；Architecture 扫描生产代码禁止 Roslyn/ApplicationPart 运行时动态加载

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| `Host_signature_authentication_follows_contract_with_sql_server` / `mysql` | **2/2** |
| `Host_outbound_call_log_query_follows_contract_with_sql_server` / `mysql` | **2/2** |
| `Host_module_catalog_follows_contract_with_sql_server` / `mysql` | **2/2** |
| `Migration043AuditingOutboundCallRecoveryTests`（双库 × 错误聚集索引恢复） | **2/2** |
| `Audit_retention_deletes_expired_rows_in_fair_batches_with_sql_server` / `mysql`（含 Outbound 类别） | **2/2** |
| `TrustedProxyForwardingApiSqlServerTests` / `MySqlTests`（签名失败审计源地址） | **2/2** |
| `DependencyRulesTests` + `HostModuleProfileTests`（禁止 Roslyn/ApplicationPart 等运行时动态加载） | **35/35** |
| `pnpm test:openapi` | **69/69** |

### Task 8 覆盖要点（`IdentitySignatureAuthenticationAssertions`）

- 查询串规范化与乱序无关
- 空/非空正文与篡改拒绝
- 过期/未来时间戳、Nonce 重放与并发重放
- Host Key 禁止绑定租户头、租户 Key 禁止跨租户
- 失败审计不泄漏 Secret/签名材料
- 密钥轮换/禁用/过期
- 请求体硬上限、重复签名头、损坏 `KeyHash` → `access_key_disabled`（工作区 Task 2 硬化）

### Task 9 覆盖要点（`AuditingOutboundCallAssertions` + 043 + Retention）

- 列表 API `auditing.outbound_call_logs.read` 权限拒绝（403 + `authorization.permission_denied`）
- Testing 探针 opt-in 写入、分页查询、详情 404、contains 时间边界
- 敏感 Cookie/正文不落库
- 043 在错误索引形状下无损恢复数据与正确索引
- 保留清理公平批次删除过期 Outbound 行（双库）

### Task 10 覆盖要点（`IdentityModuleCatalogAssertions`）

- 列表/详情需 `identity.modules.read`；无权限 403
- 快照条目 ≥10、无 Roslyn/路径泄漏字段
- OpenAPI 契约与 Architecture 禁止动态编译/ApplicationPart 变更

## 双管理端与真实栈浏览器

Vue/Layui 已存在出站调用日志与模块目录页面、路由与 `@fullnet/client-contracts` 导航登记；**本轮未新增** `admin-real-stack` 专属 Playwright 用例，也未重跑完整 `pnpm test:e2e:real`。双端 UI 行为仍以 API Integration + OpenAPI + 既有客户端单测矩阵间接约束。

## 未关闭缺口（故不标 `Verified`）

1. **OpenAccess**：签名认证开放接口产品化仍属 `Mapped`，不在 Task 8 范围。
2. **出站审计**：非默认全量拦截；生产多副本保留清理所有权与更广真实栈浏览器矩阵未在本轮执行。
3. **模块目录**：动态插件/热替换明确拒绝；许可与健康深度字段、专属真实栈 E2E 未补。
4. **Task 1/2 未提交**：052 权限码规范化与签名硬化仍在工作区，未进入 `main` 提交历史。

## 治理复盘

未命中规则或 Skill 升级触发条件；一行结论：无需规则/Skill 变更。