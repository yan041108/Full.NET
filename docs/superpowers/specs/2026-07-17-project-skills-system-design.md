# Full.NET 项目级 Skills 体系设计

日期：2026-07-17  
状态：已批准（依据项目所有者“后续自动确认，按推荐方案执行”的授权）

## 1. 目标

在仓库内建立可自动发现、可验证、可持续演进的项目级 Skills，把 Full.NET 中高频、复杂且需要工程判断的开发流程沉淀为可复用能力。Skills 负责“如何可靠完成一类任务”，`rules/` 继续负责“所有任务必须遵守什么”。

## 2. 方案选择

### 方案 A：单一万能 Skill

将模块、数据库、API、缓存、Outbox、测试等全部放入一个 Skill。入口简单，但触发范围过宽、上下文持续膨胀，不利于独立验证和演进。

### 方案 B：一次创建全部细分 Skills

立即创建双数据库、API、Outbox、缓存、测试、SignalR、AI 等 Skills。覆盖面广，但许多后续模块尚未真实实现，容易把假设固化为流程，也无法满足“每个 Skill 先验证再部署”的要求。

### 方案 C：首个纵向 Skill + 候选升级机制（采用）

先创建复用面最大且已有真实代码证据的 `fullnet-module-delivery`。它负责从需求映射到模块纵向切片交付，但把详细仓库地图放入按需加载的 reference。其余工作流进入候选登记，达到复用与稳定门槛后逐个创建、验证和提交。

## 3. 项目级目录

项目 Skills 放在仓库根目录的 `.agents/skills/`，随仓库版本控制，仅在 Full.NET 项目上下文中使用。

```text
.agents/skills/
└── fullnet-module-delivery/
    ├── SKILL.md
    ├── agents/
    │   └── openai.yaml
    └── references/
        └── delivery-map.md
```

不在 Skill 内创建 README、安装指南或变更日志。UI 元数据只包含 `display_name`、`short_description` 和显式引用 `$fullnet-module-delivery` 的 `default_prompt`。

## 4. 首个 Skill 的职责

`fullnet-module-delivery` 在新增或扩展 Full.NET 模块、CRUD、Endpoint、Command/Query、Dapper 持久化、数据库迁移或 Admin.NET 对标能力时触发。它必须指导开发代理完成：

1. 读取仓库规则、架构规格和功能对标矩阵；
2. 判断 Core、Module、Provider、Compatibility、Sample 或 Client 归属；
3. 设计 Contracts、Domain、Features、Persistence、Serialization 与注册边界；
4. 先建立单元、架构、兼容性或双数据库集成测试的失败证据；
5. 使用 Dapper、参数化 SQL、DbUp 和 SQL Server/MySQL 对等实现；
6. 根据真实需要接入事务、Outbox、MessagePack、FusionCache 和失效事件；
7. 对外保持标准 HTTP + ProblemDetails，Admin.NET 包络只通过兼容层；
8. 同步权限、租户、中文注释、序列化、DI、可观测性、文档和测试数量；
9. 运行 Microsoft Testing Platform 的真实测试程序集并报告数量。

Skill 不替代具体模块规格，不负责为未来未出现的 SignalR、AI 或外部 Provider 预建抽象。

## 5. 渐进式候选矩阵

| 候选 Skill | 复用预期 | 当前证据 | 本轮决策 |
| --- | --- | --- | --- |
| `fullnet-module-delivery` | 很高 | Tenancy 纵向切片与后续 Identity/Organization 路线明确 | 立即创建并验证 |
| `fullnet-dual-database-change` | 高 | DbUp、Dapper、SQL Server/MySQL 已有真实实现 | 先作为候选；出现下一项独立数据变更时评估拆分 |
| `fullnet-outbox-event-delivery` | 中高 | Tenancy 事件已使用 MessagePack Outbox | 先作为候选；第二个业务事件落地时升级 |
| `fullnet-api-compatibility` | 中高 | ProblemDetails 与 Admin.NET 适配器已有实现 | 先作为候选；兼容端点类型增加时升级 |
| `fullnet-cache-feature` | 中 | FusionCache 双抽象与失效处理已有实现 | 先作为候选；出现第二种缓存模型时升级 |
| `fullnet-release-verification` | 高 | 构建与四套测试命令重复运行 | 优先自动化为脚本或 CI，不先创建判断型 Skill |
| `fullnet-realtime-feature` | 未来高 | 仅有设计与路线图，没有真实模块 | 等首个 SignalR 消费者完成后评估 |
| `fullnet-agentic-feature` | 未来高 | 仅有架构约束，没有真实模块 | 等首个 AI/Agent 工具完成后评估 |

## 6. Skill 自我迭代

新增 `rules/skill-evolution.md` 管理 Skill 候选、升级、修改和退役。每项任务结束时，在规则复盘之后执行 Skill 复盘：

1. 识别是否重复执行了至少三个需要判断的步骤；
2. 判断流程是否稳定、项目特有、会在后续任务复用；
3. 优先把纯机械检查自动化为测试、脚本或 CI；
4. 只有需要工程判断的稳定流程才形成 Skill；
5. 新建或修改 Skill 必须先建立失败的契约或场景，再实施并验证；
6. 每次只创建或实质修改一个 Skill，完成验证与提交后才能进入下一个；
7. Skill 变化必须同步 `agents/openai.yaml`、候选表和交付说明。

项目所有者已授权满足门槛的 Skill 在后续任务中自动演进，但该授权不允许 Skill 扩大任务范围或覆盖更高优先级指令。

## 7. 测试策略

由于当前项目约束不允许在未明确要求时调用子代理，本轮不进行子代理压力测试，改用以下可审计验证：

1. 在 Skill 创建前先加入契约场景与验证器，运行并确认因 Skill 不存在而失败；
2. 使用 `skill-creator` 的 `init_skill.py` 初始化目录；
3. 实现 Skill 后运行项目契约验证器；
4. 运行 `skill-creator` 的 `quick_validate.py` 检查 Frontmatter、名称和结构；
5. 人工逐项对照三个场景：完整 CRUD、Outbox + 缓存、无数据库变更的只读 Endpoint；
6. 后续如获得子代理授权，再补充无 Skill/有 Skill 的前向场景测试。

契约验证器必须检查：触发描述、UI 元数据、无占位符、关键架构词、中文注释要求、reference 链接和候选治理入口。

## 8. 验收标准

1. `.agents/skills/fullnet-module-delivery` 可被 Skill 校验器识别；
2. `SKILL.md` 不超过 500 行，Frontmatter 只有 `name` 与 `description`；
3. 描述以 `Use when...` 开头，只表达触发条件，不概括完整工作流；
4. `agents/openai.yaml` 的默认提示显式包含 `$fullnet-module-delivery`；
5. 三个契约场景所需约束均可从 Skill 或其直接 reference 检索；
6. `AGENTS.md` 和规则索引包含项目 Skills 与自迭代入口；
7. Skill 候选表、升级门槛、单 Skill 验证和退役机制完整；
8. UTF-8、链接、占位符、Git 状态、Release 构建和现有测试均通过最终验证。
