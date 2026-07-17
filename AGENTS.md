# Full.NET 仓库开发规则

本文件适用于仓库根目录及全部子目录，是开发代理进入 Full.NET 后必须读取的项目入口。详细规则位于 [`rules/`](rules/README.md)。

## 指令优先级

1. 系统、开发者和当前用户指令始终高于本文件及 `rules/`。
2. 子目录若存在更具体的 `AGENTS.md`，仅对该子目录追加或收紧约束；冲突时服从更高层级规则。
3. 规则不能扩大任务授权。涉及发布、外部写入、破坏性操作或范围外变更时，仍须取得相应授权。

## 每项任务必须执行

### 开始前

1. 必须读取本文件和 [`rules/README.md`](rules/README.md)。
2. 必须读取 [`rules/development-quality.md`](rules/development-quality.md)；涉及代码、SQL、配置或脚本时，还必须读取 [`rules/code-comments.md`](rules/code-comments.md)。
3. 必须检查 `.agents/skills/` 是否存在匹配当前任务的项目 Skill；新增或扩展模块、CRUD、Endpoint、Command/Query、Dapper 持久化或双库迁移时必须使用 [`fullnet-module-delivery`](.agents/skills/fullnet-module-delivery/SKILL.md)。
4. 必须检查当前分支、`git status`、相关设计与计划，保留用户已有和无关变更。
5. 必须确认需求、授权边界和验收条件；能从仓库安全确定的信息不得反复询问。

### 开发中

1. 行为变更和缺陷修复必须先建立可失败的验证，再实现最小正确变更。
2. 必须保持模块边界、租户隔离、数据一致性以及 SQL Server/MySQL 双提供程序约束。
3. 代码标识符使用英文；所有手写源代码注释（包括 XML 文档注释）必须使用清晰中文，专业术语可保留英文。
4. 注释必须解释意图、边界、不变量或风险，禁止逐行复述代码。
5. 不得静默更改公共 API、序列化契约、数据库结构、兼容适配器或许可证边界。

### 完成前

1. 必须执行与风险相称的构建、测试和静态检查，并依据新鲜输出报告结果。
2. 必须同步更新受影响的 README、开发文档、路线图、迁移说明和测试数量门槛。
3. 必须读取并执行 [`rules/rule-evolution.md`](rules/rule-evolution.md) 的遗漏复盘；满足升级门槛时，在同一任务中更新相应规则并在交付说明中披露。
4. 规则复盘后必须读取并执行 [`rules/skill-evolution.md`](rules/skill-evolution.md) 的 Skills 复盘；满足门槛时更新候选或按测试先行流程演进一个项目 Skill。
5. 必须检查 `git diff --check`、`git status` 和分支状态，不得把“测试未执行”表述为“测试通过”。

## Full.NET 不可隐式改变的基线

- 数据访问以 Dapper 和显式 SQL 为默认实现；未经明确架构决策不得引入 EF Core 作为业务数据访问层。
- 数据库正式支持 SQL Server 与 MySQL；数据库行为变更必须同时验证两者。
- 外部 HTTP API 使用标准状态码与 ProblemDetails；Admin.NET 统一包络只存在于兼容适配层。
- JSON 使用 System.Text.Json；内部高性能序列化按既定边界使用 MessagePack，服务契约可使用 gRPC。
- 缓存以 FusionCache 为唯一实现，并通过 `.AsHybridCache()` 同时暴露 HybridCache 抽象。
- 后续功能以 Admin.NET 为功能参考目标，但实现必须遵守 Full.NET 的架构、安全和发布许可边界。

## 详细规则索引

- [`rules/README.md`](rules/README.md)：规则范围、用词和维护方式。
- [`rules/code-comments.md`](rules/code-comments.md)：中文代码注释与文档注释规范。
- [`rules/development-quality.md`](rules/development-quality.md)：常见遗漏防护和完成定义。
- [`rules/rule-evolution.md`](rules/rule-evolution.md)：自动复盘、规则升级、冲突与退役机制。
- [`rules/skill-evolution.md`](rules/skill-evolution.md)：项目 Skills 候选、测试先行、升级与退役机制。
- [`.agents/skills/fullnet-module-delivery`](.agents/skills/fullnet-module-delivery/SKILL.md)：完整业务模块纵向交付流程。
