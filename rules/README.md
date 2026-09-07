# Full.NET 项目规则索引

`rules/` 存放可执行、可审查、可持续演进的项目规则。根目录 [`AGENTS.md`](../AGENTS.md) 是自动加载入口；本目录不替代更高优先级指令，也不扩大任务授权。

## 按任务读取

所有任务先读根入口与本索引；相关内容已在上下文中且未变化时直接复用。以下是读取路由，不要求通读 `development-quality.md` 或递归打开每个链接。按实际影响读取完整相关章节及其中适用的专项规则；发现共享调用链或风险扩大时追加读取。

| 任务或影响 | 开发质量章节 |
| --- | --- |
| 咨询、只读审查 | [§2 范围](development-quality.md#2-任务开始与范围控制)；再读取被审查领域，不执行实施任务的快照、构建和文档流程 |
| 任何实际修改 | [§2 范围](development-quality.md#2-任务开始与范围控制)、[§11 验证](development-quality.md#11-测试与验证)、[§13 Git](development-quality.md#13-git编码与跨平台) |
| 模块、依赖、DI、宿主或架构 | [§3 架构](development-quality.md#3-架构与模块边界) |
| 认证、授权、租户、上传、代理或敏感数据 | [§4 安全](development-quality.md#4-安全权限与租户隔离) |
| 超级管理员、引导账号或最后一名保护 | [超级管理员边界](development-quality.md#r-20260718-super-administrator-boundary超级管理员是受保护角色不是授权旁路)，并读 §4 |
| SQL、事务、迁移或数据作用域 | [§5 数据](development-quality.md#5-dapper事务与双数据库)；租户过滤同时读 §4 |
| 后台任务、重试、并发、事件或播种 | [§6 一致性](development-quality.md#6-并发重试幂等与-outbox) |
| API、序列化、兼容或多语言 | [§7 契约](development-quality.md#7-api错误与序列化契约)；受保护 API 同时读 §4 |
| Identity.Contracts 或反向跨模块契约 | [Identity 契约边界](development-quality.md#r-20260816-identity-contracts-hub-boundaryidentitycontracts-只允许跨模块稳定契约)，并读 §3 |
| 缓存、实时连接、健康检查或多实例 | [§8 基础设施](development-quality.md#8-缓存实时通信和基础设施) |
| 日志、审计、指标或容量 | [§9 可观测性](development-quality.md#9-日志指标与高并发) |
| AI、Agent 或外部模型数据 | [§10 AI](development-quality.md#10-ai-集成与-agentic-web) |
| 永久设计/计划/验证文档、依赖或发布许可 | [§12 文档与许可](development-quality.md#12-文档依赖与发布许可) |

普通任务的计划可留在会话；永久文档达到 §12.1 门槛才创建。只读审查直接在交付中给出结论，用户请求保存报告时再写文件。

## 专项规则文件

| 文件 | 读取场景 | 责任 |
| --- | --- | --- |
| [`code-comments.md`](code-comments.md) | 新增或修改代码、SQL、脚本、配置 | 规定中文注释的语言、覆盖范围、质量与维护标准 |
| [`development-quality.md`](development-quality.md) | 按上表读取受影响章节 | 防止需求、架构、安全、数据、测试、文档、许可和 Git 遗漏 |
| [`performance-engineering.md`](performance-engineering.md) | 性能分析、基准、负载测试以及请求、SQL、缓存、Worker 或客户端包体优化 | 规定性能证据、语义门禁、低基数指标、双库和尾延迟验证 |
| [`naming-conventions.md`](naming-conventions.md) | 新增或修改数据库对象、公共标识符、API/JSON、稳定机器码、配置/缓存键或生成器产物 | 规定跨 SQL Server/MySQL、Dapper、C# 与多客户端的命名、兼容和验证方式 |
| [`native-aot.md`](native-aot.md) | 修改 Host.Api 可达代码或依赖、AOT 编译条件、JSON/配置源生成、Dapper AOT、Provider native binding、AOT 测试或工作流 | 规定静态闭包、序列化、DI、SQL、第三方依赖、发布状态与原生 E2E 门禁 |
| [`client-frontend.md`](client-frontend.md) | 新增或修改 Vue 管理端、Layui 存量代码、uni-app、Flutter/桌面端或客户端依赖 | 规定 Vue 单一后台交付线、Layui 冻结边界、逐页面/逐操作权限、各端框架、UI、许可与 `Verified` 验收方式 |
| [`rule-evolution.md`](rule-evolution.md) | 用户纠正、重复失败、高风险新类别、规则冲突或里程碑复盘 | 将有证据的遗漏升级为项目规则 |
| [`skill-evolution.md`](skill-evolution.md) | 已有 Skill 出现真实缺口或里程碑集中复盘 | 将重复且稳定的复杂工作流升级为可验证的项目 Skill |

## 规范用词

- **必须**：无例外时强制执行；无法执行必须说明原因和影响。
- **禁止**：不得实施；需要例外时必须先取得明确授权并记录理由。
- **应**：默认执行；偏离时必须有可验证的工程理由。
- **可**：允许选择，不构成默认要求。

模糊措辞不能作为规则，例如“适当处理”“尽量完善”或“视情况测试”。规则必须说明适用条件、期望行为和验证方式。

## 适用与冲突

1. 规则默认适用于整个仓库，包括 `src/`、`tests/`、`benchmarks/`、`docs/`、迁移脚本和 CI 配置。
2. 更具体的规则可以收紧通用规则，但不得静默放宽安全、数据、许可或验证要求。
3. 两条项目规则冲突时，依据更高优先级指令与最近明确决策消解；已有授权足够时继续执行并在范围内修正规则。只有无法判定且会影响安全、数据、公共契约或授权边界时暂停相关部分，其他独立工作继续。
4. 规则与现有实现不一致时，不得假装实现已经合规；必须区分“新增代码要求”和“存量技术债”，在交付中说明。

## 维护原则

1. 每个规则文件只承担一个主题，优先修改既有规则，避免重复追加近义条目。
2. 规则变更必须进入 Git diff，与代码一样接受审查；禁止在交付说明之外静默修改规则。
3. 新规则必须遵循 [`rule-evolution.md`](rule-evolution.md) 的升级门槛，并包含来源、理由和可执行的验证方式。
4. 已失效的规则必须明确退役或由新规则替换，不能保留相互矛盾的历史要求。
5. 每个里程碑结束时应审查规则的重复、冲突、过期和可自动化程度。
6. 未命中演进触发条件时不修改候选，也不要求输出治理状态。强制约束保留在规则中，重复执行方法才进入 `.agents/skills/`。
7. 测试策略由 `development-quality.md` §11 统一维护；入口、Skill 和命令地图只链接该章节。可自动化的选择器与门禁用行为测试验证，文档检查聚焦链接、命令和稳定标识，不锁定整段措辞。
