# Full.NET 项目 Skills 自我迭代规则

## 1. Rules、Skills 与自动化的边界

| 载体 | 解决的问题 | 示例 |
| --- | --- | --- |
| `AGENTS.md` / `rules/` | 所有任务必须遵守什么 | 中文注释、租户隔离、双数据库验证 |
| `.agents/skills/` | 一类高频复杂任务如何可靠完成 | 从契约到测试交付完整业务模块 |
| 测试、脚本、分析器、CI | 如何确定性执行或阻止机械错误 | 校验 Skill 结构、测试数量、依赖方向 |

禁止把强制安全规则只写进可能不触发的 Skill。禁止为一条命令或简单清单创建 Skill；能稳定自动化的流程必须优先自动化。

## 2. 里程碑集中复盘与真实缺口触发

冻结新增项目 Skill。只有已有 Skill 在真实任务中给出错误路径、缺少停止条件，或者进入里程碑集中复盘时，才执行完整 Skill 复盘：

1. 本次是否重复执行了至少三个需要工程判断的步骤？
2. 是否在寻找路径、注册点、验证命令或边界时重复消耗上下文？
3. 已使用的项目 Skill 是否缺少触发词、步骤、异常路径或最新仓库信息？
4. 工作流是否会在不同模块或后续里程碑中再次出现？
5. 该问题应该形成 Skill，还是更适合测试、脚本、生成器或 CI？

没有新证据时只在交付中合并写一行“本任务未触发规则或 Skill 演进”，不得读取或修改候选计数。解除冻结需要项目所有者明确决定，或里程碑审计证明现有两个 Skill 无法覆盖已重复验证的工作流。

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
3. 仓库路径、框架版本和状态变化必须同步直接 reference；测试数量只引用 `eng/testing/test-matrix.json`，禁止复制数字。
4. 运行项目契约、官方校验、链接、占位符和 UTF-8 检查。
5. 实质修改仍遵守“一次一个 Skill”的部署门禁；纯路径或测试数量机械更新可与对应代码变更同一提交。

禁止先修改 Skill 再补测试。禁止通过放宽契约让错误实现变绿。

## 6. 触发后的演进流程

项目所有者已授权满足门槛的项目 Skills 在后续开发中自动演进，但该授权不要求每项任务维护治理状态。命中第 2 节触发条件后：

1. 搜索 `.agents/skills/` 和本文件候选表，避免重复；
2. 已有 Skill 出现真实缺口时，在当前授权范围内执行测试先行的修改；
3. 里程碑审计时统一整理候选证据，禁止在普通任务中机械增加次数；
4. 冻结期内不创建新 Skill；解除冻结后，新候选仍须满足第 3、4 节全部门槛；
5. 把可机械化部分转为脚本、测试或 CI，把需要判断的最小流程保留在 Skill；
6. 在最终交付中披露“未触发、已修改或里程碑候选调整”之一。

## 7. 当前项目 Skill

| Skill | 状态 | 触发范围 | 验证 |
| --- | --- | --- | --- |
| [`fullnet-module-delivery`](../.agents/skills/fullnet-module-delivery/SKILL.md) | 已验证 | 模块、CRUD、Endpoint、Command/Query、Dapper、双库迁移、Admin.NET 对标纵向切片 | `pnpm test:skills`（`tests/skills/validate_project_skills.py`）+ `quick_validate.py` |
| [`fullnet-performance-hardening`](../.agents/skills/fullnet-performance-hardening/SKILL.md) | 已验证 | 请求延迟、SQL/分页、缓存、Audit、Outbox/Jobs、基准与客户端包体优化 | `pnpm test:skills`（`tests/skills/fullnet-performance-hardening.contract.json`）+ `quick_validate.py` |

## 8. 候选登记

候选表是里程碑审计输入，不是每任务状态机。候选证据只保留可核验事实与下一升级触发；完整背景写入既有验证工件，不为累计次数新增 Verification。

| 候选 | 状态/次数 | 当前证据 | 下一升级触发 |
| --- | --- | --- | --- |
| `fullnet-dual-database-change` | 观察 / 11 | 双库迁移、Tenancy SQL/API、Identity 授权、语言偏好、Seed 审计与 Jobs Provider-neutral 主动续租 SQL 均覆盖；008/009 已用真实 MySQL/SQL Server 验证 23 列 Expand/Contract、维护窗口拒绝、schema-mode 门禁、显式聚集与未记账半完成恢复 | 完成生产等价停止写入＋备份恢复演练，或第二个破坏性双库迁移复用后，按测试先行评估从模块交付 Skill 拆分；仍缺真实恢复介质与 RTO/RPO 停止条件 |
| `fullnet-outbox-event-delivery` | 候选 / 2 | TenantProvisioned 与 TenantChanged 均使用事务 Outbox、版本化 MessagePack；TenantChanged 的 L2 删除或 Backplane 失败会传播到 Worker 并触发 Outbox 重试 | 第二个业务模块交付可靠事件时升级，验证跨模块复用后的输入、重试和停止条件 |
| `fullnet-api-compatibility` | 自动化优先 / 5 | ProblemDetails、Admin.NET Mapper、本地化认证 Challenge/429 已有真实 API 测试；冻结夹具比较器以及 PR base SHA / `main` push before SHA CI 会阻止 v1 路径、操作、安全字段与 schema 被静默破坏 | 等首个多客户端生成或真实 SDK 消费者落地后，再评估剩余人工决策是否足以形成 Skill；机械兼容检查继续保留在脚本、测试与 CI |
| `fullnet-cache-feature` | 候选 / 3 | FusionCache 双抽象、租户 ID/域名 key 与 tag 失效、提交后本机修复、事务 Outbox 驱动的 Redis Backplane 多实例可靠失效及失败重试均已落地 | 独立业务模块采用第二种缓存模型时，基于两类消费者边界按测试先行评估升级；当前创建新 Skill 会扩大本任务范围 |
| `fullnet-release-verification` | 自动化优先 / 12 | uni-app 三目标构建、fresh H5 E2E、许可与漏洞门禁已落地；共享 Hosting 全量 Integration 曾在 Docker Desktop 停止时产生 172 项环境失败，本次 SQL Server/MySQL 审计聚焦也在容器创建前因 daemon 停止各失败 3 项；两次均在启动并预热 Engine 后原命令精确复跑通过 | 优先把 Docker Engine readiness、冷启动预热和环境失败分类收敛进 Integration preflight/脚本，不创建判断型 Skill |
| `fullnet-realtime-feature` | 候选 / 3 | SignalR/MessagePack Hub、Notifications 提交后尽力推送、两个真实 API 宿主与专用 Redis 故障恢复，以及 Vue/Layui 认证连接、切上下文重连、通知刷新与失败降级均有自动化证据 | 第二个独立业务模块消费实时发布，或生产多副本编排与真实浏览器断网恢复形成完整生产流程后，再按测试先行评估升级；当前创建新 Skill 会扩大本任务范围 |
| `fullnet-agentic-feature` | 等待真实实现 / 0 | 只有 AI、Agent、MCP、Agentic Web 架构约束 | 首个显式授权 Agent Tool 验收后评估 |
| `fullnet-dual-admin-feature` | 候选 / 11 | Identity 会话、租户切换、权限导航、国际化/可访问性之外，租户套餐、Settings、Auditing 访问/操作日志与 Host API Key 均按同一“contracts 守卫 + admin-i18n 双语 + Vue/Layui 双实现 + shell-parity 双端场景”模式交付；API Key 额外验证一次性明文不进入 Web Storage | 首个含列表、表单、权限与租户边界的双端业务 CRUD 达到 `Verified` 后评估升级 |
| `fullnet-localization-delivery` | 候选 / 5 | L0-L2 之上，L3 uni-app 已落地规范语言/别名、Vue I18n、偏好原子提交、ProblemDetails、三目标构建与 H5 E2E；小程序开发者工具未安装，跨平台停止条件未闭合 | L2 落地首个双库可翻译业务数据，或完成微信/支付宝真实工具验收后评估升级 |
| `fullnet-seed-data-delivery` | 候选 / 5 | Identity 作为第二个模块复用 Contributor、稳定错误码与 Scoped 多实现，并由 Migrator 完成迁移后 Profile 编排、失败阻断、兼容别名与 Host 依赖门禁；Task 3A 已将场景查看者移出发布物并改为 API 健康后的测试脚本幂等创建，Architecture 与三宿主发布物扫描已锁定边界；当前机器缺容器运行时，更新后的双库真实栈未验证 | 在 SQL Server/MySQL 完成 Baseline/Development/Demo/Test 与新场景准备脚本 E2E 后，按测试先行评估升级 |

候选只在里程碑集中复盘时更新原行，禁止创建近义候选。候选升级后移入“当前项目 Skill”并删除原候选行。

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

- **未触发**：与规则结论合并为一行，说明没有真实 Skill 缺口且不在里程碑复盘；
- **已修改**：列出失败场景、Skill 文件、元数据和验证结果；
- **里程碑候选调整**：列出新增、合并、冻结或退役的候选及证据。
