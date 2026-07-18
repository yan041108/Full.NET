# Full.NET 项目 Skills 自我迭代规则

## 1. Rules、Skills 与自动化的边界

| 载体 | 解决的问题 | 示例 |
| --- | --- | --- |
| `AGENTS.md` / `rules/` | 所有任务必须遵守什么 | 中文注释、租户隔离、双数据库验证 |
| `.agents/skills/` | 一类高频复杂任务如何可靠完成 | 从契约到测试交付完整业务模块 |
| 测试、脚本、分析器、CI | 如何确定性执行或阻止机械错误 | 校验 Skill 结构、测试数量、依赖方向 |

禁止把强制安全规则只写进可能不触发的 Skill。禁止为一条命令或简单清单创建 Skill；能稳定自动化的流程必须优先自动化。

## 2. 每项任务的 Skill 复盘

完成 [`rule-evolution.md`](rule-evolution.md) 的规则复盘后，必须继续执行一次 Skill 复盘：

1. 本次是否重复执行了至少三个需要工程判断的步骤？
2. 是否在寻找路径、注册点、验证命令或边界时重复消耗上下文？
3. 已使用的项目 Skill 是否缺少触发词、步骤、异常路径或最新仓库信息？
4. 工作流是否会在不同模块或后续里程碑中再次出现？
5. 该问题应该形成 Skill，还是更适合测试、脚本、生成器或 CI？

没有新证据时必须得出“本次无 Skills 变化”，不得为了表现自我迭代而制造 Skill。

## 3. 候选升级门槛

新 Skill 必须同时满足以下条件：

1. **高复用**：在至少两个独立任务或模块中出现，或者项目所有者明确要求长期复用；
2. **有判断**：包含至少三个不能用单一确定性命令替代的决策或步骤；
3. **边界稳定**：输入、输出、停止条件和主要失败模式已经从真实实现中得到验证；
4. **项目特有**：包含 Full.NET 的目录、架构、契约或工具知识，通用现有 Skill 无法充分覆盖；
5. **可验证**：能够在创建前写出会失败的契约、场景或检索测试，并在实现后验证通过。

安全、数据损坏、租户越权或许可证等高风险流程可在首次真实出现时升级，但仍必须有稳定边界和失败验证。

以下情况禁止创建 Skill：

- 只在一次任务中出现的临时操作；
- 尚无真实消费者的未来架构设想；
- 只包装构建、复制或格式化等机械命令；
- 与现有项目 Skill 高度重叠且不能明确拆分责任；
- 仅记录一次问题的叙事，没有可复用流程。

## 4. 新 Skill 的 RED-GREEN-REFACTOR

每次只能创建一个 Skill，并按顺序完成：

1. **RED**：先创建契约或场景，运行并确认因目标 Skill/能力缺失而失败；失败不能来自语法、编码或环境误配。
2. **初始化**：使用 `skill-creator` 的 `init_skill.py` 在 `.agents/skills/` 创建标准目录，不手写替代初始化流程。
3. **GREEN**：编写最小 `SKILL.md` 和必要 reference/script，使相同契约通过。
4. **官方验证**：运行 `quick_validate.py`，检查 Frontmatter、名称和目录结构；中文文件使用 Python UTF-8 模式。
5. **场景审查**：逐项确认不同输入、可选路径和不应触发的反例。得到子代理授权时，应补充无 Skill/有 Skill 的前向测试。
6. **REFACTOR**：只针对实际暴露的遗漏收紧触发描述、步骤或防误用说明，再重新运行全部验证。
7. **部署门禁**：提交当前 Skill、测试和元数据后，才能开始下一个 Skill。

Skill 内只保留必要的 `SKILL.md`、`agents/openai.yaml`、`references/`、`scripts/` 或 `assets/`。禁止添加 README、安装指南和变更日志。

## 5. 修改已有 Skill

1. 先新增能复现缺口的契约或场景，并确认当前 Skill 无法满足或会给出错误路径。
2. 修改正文时检查 `description` 是否仍准确触发；修改触发范围或名称时必须同步 `agents/openai.yaml`。
3. 仓库路径、测试数量、框架版本和状态变化必须同步直接 reference。
4. 运行项目契约、官方校验、链接、占位符和 UTF-8 检查。
5. 实质修改仍遵守“一次一个 Skill”的部署门禁；纯路径或测试数量机械更新可与对应代码变更同一提交。

禁止先修改 Skill 再补测试。禁止通过放宽契约让错误实现变绿。

## 6. 自动演进流程

项目所有者已授权满足门槛的项目 Skills 在后续开发中自动演进。每项任务结束时必须：

1. 搜索 `.agents/skills/` 和本文件候选表，避免重复；
2. 更新命中的候选次数、最近证据和下一触发条件；
3. 已有 Skill 出现真实缺口时，在当前授权范围内执行测试先行的修改；
4. 新候选达到全部门槛且创建不构成任务范围的重大扩张时，建立独立规格、计划和 RED 契约后实施；
5. 若创建会显著扩大当前任务，只登记候选和证据并报告，不得用项目授权覆盖更高层级的范围限制；
6. 把可机械化部分转为脚本、测试或 CI，把需要判断的最小流程保留在 Skill；
7. 在最终交付中披露“无变化、候选更新、已修改或已新增”之一。

## 7. 当前项目 Skill

| Skill | 状态 | 触发范围 | 验证 |
| --- | --- | --- | --- |
| [`fullnet-module-delivery`](../.agents/skills/fullnet-module-delivery/SKILL.md) | 已验证 | 模块、CRUD、Endpoint、Command/Query、Dapper、双库迁移、Admin.NET 对标纵向切片 | `tests/skills/validate_project_skills.py` + `quick_validate.py` |

## 8. 候选登记

| 候选 | 状态/次数 | 当前证据 | 下一升级触发 |
| --- | --- | --- | --- |
| `fullnet-dual-database-change` | 观察 / 5 | 基础迁移、Tenancy SQL/API、Identity 授权上下文与语言偏好迁移均覆盖双库；语言偏好迁移审查进一步暴露 DbUp 未记账、DDL 部分完成时两库均无法恢复，现已加入真实恢复测试与现有模块交付 Skill | 再出现跨模块提供程序分支时，评估从模块交付 Skill 拆分；当前由强化后的既有 Skill 与双库恢复门禁覆盖 |
| `fullnet-outbox-event-delivery` | 候选 / 1 | TenantProvisioned 事件使用事务 Outbox 与 MessagePack | 第二个业务模块交付可靠事件时升级 |
| `fullnet-api-compatibility` | 候选 / 3 | 标准 ProblemDetails 与 Admin.NET Mapper 已存在；本次终审又将认证 Challenge 的裸 401 收敛为保留 `WWW-Authenticate` 的稳定、本地化 ProblemDetails，并以聚焦测试锁定协议 | 新增分页、文件或另一类兼容端点时评估升级 |
| `fullnet-cache-feature` | 候选 / 2 | FusionCache 双抽象、按域名与按 ID 的租户解析缓存及 tag 失效处理已存在 | 独立业务模块采用第二种缓存模型或 Redis 多实例验证落地时升级 |
| `fullnet-release-verification` | 自动化优先 / 9 | uni-app 交付新增三目标构建、强制 fresh H5 产物的浏览器 E2E、许可证与官方 registry 漏洞门禁；审查发现陈旧产物可造成 DEV bridge 泄漏扫描假通过，现已由 `pretest` 契约阻止 | 继续收敛为跨平台验证脚本/CI，不优先创建判断型 Skill |
| `fullnet-realtime-feature` | 等待真实实现 / 0 | 只有 SignalR、MessagePack Hub、Redis Backplane 设计 | 首个 `IRealtimePublisher` 消费者验收后评估 |
| `fullnet-agentic-feature` | 等待真实实现 / 0 | 只有 AI、Agent、MCP、Agentic Web 架构约束 | 首个显式授权 Agent Tool 验收后评估 |
| `fullnet-dual-admin-feature` | 候选 / 4 | Identity 会话、租户切换、权限导航、`zh-CN/en-US` 国际化/可访问性及组件语言与偏好失败回滚已按同一契约分别实现 Vue/Pinia 与 Layui/原生 JS，并通过同场景双端 E2E | 首个包含列表、表单、权限和租户边界的双端业务 CRUD 切片达到 `Verified` 后评估升级 |
| `fullnet-localization-delivery` | 候选 / 5 | 在 L0-L2 基础上，L3 uni-app 已落地规范语言适配、平台别名、Vue I18n、账号偏好原子提交、ProblemDetails、三目标构建与 H5 E2E；小程序开发者工具仍未安装，尚未形成完整跨平台验收停止条件 | L2 落地首个双库可翻译业务数据，或完成微信/支付宝真实工具验收后评估升级 |
| `fullnet-seed-data-delivery` | 等待真实实现 / 0 | 已形成生产 Baseline、Development/Demo/Test Overlay、双库锁/审计和场景 Test Factory 分层设计，当前仍是 Migrator 硬编码 `--seed-local` | S0-S2 落地且第二个真实业务模块贡献双库幂等 Seed 后评估 |

候选命中时更新原行，禁止创建近义候选。候选升级后移入“当前项目 Skill”并删除原候选行。

## 9. 候选记录模板

```markdown
| `skill-name` | 候选 / 1 | 任务、失败或用户决策的可核验证据 | 达到稳定边界所需的下一次真实使用 |
```

名称必须使用小写字母、数字和连字符，优先采用动词或动作导向表达，最长 64 个字符。

## 10. 退役与拆分

1. Skill 与仓库架构不再匹配时，先提供替代 Skill 或迁移路径，再删除自动入口。
2. 一个 Skill 经常加载无关内容或超过 500 行时，按稳定职责拆分；先为新边界建立契约。
3. 两个 Skill 长期重叠时保留触发更清晰、验证更完整的一个，并更新所有引用。
4. 退役必须删除或更新 `AGENTS.md`、规则索引、候选表、UI 元数据和契约测试，禁止留下失效触发器。

## 11. 交付披露格式

- **无变化**：本次没有重复且稳定的新工作流，已有 Skill 无缺口；
- **候选更新**：列出名称、次数、证据和下一触发条件；
- **已修改**：列出失败场景、Skill 文件、元数据和验证结果；
- **已新增**：列出触发范围、RED 失败、GREEN 结果、官方校验与仍未进行的前向测试。
