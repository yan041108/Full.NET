# 外部全面分析复核与吸收记录

- 复核日期：2026-07-21
- 复核起点：当前 `main`（含 pre-v1 naming 010/011 与 Integration 门槛 **85**）
- 输入：项目所有者提供的第三方只读全面分析报告（未改仓库文件）
- 方法：逐项对照当前代码、CI、能力矩阵、既有硬化计划与 2026-07-18 外部复核；报告未附新鲜构建输出，因此不把其推断直接视为缺陷
- 前序：[`external-review-2026-07-18.md`](external-review-2026-07-18.md)

## 结论摘要

报告总体判断与仓库自我定位一致：**工程与治理成熟度明显高于业务模块成熟度**；当前是 Identity/Tenancy 安全底座 + 双库基础设施，不是 Admin.NET 全功能后台。多数“不足”在 [`capability-status.md`](../roadmap/capability-status.md) 与 [`2026-07-18-architecture-hardening.md`](../superpowers/plans/2026-07-18-architecture-hardening.md) 中已登记。

本轮**不照单全收**。吸收的是：优先级重排、文档入口、门槛审计补记、PR 集成冒烟加宽、Worker 部署约束明示、首个业务纵向切片计划立项。若干条目已过期、过度推断或与既有决策冲突，予以保留说明而不改架构基线。

## 逐项真实性判定

| 报告结论 | 判定 | 处置 |
|---|---|---|
| 仅 Identity/Tenancy，无用户/角色/菜单 CRUD；不宜宣传“开箱即用完整后台” | **成立** | 状态矩阵已禁止夸大宣传；本轮将“首个业务纵向切片”从 P2 提升为近期 P0/P1 交付证明项，并新增实施计划 |
| 元治理 / ADR / 能力矩阵诚实标记 / 治理 CI 成熟 | **成立** | 维持；无需改基线 |
| UUID v7 Expand/Contract 与维护窗口门禁严谨 | **成立** | 维持；生产窗口与聚集索引基准仍缺（矩阵已记） |
| 登录时间攻击防御、Refresh 重用撤销、逐请求会话校验、密钥轮换 | **成立** | 维持；MFA/故障注入仍为缺口 |
| Outbox 与业务同事务、双库 Acquire、指数退避 | **成立** | 维持 |
| Outbox 无 MaxAttempts / 死信闭环 | **成立** | 已在硬化计划 Task 6；保持 P1，本轮不降级也不重复设计 |
| Outbox Worker 多实例“无保护、K8s 必出问题” | **过度** | `UPDLOCK/READPAST` + 租约已提供并发安全领取；真缺口是多 Worker 压力/崩溃测试与**部署拓扑文档**。吸收为：文档明示默认单副本或依赖租约的多副本假设，并在 Task 6 增加双库多 Worker 压力场景；不默认引入 Redis Leader Election |
| MySQL Outbox Acquire 会全表加锁、性能必然恶化 | **证据不足** | 沿用 07-18 判定：正确性由行锁与事务保证；性能结论须基准，不得凭 SQL 形态定性。记入 P2 基准候选 |
| SqlScopeGuard 字符串包含脆弱、Global 可绕过审查 | **成立（已知债）** | 矩阵与硬化计划已记；保持语义元数据 / Global 精确目录路线 |
| ProvisionTenant 存在 TOCTOU | **部分成立** | 应依赖唯一约束 + 冲突映射为稳定错误码；记入 Tenancy 后续切片验收，不单独开架构变更 |
| JWT 不含 kid | **不成立/过期** | `JwtAccessTokenIssuerTests.Issued_token_has_kid_required_claims_and_valid_signature` 已断言 kid；轮换按 kid 路由优化可留 P2 |
| Login Handler 过大、重试魔法数不一致 | **成立（可维护性）** | P2 重构候选；不阻塞业务切片 |
| AppHost 缺 OTel Collector / Aspire HealthCheck / 持久化卷 | **部分成立** | 本地编排最小可用已满足；可观测与 Aspire 钩子记入 P2 DX，不抬升为 1.0 门禁 |
| Integration 门槛 audit 停在 66/74，与 CI **85** 漂移 | **成立** | 本轮补记门槛审计；声明源以 README / getting-started / CI / delivery-map 为准 |
| PR 仅跑 2 项迁移冒烟，业务回归滞后到 main | **成立（工程权衡）** | 吸收：扩大 PR 冒烟至 Identity/Tenancy/Outbox 核心 filter（目标 ≤15m），全量仍仅 main |
| Architecture Tests 仅 26 项偏少 | **方向正确** | 随模块增长追加表所有权、Contracts 泄漏、SqlDataScope 显式性；不追求数量本身 |
| 缺性能基准 / 故障注入 | **成立** | 矩阵已记；ADR-0003 聚集索引基准与 Outbox/Login 基准保持 P1/P2 |
| 双端成本高、建议 Layui 退役时间表 | **决策门禁** | 当前规则仍强制双端同步；本轮记录为待所有者决策项，**不**在复核中单方面改变 `client-frontend` 基线 |
| uni-app 仅 locale 页、Flutter 未建工程 | **成立** | 与矩阵一致；Flutter 保持 Designing，不承诺 1.0 业务页 |
| 文档多、入门路径不清；rules 与 specs 双写风险 | **部分成立** | 吸收：新增人类 onboarding 入口；明确 rules=强制权威、specs=设计历史 |
| 缺生产运维 Runbook | **成立** | 吸收为文档债：JWT 轮换、Outbox 死信、Redis 故障、Seed 失败恢复 |
| Pre-v1 兼容层无退役时间表 | **成立** | 记入 1.0 发布后兼容窗口待决事项 |
| Seed Production 隔离缺 CI 验证 | **成立** | Seed 计划与矩阵已记；保持 P0/P1 生产可控性队列 |
| 综合评分与“适合/不适合生产”判断 | **意见参考** | 不写入能力矩阵；对外表述继续以状态矩阵第 3 节为准 |

## 本轮吸收动作（文档与计划）

1. 更新 [`capability-status.md`](../roadmap/capability-status.md) 近期优先队列：提升首个业务纵向切片与门槛/PR 冒烟治理。
2. 补记 [`test-threshold-audit-2026-07-19.md`](test-threshold-audit-2026-07-19.md) 至声明门槛 **314/7/26/85**。
3. 扩展 [`architecture-hardening`](../superpowers/plans/2026-07-18-architecture-hardening.md) Task 6（多 Worker 验证与部署约束）并新增 PR 集成冒烟任务。
4. 新增首个 Identity 管理纵向切片实施计划（用户管理优先；角色/菜单随后），批准依据为已批准总体架构“先纵向切片”与功能对标矩阵。
5. 新增 [`onboarding.md`](../development/onboarding.md) 人类阅读入口；`getting-started` 增加链接。
6. 在客户端路线图记录 Layui 去留为 Decision Gate 待决事项（不改强制双端规则）。

## 明确不吸收 / 暂缓

| 建议 | 理由 |
|---|---|
| 立即引入 Redis Leader Election | 租约领取已覆盖正确性；先补多 Worker 测试与文档，再谈选举 |
| 将 Layui 标为历史兼容并开始退役 | **已否决**（见下方决策附录：长期并行） |
| 把 Flutter/uni-app 业务页列入 1.0 必达 | 与现矩阵冲突；继续按客户端路线图分期 |
| 因“文档多”合并或删除 ADR/specs | 违反 ADR-0001 分层；只加强入口导航 |
| 将评估评分写入能力矩阵 | 评分是意见，不是可验证状态 |

## 建议的后续实施顺序（吸收后）

1. **P0 文档闭环**：门槛审计补记（本轮）、onboarding 入口（本轮）。
2. **P0/P1 交付证明**：Identity 用户管理纵向切片（API + 双库 + Vue/Layui + 真实栈冒烟）。
3. **P1 可靠性**：Outbox 死信/MaxAttempts/多 Worker 压力（硬化 Task 6 扩展）。
4. **P1 工程门禁**：PR 集成冒烟加宽至核心 Identity/Tenancy/Outbox 场景。
5. **P1 语义 SQL**：TenantRequired 元数据与 Global Statement 目录。
6. **已决策**：Layui 长期并行（见决策附录）；Pre-v1 兼容层 1.0 后退役日历仍待决。
7. **P2**：运维 Runbook、Aspire HealthCheck、性能基准、Login Handler 拆分。

## 决策附录（项目所有者 2026-07-21）

| 议题 | 决策 | 落点 |
|---|---|---|
| 首个业务纵向切片 | **用户管理**作为第一刀（Host 用户列表/详情/创建/更新/禁用） | [计划已批准](../superpowers/plans/2026-07-21-identity-user-management-vertical-slice.md)；对标矩阵「用户管理」保持 Designing → 实施后升 Implementing |
| Layui 轨道 | **长期并行**，不设退役窗口；非历史兼容过渡层 | [`client-frontend.md`](../../rules/client-frontend.md) §4；[`client-delivery-roadmap.md`](../roadmap/client-delivery-roadmap.md) §3.1；能力矩阵 Layui 行与优先队列第 8 项 |

后续实施默认按上述决策执行；若变更须再次书面确认并同步规则/路线图。

## 未验证项

- 本轮为文档吸收，**未**重跑四套 .NET 全量测试与客户端 E2E。
- Integration **85** 项声明门槛已与 README/CI/getting-started/delivery-map 对齐，但本轮未附新鲜全量执行日志；下次发布候选须补新鲜 85/85 证据。
- MySQL Outbox 热点表锁性能、ProvisionTenant 并发冲突映射、AppHost `UseMySql` 默认说明均未在本轮实测。

## 关联文档

- [当前能力状态矩阵](../roadmap/capability-status.md)
- [架构硬化实施计划](../superpowers/plans/2026-07-18-architecture-hardening.md)
- [Identity 用户管理纵向切片计划](../superpowers/plans/2026-07-21-identity-user-management-vertical-slice.md)
- [人类 onboarding 入口](../development/onboarding.md)
- [2026-07-18 外部复核](external-review-2026-07-18.md)
