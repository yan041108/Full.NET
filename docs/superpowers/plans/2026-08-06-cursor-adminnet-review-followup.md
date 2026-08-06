# Cursor 后续开发执行计划：Admin.NET 模块吸收收口与下一波

> 本计划供 Cursor 直接顺序执行。每个 Task 独立 snapshot、独立提交；前一个 Task 的 shared runner、Docker 与残留全部为 0 后才能开始下一个。只开发 `ui/admin`，禁止修改或扩展 `ui/admin-layui`。

## 总体约束

1. 以 Admin.NET 的模块能力、表字段和成熟交互为参考，不复制源码、前端资源、动态程序集任务、通用 Repository、物理文件 URL 或存储路径。
2. Full.NET 继续使用模块化单体、Dapper 显式 SQL、标准 HTTP/ProblemDetails、SQL Server/MySQL 成对迁移和 Vue 单一后台线。
3. 每个页面和每个调用受保护 API、读取敏感数据或产生副作用的按钮使用独立稳定权限码；Vue 无权限时不创建入口，直接绕过客户端必须由对应 Endpoint 返回 `403 authorization.permission_denied`。
4. 行为变更先 RED 后 GREEN。迁移号不得预留猜测；开始 Task 时重新列出两库最新迁移，发现占用立即停下协调。
5. 每个列表必须是真实服务端 `page/pageSize/total`；禁止固定取前 20 条后在浏览器伪分页。
6. 每个 Task 完成后运行 snapshot affected inner/slice、Unit、Architecture、OpenAPI、client-contracts、Vue、naming、SQL safety、governance、`git diff --check`，并确认 Docker/runner residual 为 0。测试数量只更新 `eng/testing/test-matrix.json`。

## Task 0（P0）：关闭当前 Cursor 工作区的合并门禁

建议 snapshot：`cursor-adminnet-wip-stabilization-20260806`。

### 0A. 用户档案字段级授权与安全更新

- 把 `fn_identity_user_profile` 的每个可读取敏感字段加入稳定字段投影目录，并按隐私等级设置默认不可分配/可分配策略；身份证号、紧急联系人、地址等不得因拥有 `identity.users.read` 自动可见。
- 列表、详情、导出使用同一 effective projection；禁止先查全量再在内存中掩码。
- 替换当前全对象 PUT 覆盖语义：采用显式字段掩码或窄 Patch 契约，只允许写入调用者被授权管理的字段；不可见字段保持原值。
- 为“查看档案/管理档案”选择稳定权限或字段授权组合，并接入角色授权树。当前超级管理员专属门禁只能在完整字段授权上线前保留为安全兜底，不能作为最终产品语义。
- Vue 只渲染有效字段，搜索条件和表格列也按字段授权移除；覆盖普通角色、部分字段角色、超级管理员、重复 Claim、导出和绕过 API。

### 0B. Host 菜单权限选项自包含

- 删除 `packages/client-contracts/src/host-menus.ts` 中手写的 `HOST_MENU_ASSIGNABLE_PERMISSIONS` 产品清单。
- 在 Identity 菜单用例下增加只读 permission-options Endpoint，从服务端 `AuthorizationCatalog` 投影 Host 可分配权限，并排除超级管理员管理等受保护权限；使用菜单页面自己的精确读取权限，不依赖其他模块页面权限。
- 返回稳定 code、module/page/action 分组和可本地化名称键；Vue 分组搜索选择，未知/已退役权限在编辑旧数据时给出明确只读提示。
- 测试至少覆盖 Document、Jobs、Notifications、Settings 权限能够选择，受保护权限不能选择，以及伪造未登记权限在后端失败。

### 0C. 用户组织/职位操作权限与部分成功

- 为用户页“分配机构”“分配职位”建立独立稳定权限码、授权目录、菜单按钮、Endpoint 保护和迁移；不得继续复用 `identity.users.update`。
- 读取参考数据保持窄接口边界，不允许 Organization 直接访问 Tenancy 或 Identity 物理表。
- 对创建/编辑用户后的多步骤保存制定明确协议：优先设计一个受控编排用例；若仍使用多 Endpoint，响应必须返回逐步骤已提交状态，Vue 提供幂等重试，不得把部分成功显示为完全失败。
- 补 OpenAPI/client-contracts、SQL catalog、双库权限拒绝、乐观并发、租户隔离和真实栈测试。

### 0D. 当前 WIP 最终验收

- 两库验证 `082_IdentityUserProfile` 首次迁移、二次执行、恢复、外键和数据保留。
- 两库验证自定义菜单父环、系统菜单保护、目录种子同步幂等和导航投影。
- 运行 `pnpm test:integration:affected -- --snapshot cursor-adminnet-review-20260806 --phase slice`；SQL Server/MySQL 必须非零发现。
- 完整 Unit fresh discovery 必须等于矩阵门槛，不能仅 `--no-build` 复用旧程序集。
- Task 0 未全部通过前，工作区不得提交为 merge candidate，也不得开始新增模块。

## Task 1（P0）：Document 上传引用 claim/release 对账

建议 snapshot：`document-upload-reference-reconciliation-20260806`。

- 在 Files.Contracts 定义窄的 opaque claim/release 或 reference-reader 契约，不暴露 ProviderKey、StorageKey、物理路径。
- 区分确定回滚、确定提交、提交结果未知三种状态。只有确定回滚可同步释放；提交未知保留对象，在 grace period 后由对账器查询 Document 引用并决定保留或清理。
- 对账必须覆盖 committed-but-threw、definite rollback、worker-won、重复重试和 referenced Blob 永不删除；保持 Files 现有 Pending→Publishing→Ready 不变量。
- Document 权限只使用 `document.host_documents.add_version` 与 `download`，不得要求宽泛 Files 上传/下载权限。
- 双库、Worker、权限绕过、真实 API/Vue E2E 与 teardown 全部通过后才可关闭。

## Task 2（P0）：Jobs 计划预览权限与编辑闭环

建议 snapshot：`jobs-schedule-preview-authorization-20260806`。

- 新增稳定 `jobs.schedules.preview` 操作权限，加入授权目录和 Vue 按钮；create/update 用户按明确迁移策略获得兼容授权，禁止把 preview 授给仅 read 角色。
- Cron preview Endpoint 只接受该权限。保留本轮“取消传播、仅捕获预期输入异常”的修复并增加 Unit/API 测试。
- 证明 create-only、update-only 经授权后均可预览；无 preview 权限直接 API 为 403，Vue 不创建入口。
- 补齐计划创建/编辑真实栈、IANA 时区、Cron/one-time、misfire、分页、并发冲突和两库恢复。

## Task 3（P1）：Notifications 公告类型、受众与发布/撤回状态机

建议 snapshot：`notifications-announcement-lifecycle-20260806`。

- 参考 Admin.NET 公告/通知语义，落地 `Kind`、`AudienceKind`、draft/published/retracted、发布/撤回操作者与时间；用户/机构受众使用规范化子表，不复制显示名作为权威。
- update 仅允许 draft；publish/retract 是幂等、独立权限、affected-row 失败关闭的显式状态迁移。
- Vue 提供分页过滤、受众选择/摘要、状态反馈和危险操作确认；正文安全渲染，不引入未经批准的 HTML 富文本执行。
- 迁移号开始时现场确认，完成双库恢复、审计、OpenAPI/contracts、Vue/E2E。

## Task 4（P1）：站内信收件人授权选择器

建议 snapshot：`notifications-inbox-recipient-picker-20260806`。

- 通过 Identity.Contracts 的窄分页候选投影搜索启用用户；禁止把原始 UUID 文本框作为主交互，也禁止引用 Identity 实现项目。
- 服务端拒绝空、重复、禁用和跨作用域收件人；发送前显示人数和确认。
- Inbox 使用服务端分页和状态/未读过滤；mark-one、mark-all 只有真实独立副作用时才建立各自权限和按钮。

## Task 5（P1）：SerialNumbers 规则管理体验

建议 snapshot：`serial-number-rule-vue-parity-20260806`。

- 服务端分页并支持 key/name/scope/reset interval/status 过滤。
- Vue 表单展示 Pattern、ResetInterval、Min/Max、DisplayOrder、Scope、enabled，提供内联校验、示例和真实 UTC 日期时间输入。
- Preview 返回渲染值、reset bucket、next sequence，但不得修改计数器；规则行不得复制当前序列状态。

## Task 6（P1）：Jobs 执行历史与只读健康页

建议 snapshot：`jobs-execution-history-health-20260806`。

- 分页历史按 definition/schedule/status/time range 查询；详情只展示安全错误码、attempt、耗时、next retry 和 correlation，不回传异常正文或秘密。
- 健康页只展示 allowlisted handler、低基数 backlog 和 Worker heartbeat，复用 typed registry；禁止 AssemblyName、任意类型或脚本执行。
- retry/cancel 没有批准状态机时先不做按钮；一旦做，必须独立权限、affected-row 不变量和双库测试。

## Task 7（P2）：Files 目录分页和安全预览

建议 snapshot：`files-catalog-safe-preview-20260806`。

- 支持 original name/content type/provider/status/time range 的服务端过滤分页。
- 永不返回 StorageKey、物理路径或永久公开 URL；预览按安全 MIME 与大小白名单，其他内容走认证下载。
- 保留实际 SizeBytes、Provider、Pending/Publishing/Ready、补偿和删除 affected-row 既有不变量。

## Task 8（P2）：CodeGeneration 模板类型化编辑

建议 snapshot：`codegeneration-template-editor-20260806`。

- 服务端分页和 name/owner/module/entity 过滤。
- 稳定 Schema 字段使用类型化表单；高级 JSON 模式保留严格校验。更新前展示规范化 diff 与校验报告。
- 版本冲突保留用户草稿并允许 reload/compare；不得在浏览器执行生成代码或暴露含密钥产物。

## Task 9（P2）：Document 完整插件能力分片推进

严格按 9A→9B→9C→9D 独立 snapshot、迁移和提交：

1. 9A Core library：分类树、标签分配、版本说明、回收站 restore/purge。
2. 9B Sharing：高熵 token、密码哈希、到期、访问次数、view/download capability 和限流。
3. 9C ACL：用户/机构/角色规范化授权与 view/download/edit/manage 动作。
4. 9D Preview/audit/statistics：安全预览适配、不可变操作日志和有界聚合。

不得在一个提交中实现全部子任务；每个子任务先更新并评审 Document spec，再进入实现。

## 可直接粘贴给 Cursor 的首个指令

```text
执行 docs/superpowers/plans/2026-08-06-cursor-adminnet-review-followup.md 的 Task 0，且只执行 Task 0。

先读取 AGENTS.md、rules/README.md、development-quality、code-comments、naming-conventions、client-frontend 和 fullnet-module-delivery。记录 HEAD；基于当前脏工作区创建 snapshot cursor-adminnet-wip-stabilization-20260806，保留所有既有改动。先写 RED，再完成 0A→0B→0C→0D。只开发 ui/admin，禁止修改 ui/admin-layui。不得开始 Notifications、Document 新能力或其他后续 Task。

重点验收：用户档案逐字段授权且不可见字段不会被 PUT 清空；菜单权限选项来自服务端授权目录而非硬编码；分配机构/职位有独立按钮和 Endpoint 权限；SQL Server/MySQL 082、自定义菜单、组织关系均有非零测试发现；最后运行同一 snapshot affected slice、完整 Unit fresh discovery、Architecture/OpenAPI/client-contracts/Vue/naming/sql-safety/governance/git diff --check，并确认 runner/Docker residual=0。任何一项阻断就停止提交，如实报告。
```
