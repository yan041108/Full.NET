# 2026-08-07《Full.NET 架构完整分析报告》复核记录

- 复核日期：2026-08-08
- 代码基线：`bbb718a49979cd33d873e6981174dee6a982f614`（`main`）
- 输入：Codex 附件 `pasted-text.txt` 中的《Full.NET 架构完整分析报告（2026-08-07）》
- 范围：只读代码、契约、迁移、测试、CI、路线图和验证记录交叉复核；本记录不以文档中的测试数量代替 [`eng/testing/test-matrix.json`](../../eng/testing/test-matrix.json)。

## 结论摘要

报告对 Full.NET 的整体优点评价基本成立，但“十大不足”中混入了已经关闭的历史状态和两个错误归因。不能按原风险矩阵直接开工：

- **成立或部分成立，需进入后续计划**：跨模块本地事务债务、本地投影参考切片、2 个缓存策略 allowlist、NuGet 漏洞阻断、Vue/OpenAPI/共享契约覆盖、首个真实非加法事件升级演练。
- **已实现，不应重新开发**：Tenancy 新写路径移除缓存 Outbox、MySQL UUID `char(36)` 迁移、Production TOTP 真实栈、模块表访问债务目录、生产 Endpoint 的 System.Text.Json 源生成覆盖门禁。
- **只剩环境或发布验收**：MySQL UUID 生产维护窗口与 RPO/RTO 演练；完整生成式 SDK 是否需要应由真实多客户端/外部 SDK 证据决定。

跨模块一致性是当前最值得继续投资的方向，但正确方案不是报告提出的“事务前校验 + Outbox 保证写”。同步校验在调用返回后即可陈旧，Outbox 只能保证本模块状态与本模块事件原子提交，不能把两个模块变成一个事务。正确退役模式已补入 [`ADR-0002`](../architecture/adr/ADR-0002-modular-monolith-evolution.md#存量债务退役与本地投影验收)：提示性校验、消费方投影、资源 claim 状态机和 Saga 必须按不变量分别选用。

## 十项不足逐条判定

| # | 报告结论 | 复核判定 | 当前证据与处理 |
|---:|---|---|---|
| 1 | 5 项跨模块本地事务债务未退役 | **成立，但统一标为 P0 过重，根因描述不准确** | [`module-local-transaction-debt.json`](../../contracts/architecture/module-local-transaction-debt.json) 精确存在 5 项，其中 Identity→Organization、Document→Files 为 high，其余 3 项为 medium。Architecture 门禁已经阻止新增，且当前仍为同进程模块化单体，因此应按 high→medium 的 P1 队列逐项退役，而不是以“事务前校验 + Outbox”作为通用解法。 |
| 2 | Tenancy 仍以 Outbox 传播缓存失效 | **已过时，不成立** | `TenantCacheInvalidator.InvalidateAfterCommitAsync` 已执行提交后直接失效；`HostTenantManagementService` 明确禁止新缓存 Outbox。`TenantChangedCacheInvalidationHandler` 等只为兼容排空历史消息，必须在所有环境旧消息排空后再删除，不能重新实现。 |
| 3 | MySQL UUID `char(36)` 迁移尚未实施 | **实现层面不成立，生产演练缺口成立** | 008/009 已完成 expand→backfill→contract，`UuidStorageContractV1`、双库迁移和恢复测试锁定 `binary(16)`。剩余是生产等价环境维护窗口、备份恢复和 RPO/RTO 演练，不是再次编写迁移。 |
| 4 | 缺少跨模块事件本地投影样例，且表访问债务目录不存在 | **部分成立** | 完整的业务“所有者事件→消费方投影→回填/重建/对账”参考切片仍缺；但 [`module-table-access-debt.json`](../../contracts/architecture/module-table-access-debt.json) 已存在且当前为空，相关扫描门禁也已落地。Identity→Organization 是首个投影候选。 |
| 5 | FusionCache 分类和失效策略未统一 | **部分成立，现状被低估** | C0/S0-L2/S1/S2/N0、策略注册表、Redis Backplane、提交后直接失效已落地。真实缺口是 `TenantResolver` 与 `MyGridPreferenceService` 仍手写 `HybridCacheEntryOptions`，Architecture 测试以 2 项精确 allowlist 管理；应迁入注册表并把 allowlist 收敛为零。 |
| 6 | Production TOTP 强制路径真实栈未完成 | **不成立** | [`real-stack-redis-production-totp-2026-07-29.md`](real-stack-redis-production-totp-2026-07-29.md) 已记录 Production 配置、真实 Redis、真实 API 与浏览器 grant 链路；`production-totp-grant.test.mjs` 存在。能力应保持 `Build-verified`，不能重复立项，也不能据此声称生产环境已认证。 |
| 7 | 当前机器缺少容器运行时 | **当前不成立** | 复核时 Docker Client/Server 可用，版本为 29.6.2；此前某个窗口缺少容器或 Docker 未启动属于历史验证条件，不应继续写入当前能力矩阵。每个真实栈任务仍须报告本次新鲜 runner、容器与 residual 结果。 |
| 8 | 缺少消息协议版本升级完整路径 | **部分成立** | [`outbox-worker-topology.md`](../operations/outbox-worker-topology.md) 已定义并行版本 Handler、精确版本路由、consumer-first、producer-second 和退役扫描；基础设施测试也锁定这些规则。缺口是首个真实非加法业务事件的相邻版本 upgrader 与 v1→v2 演练。该实现应由真实事件触发，不能创建无消费者的通用升级器。 |
| 9 | STJ 和 Vue/OpenAPI 强类型验证不完整 | **部分成立，STJ 状态已过时** | `SerializationRulesTests` 已从生产 Endpoint 元数据枚举请求、响应、分页项和 ProblemDetails，缺少源生成类型会失败关闭；Vue 已广泛消费 `packages/client-contracts`，OpenAPI breaking gate 也存在。缺口是“Vue API 调用点—OpenAPI 路由/Schema—共享 TS 契约”三者没有统一覆盖清单；完整生成式 SDK 目前不是必要条件。 |
| 10 | 安全扫描只报告、不阻断 CI | **只对 NuGet 成立** | npm 已通过 `security/client-audit-policy.json` 和 `audit-client-dependencies.mjs` 对 Critical/High 失败关闭，并只接受精确、到期例外；CI 中 NuGet 仍只执行 `dotnet list ... --vulnerable`，缺少仓库级 JSON 解析、例外策略与可测试的失败关闭门禁。 |

## 风险重排

| 优先级 | 工作 | 原因 |
|---|---|---|
| P0 | NuGet Critical/High 漏洞失败关闭门禁 | 这是合并与发布供应链边界；当前只有命令退出语义，无法审计精确例外与解析失败。 |
| P1 | 5 项跨模块本地事务债务按不变量退役 | 两项 high 需要投影或 claim 状态机；三项 medium 可先通过事务外批量校验、失败关闭和对账降低耦合。 |
| P1 | 缓存策略 allowlist 收敛为零 | 已有框架能力，只需完成最后两个业务调用点，改动范围小且可自动门禁。 |
| P2 | Vue/OpenAPI/共享 TS 契约覆盖清单 | 防止新 API 调用退回手写漂移，不要求立即生成全量 SDK。 |
| Decision Gate | 首个真实 Integration Event v1→v2 演练 | 等真实非加法事件出现再实现相邻 upgrader；当前只保持规则和测试，不做投机代码。 |
| Production Gate | MySQL UUID 维护窗口与 RPO/RTO 演练 | 只在生产等价环境执行，不再修改 008/009 迁移来伪造完成。 |

## 能力矩阵损坏根因与修复

复核期间确认 [`capability-status.md`](../roadmap/capability-status.md) 不是终端显示问题，而是文件中实际存在 Unicode Replacement Character `U+FFFD`：修复前共有 161 个，分布在 46 行。Git 历史显示：

- `16c2d53c2794dd4ecc0f064cc04fd40a45dab978` 是最后一个无替换字符版本；
- `fce87db142ee6a7fbf408e26090e2ab38a542acf` 首次写入大批 `U+FFFD`，后续提交只局部覆盖，损坏因而长期保留；
- 可以确认的根因类别是一次错误的文本解码/重编码写回；历史命令不足以确定具体编辑器或执行进程，因此不把责任归因给某个工具。

本轮已重写该矩阵，移除损坏字符、历史测试数量和已经过时的缓存 Outbox、UUID、TOTP、Docker、STJ 结论。后续治理任务必须加入权威 Markdown 的 UTF-8/`U+FFFD` 失败关闭检查，避免同类损坏再次进入 `main`。

后续复核又确认 [`getting-started.md`](../development/getting-started.md) 在同一首个损坏提交中把中文批量替换成了有效 UTF-8 的 ASCII `?`：修复前共有 5,120 个问号和 568 段连续问号。该文件已按当前架构、测试入口和 Vue 单一交付线重建；文档完整性门禁因此同时检查无效编码、`U+FFFD` 和排除代码后的连续 ASCII 问号乱码。

## 本轮文档处置

1. 修订 [`ADR-0002`](../architecture/adr/ADR-0002-modular-monolith-evolution.md)，明确债务分类、投影验收和 claim/Saga 边界。
2. 修订[总体架构规格](../superpowers/specs/2026-07-17-fullnet-architecture-design.md)，纠正 UUID 当前状态，补齐事件版本、Endpoint/客户端契约和依赖漏洞门禁。
3. 重写[能力状态矩阵](../roadmap/capability-status.md)，只保留状态、稳定证据入口和下一优先级。
4. 将已完成的 2026-08-07 框架硬化计划标记为历史底座，将剩余业务债务转入[后续实施计划](../superpowers/plans/2026-08-08-architecture-gap-follow-up.md)。

## 未声称的验证

本轮是文档与框架设计复核，没有修改生产代码、SQL、配置或客户端，也没有据此提升任何能力到 `Production-verified`。Cursor 后续每个代码切片仍须按独立 snapshot 执行 RED→GREEN、受影响双库验证、治理门禁和残留清理。
