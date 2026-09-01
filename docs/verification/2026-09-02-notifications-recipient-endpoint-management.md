# Notifications 当前用户收件端点管理验证（2026-09-02）

- **任务：** Notifications 当前用户邮箱收件端点登记、查询与删除
- **基线：** `99081317da8ceddabbb7af73c7a04eb390b9328a`（`main`）
- **快照：** `notifications-workflow-continue-20260901`
- **状态：** API/UI **Build-verified**；邮箱验证闭环未交付；容量 **Capacity-not-verified**

## 交付范围

- 新增 `GET/POST/DELETE /api/v1/notifications/my-recipient-endpoints`。用户标识只从认证 Claim 取得，租户作用域只从 `ICurrentTenant` 取得，请求体不能覆盖用户、租户或验证状态。
- 登记只接受当前作用域内已启用 Profile 的最新发布版本，并要求闭合 Provider Adapter 声明相同端点类型；跨作用域、旧版本、未知 Provider 或端点类型不匹配均失败关闭。
- 原始邮箱在落库前经 Data Protection 保护；列表和登记响应只返回掩码，不投影 `RawValue` 或 `ProtectedValue`。
- 当前用户入口把新端点固定保存为 `pending`；Delivery Worker 仍只消费 `verified`，因此本切片不会把未验证地址伪装成可投递地址。
- Vue 通知偏好页只列出当前作用域已发布并启用的 SMTP Profile，支持登记、刷新、双击确认删除，并明确提示待验证端点不会参与真实投递。
- OpenAPI manifest、冻结契约和生成 TypeScript 客户端同步新增 3 个 Operation；Vue API 层只保留生成 Operation 的薄适配。

## 安全与一致性边界

- `PreferencesRead` 只允许查看本人当前作用域端点；`PreferencesUpdate` 独立保护登记和删除。删除条件同时包含 `Id + TenantScopeKey + UserId`，不存在与越权均返回 404。
- 唯一键按 `TenantScopeKey + UserId + ProviderProfileVersionId + EndpointKindKey` 串行检查；重复登记返回稳定 409，不回显原值。
- SQL Server 使用 `UPDLOCK, HOLDLOCK`，MySQL 使用 `FOR UPDATE`；没有新增迁移，也没有跨模块表访问。
- 邮箱格式拒绝 display-name 等歧义形式；当前切片不接受任意渠道原值校验器扩展。

## 自动化证据

| 命令/范围 | 结果 |
|---|---|
| SQL Server 初始 HTTP/OpenAPI RED | 新路径未映射时由运行时 OpenAPI 抛出缺少路径，确认失败来自待实现契约 |
| Vue `NotificationPreferencesView.test.ts` 初始 RED | 旧诚实占位缺少端点管理交互，3 个用例按预期失败 |
| `pnpm test:slice -- --snapshot notifications-workflow-continue-20260901` | **2/2**，SQL Server/MySQL Notifications 聚焦组全部通过；Release 构建 0 警告、0 错误 |
| `pnpm --dir ui/admin test -- NotificationPreferencesView.test.ts` | **3/3** |
| Notifications 真实栈 Playwright（Vue + SQL Server） | **1/1**，空 SMTP 目录下展示端点列表并禁止登记 |
| `pnpm test:openapi` | **122/122** |
| OpenAPI 离线快照与客户端生成 `--check` | 两项均零漂移 |
| `dotnet build Full.NET.slnx -c Release --no-restore` | 0 警告、0 错误 |
| `pnpm --dir ui/admin typecheck` | 通过 |
| `pnpm --dir ui/admin build` | 通过；仍报告既有 VForm3 `eval` 与大包警告，本切片未新增该依赖 |
| `pnpm test:aot:analyzers` | Host.Api 分析构建通过，0 警告、0 错误 |
| `pnpm test:aot:worker:analyzers` | Worker 分析构建与 JIT Rebuild 通过，0 警告、0 错误 |
| Notifications + Native AOT 聚焦 Architecture | **51/51** |
| `pnpm test:localization` | **7/7** |
| `pnpm test:governance` | **52/52** |

`test:inner` 对该快照正确返回无额外 inner 目标；纵向关闭由同一快照的 `test:slice` 双库聚焦组完成。完整 Integration 集合仍只由 `main` CI 的测试矩阵执行。

## 未完成边界

- 未实现邮件验证码的生成、发送、校验、限流、过期、重放防护与自动升级 `verified`；这应作为下一条独立纵向切片。
- 未重新执行 QQ SMTP 外部账号认证；已有结论仍为 **External-auth-not-verified**，不能声明 QQ 邮件已发送或被服务器接受。
- 未执行 Linux Native AOT publish/原生进程端点 E2E，不标记 `Aot-published` 或 `Native-provider-verified`。
- 未执行负载、容量、退信、送达回执、多账号矩阵或生产密钥托管认证，保持 **Capacity-not-verified**。
- 静默时段、营销同意、短信、企微、公众号和钉钉端点不在本切片范围。

本任务未触发新的规则或 Skill 演进。
