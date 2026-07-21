# Full.NET 架构复核与事件交付方案评估

- 日期：2026-07-22
- 状态：已复核，改进项已吸收
- 代码基线：`370c6a7`（`main`）
- 范围：仓库结构、能力状态矩阵、近期验证记录、架构硬化计划，以及用户提供的两份外部审查材料
- 方法：静态代码核对、现有自动化证据交叉检查、规格/ADR/计划一致性检查；本轮未修改产品代码，也未重跑测试

## 1. 总体结论

Full.NET 继续采用强化型模块化单体是正确方向。当前主要风险不是重新选型，而是把已经识别的生产门禁按优先级关闭，并防止测试设施、模块注册、租户 SQL、缓存一致性和 Outbox 可靠性随着业务增长发生漂移。

外部报告对 Outbox、`IdentityModule.AddServices`、租户 SQL 语义、缓存故障注入和代理后客户端地址的判断基本成立，但部分结论已经落后于当前代码，或把治理清单数量误读为仍未迁移的运行时对象。后续计划只吸收能够被当前仓库证据支持的部分。

## 2. 外部建议逐项复核

| 外部结论 | 复核结论 | 处理 |
|---|---|---|
| Outbox 缺最大重试、死信和版本退役闭环 | 成立 | 保持架构硬化 Task 6 为当前 P1 可靠性工作；当前阶段只完善 Outbox |
| `IdentityModule.AddServices` 过大 | 成立，但属于可维护性而非当前正确性故障 | 纳入 P2 组合根拆分；不得改变公共注册行为 |
| 命名债务 83 项均未收敛 | 表述不准确 | 当前清单为 85 项，包含不可改写历史迁移和动态 SQL 精确豁免；010/011 后运行时对象已规范化。真正缺口是生产维护窗口、备份恢复和协议别名退役 |
| `TenantRequired` 仍依赖参数文本检查 | 成立 | 继续执行架构硬化 Task 5 的受控语义元数据与 Global Statement 精确目录 |
| FusionCache 安全关键失效缺真实故障验证 | 成立 | 继续执行架构硬化 Task 7 |
| `E2eHostViewerSeedContributor` 进入业务发布物 | 成立，且违反强制 Seed 边界 | 提升为 P0，先移出 Identity 发布程序集，再继续剩余 P1 工作 |
| Organization 应新增通用 `OrganizationScope` 并扩展 Seed 接口 | 不接受该实现建议 | 当前已有角色数据范围投影和 Organization 查询过滤；后续要求其他业务模块接入既有受控数据范围，不把组织过滤耦合进 Seed 接口 |
| Vue/Layui 双端共享不足 | 部分成立 | headless 契约和多个真实业务切片已存在；保留持续门禁，不重复建立另一套共享框架 |
| Login Handler 应立即拆成四段流水线 | 部分成立 | Handler 较大，但安全并发逻辑紧密；只在下一次登录行为变更或基准证明有收益时拆分，保持 P2 |
| 反向代理后按 `RemoteIpAddress` 限流可能失真 | 成立 | 纳入生产前代理信任链与客户端地址解析任务；只接受显式可信代理/网络，不直接信任任意转发头 |
| 双端 E2E 尚未实现 | 已过时 | 当前已有 SQL Server/MySQL 真实后端双管理端 E2E；剩余 Redis 与 Production TOTP 强制路径，不重复规划已完成范围 |

## 3. 确认的改进顺序

1. **P0 规则合规**：将 E2E 专用查看者数据从 Identity 发布程序集移到测试专用装配/场景夹具，并增加发布物边界断言。
2. **P1 Outbox 生产闭环**：最大尝试、永久失败分类、死信、人工重放审计、版本共存/退役、多 Worker 双库压力与拓扑文档。
3. **P1 数据与缓存安全**：完成 `TenantRequired`/`Global` 语义门禁和安全关键缓存多节点故障注入。
4. **P1 代理部署安全**：显式可信代理配置、规范客户端地址解析和伪造转发头测试。
5. **P2 可维护性**：按认证、授权、Seed、CORS、限流和领域服务拆分 Identity 组合根，但保持模块公共入口与 DI 行为不变。
6. **持续项**：其他业务模块复用既有机构数据范围；Vue/Layui 继续以共享契约夹具和对等 E2E 防漂移。
7. **最后阶段 Decision Gate**：只有当前硬化、核心业务模块和生产可观测性完成后，才评估 Kafka/CDC Relay。

## 4. Outbox、CDC Relay 与直接 Kafka 的可行性

### 4.1 可行，但不能仅按 QPS 路由

三种交付方式可以共存，但 `1000 QPS` 不能成为通用硬阈值。数据库类型、事务大小、Payload、索引、批量领取、延迟目标、保留期和部署规格都会改变实际容量。路由必须由事件语义和 SLA 静态决定，压测结果只负责证明某种实现是否达到目标，不能在运行时因为瞬时 QPS 自动改变可靠性语义。

同进程模块内部事件继续使用类型化 Contract/Dispatcher，不进入 Kafka。只有跨进程、需要持久交付的 Integration Event 才进入以下交付轨道。

| 轨道 | 适用范围 | 一致性语义 | 当前决策 |
|---|---|---|---|
| 事务 Outbox + Worker 轮询 | 默认可靠业务集成事件；低到中等吞吐或可接受轮询延迟 | 业务数据与事件原子落库；发布至少一次；消费者幂等 | **当前唯一实施轨道** |
| 事务 Outbox + CDC Relay + Kafka | 经压测证明轮询成为瓶颈，且事件仍必须与业务事务原子落库 | CDC/Producer/Consumer 均可能重放；端到端仍按至少一次设计 | M5+ Decision Gate |
| 直接 Kafka | 可丢失、可重算、与业务事务无原子关系的遥测/行为流 | 由 Producer 确认和业务容忍度决定；不得伪装成可靠业务事件 | M5+ Decision Gate |

### 4.2 必须修正的外部方案表述

- CDC Relay 不能被描述为端到端 Exactly-Once。连接器偏移、Kafka 事务和消费者副作用不是一个原子事务，必须使用稳定 `EventId`、分区键和消费幂等。
- Debezium/Kafka Connect 通常自行维护连接器 Offset；Full.NET 不预建 `fn_cdc_bookmark` 作为通用事实源。
- 直接 Kafka 不允许在 `finally` 或无人观察的后台 `Task` 中 fire-and-forget。即使事件可丢，也必须有有界缓冲、Producer Delivery Report、过载策略和丢弃指标。
- 轮询 Worker 与 CDC Relay 不能无所有权地同时发布同一 Outbox 记录。每个事件流必须由一个 Relay Provider 独占，并有明确切换、排空和回退步骤。
- 不建立根据 QPS 动态分流的 `SmartEventPublisher`。业务代码只声明稳定事件类别/交付 SLA，Provider 选择由部署期配置和批准的事件目录决定。

### 4.3 后期进入门禁

只有同时满足以下条件，才允许为 CDC/Kafka 编写独立 ADR、规格和实施计划：

1. 当前 Outbox Task 6 已完成并有 SQL Server/MySQL 双库、多 Worker 和故障恢复证据；
2. 存在真实事件消费者、稳定吞吐/延迟/可丢失预算，而不是预估 QPS；
3. 基准证明调优后的轮询 Outbox 无法满足目标，且瓶颈不是索引、批量、Payload 或消费者处理；
4. 已确认 SQL Server CDC 与 MySQL Binlog 的部署权限、日志保留、Schema 演进和恢复策略；
5. Kafka 集群、Schema 兼容、ACL、TLS、分区、重放、DLQ、监控和容量成本有明确责任人；
6. 事件路由在发布前静态冻结，可靠事件禁止降级到直接 Kafka；
7. 方案不构成提前服务拆分，也不改变模块化单体基线。

## 5. 未验证项

- 本轮未执行新的构建、测试、性能基准或故障注入；代码能力状态不因本报告提升。
- 未验证任何特定 Kafka、Debezium 或 Kafka Connect 版本与许可证/部署组合。
- `1000 QPS` 仅作为用户提出的初始量级，不作为项目门禁常量。
- 工作区存在用户已有的未跟踪测试输出文件，本轮不读取其结论、不修改也不纳入文档变更。

## 6. 正式落点

- 长期架构基线：[总体架构 Spec](../superpowers/specs/2026-07-17-fullnet-architecture-design.md)
- 强制执行边界：[开发质量规则 §6](../../rules/development-quality.md#6-并发重试幂等与-outbox)
- 技术阶段与 Provider 门禁：[技术集成路线](../superpowers/specs/2026-07-17-technology-integration-roadmap-design.md)
- 当前状态与优先级：[能力状态矩阵](../roadmap/capability-status.md)
- 可执行工作：[架构硬化实施计划](../superpowers/plans/2026-07-18-architecture-hardening.md)
