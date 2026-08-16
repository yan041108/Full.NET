# CodeGeneration Admin.NET 对标与 Identity B1 验证记录

- 日期：2026-08-16
- 基线：`main` @ `31bec6a2`
- 任务快照：`b1-codegen-parity-20260816`
- 规格：[2026-08-16 CodeGeneration Admin.NET 对标设计](../superpowers/specs/2026-08-16-codegeneration-adminnet-parity-design.md)
- 计划：[2026-08-16 实施计划](../superpowers/plans/2026-08-16-codegeneration-adminnet-parity.md)

## 结论

Full.NET 已按能力和用户流程对标 Admin.NET.Pro v2.1 核心「代码生成」：Host 只读表目录、可视化任务/列配置、精确操作权限 Vue SFC、Host Apply 显式模块/Composition/Vue 接线、鉴权 zip 下载，以及同模块 Tree / MasterDetail / ManyToMany 可执行生成。Identity B1 补齐导入、批量停用/启用，并增加「撤销最后一名超级管理员」Vue 真实栈规格。状态保持 **Build-verified**，未跑 `test:e2e:real`，不得标 `Verified`。

DatabaseTools、ReZero、运行时 DDL、公开 zip URL 与 Layui 新功能仍按规格拒绝。

## 交付边界

- 生成任务继续使用 `fn_codegeneration_template`，未新建 Job 表或业务 `.csproj`。
- Host 表目录只扫描当前进程已配置库，权限 `codegen.catalog.read`，SQL 为 `HostOnly`。
- 新生成权限为 `{module}.{resource}.{read|create|update|disable}`；Host 预览默认不再发出 `layui_client`，改为 `vue_view`。
- 下载：`GET /api/v1/code-generation/runs/{id}/artifacts.zip`，权限 `codegen.runs.download`。
- Identity：`POST /api/v1/identity/users/import`（`identity.users.import`，拒绝超级管理员）、`/batch-disable`、`/batch-enable`。

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| `pnpm test:inner -- --snapshot b1-codegen-parity-20260816` | **39/39**（CodeGeneration + Identity MySQL 聚焦 + 已登记迁移恢复） |
| `pnpm test:slice -- --snapshot b1-codegen-parity-20260816` | **125/125** 双 Provider |
| CodeGeneration 单元测试 | **300/300**（一次 Windows 检查点 Move 偶发已用短重试消化） |
| Identity 授权目录/树单元测试 | **41/41** |
| Vue：工作台、预览、用户导入/批量、目录契约 | **38/38** |
| Integration 分片发现 | api-sqlserver **62**、api-mysql **62**、migrations **306**、infrastructure **126**、messaging-heavy **56**，合计 **612**，无遗漏或重复 |
| 单元测试矩阵 minimum | **1445** |
| `git diff --check` | 无空白错误 |
| `pnpm test:e2e:real` | **未执行**（inner/slice 禁令；最后一名超管规格已写入 `tests/e2e/admin-real-stack/tests/host-super-administrators.spec.mjs`） |

完整 Unit / Integration / 真实栈集合仍由 `main` CI 互斥分片负责。

## 为挡住验证而做的伴随修复

- MySQL `093`：COMMENT 后补分号，避免 `ALTER COLUMN DROP DEFAULT` 被拼进前一条语句。
- SQL Server `047`/`051`：扩展属性移出 `EXEC(N'...')` 字符串。
- 诊断策略写入 `fn_settings_config_entry` 时补齐 `GroupName` 参数与 Dapper 投影。
- infrastructure 分片过滤器排除已划入 messaging-heavy 的 Organization CDC 用例，消除分片重叠。

## 未验证项

- 真实浏览器 CORS/Cookie/Session 与最后一名超管 UI 拒绝路径（需 `test:e2e:real` 才能标 `Verified`）。
- DatabaseTools / 视图维护 / ReZero 仍为 Mapped。
- Unique 列目前按第一个 Unique 列生成单一 `EnsureUniqueAsync`。

## 治理复盘

本次没有命中新的通用规则或 Skill 缺口。注释迁移语法热修与 GroupName 参数遗漏属于既有迁移/SQL 契约的阻塞修复，不扩张规则或 Skill。
