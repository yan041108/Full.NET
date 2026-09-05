# 架构与代码审查修复验证记录

## 结论

- 状态：`Build-verified`，不提升为 `Verified`。
- Workflow 现在显式依赖 Notifications；裁剪配置不能再启用会发布可靠通知事件、却没有消费模块的组合。
- Notifications 按可信 Host/Tenant 作用域批量解析活动收件人，Tenant 路径不会回退到全局 Host 用户目录，且一次请求只执行一次目录查询。
- Workflow 内建通知模板使用稳定的系统审计主体创建和发布，不再把事件收件人伪装成模板作者。
- 架构测试使用统一的 14 个官方业务模块程序集清单；Global SQL 治理由该清单扫描，并精确登记 60 条 Workflow 全局语句。
- Mermaid 模块依赖图现已由治理测试逐字校验生成结果；缓存文档恢复为 Redis Backplane 与 TTL/版本兜底语义，不再错误声称使用 Outbox 修复缓存失效。

## 兼容性与治理边界

- 未修改 HTTP、JSON、数据库对象或集成事件序列化契约。
- 完整程序集扫描额外发现 59 个 Jobs/Notifications 既有公开错误码不满足三段式命名；为避免静默破坏公共契约，已按文件和值精确登记到 `contracts/naming/naming-debt.json`，移除里程碑为 `M1.0`。
- Workflow 的 60 条 Global SQL 登记均要求所属表及可信 `TenantScopeKey` 或已按作用域定位的父聚合标识；未增加通配豁免。
- API 与 Worker Native AOT 可达路径没有引入反射注册、动态 JSON 或新的 native binding。

## Fresh 本地证据

- `dotnet build Full.NET.slnx -c Release --no-restore`：成功，0 警告、0 错误。
- `pnpm test:dotnet:unit -- --no-build`：1851/1852 通过，1 项仅 Linux 执行的 FIFO 用例跳过，0 失败。
- `pnpm test:dotnet:architecture -- --no-build`：201/201 通过。
- `pnpm test:dotnet:compatibility -- --no-build`：12/12 通过。
- `pnpm test:governance`：52/52 通过。
- `pnpm test:naming`：30/30 通过。
- `pnpm test:sql-safety`：5/5 通过。
- `pnpm test:aot:analyzers`：成功，0 警告、0 错误。
- `pnpm test:aot:worker:analyzers`：成功，0 警告、0 错误。
- `git diff --check`：通过。

## 未验证边界

- `pnpm test:integration:affected -- --base bd0f310ee0477f75b58f8234e7850cf4306e62c7 --phase inner` 选择 Identity/MySQL 15 项后超过 10 分钟无结果，且 MySQL 无活动测试连接；已人工停止，不能表述为通过。
- 未本地执行 SQL Server/MySQL 双库 Workflow→Notifications Worker 全链、Linux Native AOT publish/原生进程 E2E、完整 Integration、真实浏览器或容量验证；这些环境重型门禁应在取得提交与推送授权后由 GitHub Actions 验证。
- 本轮不包含远端推送或 PR；本地提交与合并状态以仓库 Git 日志为准。

## 规则与 Skill 演进

- 规则演进未触发：现有租户隔离、模块闭包、Global SQL 和生成物治理规则足以覆盖，本轮已通过代码与门禁补齐执行缺口。
- Skill 演进未触发：`fullnet-module-delivery` 已覆盖本轮模块、Contracts、Dapper 与验证边界。
