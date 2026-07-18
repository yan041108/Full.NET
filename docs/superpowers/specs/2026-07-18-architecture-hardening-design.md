# Full.NET 架构风险复核与硬化设计

- 日期：2026-07-18
- 状态：已批准；依据项目所有者“后续按推荐方案自动确认”的授权纳入基线
- 范围：吸收外部交叉审查中可被仓库证据证实的改进，不改写既有核心选型
- 非范围：本次只完善文档和计划，不修改代码、SQL、配置或测试

## 1. 复核方法与结论

本次逐项对照实现、测试和既有规格。结论分为三类：

1. **接受**：真实存在且会影响生产安全、演进或交付真实性；
2. **部分接受**：风险真实，但外部建议的原因或方案不完全正确；
3. **不接受**：仓库已有防护，或建议会破坏 Full.NET 的既定边界。

总体判断不是“架构需要重做”，而是“正确的架构基线需要更硬的实施门禁”。近期优先级应从扩展功能转向 Seed、模块装配、SQL 安全、消息演进和真实链路验证。

## 2. 外部审查逐项处置

| 审查项 | 处置 | 仓库事实与最终决策 |
|---|---|---|
| MessagePack + Outbox 版本风险 | 部分接受，P1 | 已有 `SchemaVersion`、整数 Key、尾部追加和精确版本路由；“DTO 加字段必然失败”并不准确。真实缺口是蓝绿期间多版本共存、旧消息升级、版本退役和毒消息闭环 |
| 双数据库方言地狱 | 接受，P0/P1 | 允许数据库专用 SQL，但必须成对实现、语义一致并双库测试；禁止把专有语法隐藏在通用 Handler。JSON 聚合/变更默认放应用层 |
| FusionCache 失效时序 | 部分接受，P1 | 全局 Fail-Safe 已关闭；Background Refresh 不能作为权限正确性机制。真实缺口是提交到 Outbox/Backplane 生效之间的陈旧窗口 |
| 租户上下文与刷新竞态 | 部分接受，P1 | Refresh 已以服务端 Session 的 `ActiveTenantId` 为权威，不信任 JWT 租户。需补切换/刷新线性化和跨 Tab 共用 Cookie 的刷新竞争验证 |
| Seed 与 Migration 回滚 | 部分接受，P0 | Baseline/Overlay 设计已完整，真实问题是尚未实施。拒绝通用 Seed Down；开发重置使用临时库重建/备份恢复，数据修正向前演进 |
| Layui 现代化代价 | 部分接受，P1 | Layui 端已有 Vite/Vitest；不降低功能、权限和关键流程对等要求。允许交互机制不同，收敛 headless 规则，不共享 UI 组件 |
| L5 业务数据翻译 | 部分接受，P2 | 需要给出可复制的模块翻译表参考结构；拒绝全局 EAV 和默认多语言列爆炸。首个真实消费者拥有自己的翻译表和索引 |
| 数据库变更审查 | 接受，P0 | 现有规则缺少自动化 SQL 静态门禁和破坏性变更豁免流程，必须补齐 expand/migrate/contract 与数据责任人复核 |
| 日志噪声与丢失 | 部分接受，P1 | 设计已区分普通日志和可靠审计；实现仍缺独立高优先级通道。拒绝默认在请求线程同步写远程/磁盘 |
| `TenantId` / `TenantID` | 接受，P2 | 新增或触碰文档统一使用 `TenantId`；不为此制造大范围无价值改动 |
| 测试命令改为 `dotnet test` | 不接受 | 当前使用 Microsoft.Testing.Platform 可执行测试宿主，直接运行 DLL 并带最小测试数门禁是有意设计，可防止零发现假成功 |
| AI/Agent 内容过早 | 部分接受，P2 | 保留 M5+ 安全边界，但能力状态必须明确为 `Planned`，不得挤占近期底座硬化 |
| 能力范围容易高估 | 接受，P0 | 新增唯一总览状态矩阵，明确当前主要落地范围和证据，不再要求读者拼接多个文档判断 |
| 模块生命周期脱节 | 接受，P0 | `IFullNetModule.InitializeAsync` 当前没有统一调用链，接口与行为不一致 |
| 宿主装配漂移 | 接受，P0 | Api/Migrator 手工注册完整模块，Worker 使用专用入口；需要显式 Host Profile/Manifest 和一致性测试，不采用全程序集扫描 |
| 双管理端逻辑复制 | 接受，P1 | HTTP、会话、导航白名单存在镜像实现；提取纯 headless 策略和协议夹具，保留 Vue/Layui 框架适配 |
| 浏览器 E2E 真实后端不足 | 接受，P1 | API 集成测试已覆盖真实 Host/CORS，但 Playwright 主要 Mock API；两者不能互相替代 |
| 多客户端共享不足 | 部分接受，P2 | 跨平台共享稳定协议、错误码、OpenAPI 模型和测试夹具；不强行共享 UI、存储或平台认证实现 |
| 前端版本分叉 | 部分接受，P2 | uni-app/DCloud 与 Vue Admin 有不同兼容窗口；采用“兼容性队列”治理，不追求所有包同版本 |

## 3. 可靠消息演进

### 3.1 版本兼容模型

Outbox 继续以 `(MessageType, SchemaVersion)` 路由，不用 CLR 类型名充当永久协议。每个事件必须声明稳定消息类型、正整数版本和内容类型。整数 Key 只能尾部追加，已发布编号永久保留。

当事件发布第二个版本时，模块必须在以下两种方式中明确选择：

- 在支持窗口内并行保留 V1/V2 Handler；或
- 注册逐级升级链，把 V1 载荷升级为当前规范版本后交给当前 Handler。

升级链按原始事件类型与相邻版本注册，不设计一个只接受当前 `T` 的 `IOutboxMessageUpgrader<T>`，因为旧载荷在进入当前 DTO 前就可能无法安全反序列化。具体接口在实施时以独立旧版本 DTO 或受限二进制转换为输入，禁止 Typeless/Contractless。

### 3.2 发布与退役

事件版本升级使用“先消费者、后生产者、最后退役旧消费者”顺序。旧版本支持期至少覆盖：最长 Outbox 保留期、最长失败重试期、蓝绿回滚窗口三者的最大值。没有队列/表扫描证据不得删除旧 Handler 或 Upgrader。

失败必须区分瞬时错误、永久契约错误和处理器缺失。超过最大尝试次数或确认不可重试的消息进入可查询死信状态，不得永久阻塞同批消息；保留 `MessageId`、租户、Trace、错误摘要、尝试次数和人工重放审计。

## 4. 双数据库 SQL 边界

### 4.1 可移植子集优先

普通 CRUD 优先使用两库共同支持的参数化 SQL。以下能力不直接散落在业务 Handler：分页、批量写入、Upsert、锁与跳过锁、CTE/窗口函数、JSON 路径/聚合、日期函数、大小写/排序规则、返回生成值。

### 4.2 专有能力准入

确有性能或原子性需要时允许 Provider 专用 SQL，但必须同时具备：

1. 同一语义名称下的 SQL Server/MySQL 两份实现；
2. 明确的输入、输出、并发和空值语义；
3. 两库真实集成测试和必要的执行计划/索引说明；
4. 业务 Handler 只选择语义，不拼接数据库函数；
5. 缺任一 Provider 实现时编译或测试失败，而不是运行时静默回退。

`ISqlDialect` 只提供小而稳定的语法原语；复杂查询使用 Provider Statement Catalog，不演化成囊括全部数据库能力的“上帝接口”。SQL Server `MERGE` 不作为默认 Upsert；JSON 聚合、检索和局部更新默认在应用层完成，只有基准证明收益且双库语义可控时通过 ADR 准入。

## 5. 缓存一致性等级

| 等级 | 典型数据 | 规则 |
|---|---|---|
| S0 安全关键 | 权限、用户禁用、安全戳、租户启停/到期、API Key、Session | Fail-Safe 关闭；授权决定不得只依赖可能陈旧的 L1；写事务提交后同步清除当前进程，再写/保留 Outbox 负责跨节点修复；使用极短 TTL 或直接查权威存储 |
| S1 业务关键 | 配置开关、套餐限制、工作流状态 | 明确最大陈旧窗口；提交后本机失效 + Outbox/Backplane；必要时关键写后读走权威存储 |
| S2 展示/参考 | 字典、只读投影、非安全统计 | 可使用有界 Fail-Safe、Eager/Background Refresh，但必须声明最大陈旧时间 |

Background Refresh 只优化延迟，不证明正确性。多实例验证必须覆盖“数据库已提交但失效尚未消费”、Redis 不可用、Worker 延迟和节点持有旧 L1 的场景。

## 6. 模块生命周期与宿主 Profile

继续坚持显式注册，不采用大范围反射扫描。建立一个由代码明确声明的模块目录，每个模块描述：名称、依赖、适用宿主、完整服务入口、Worker 最小入口、Endpoint 能力和是否需要初始化。

宿主使用 Profile 表达意图：

- `Api`：完整 HTTP 模块和 Endpoint；
- `Worker`：明确的后台 Handler/Consumer 最小集合；
- `Migrator`：迁移、Seed Contributor 和必要领域服务；
- `Test`：在生产 Profile 上追加测试专用能力。

`InitializeAsync` 必须在 Host 启动后、接收业务流量前，按依赖拓扑恰好调用一次；失败阻止 Host 就绪。它只用于幂等的运行时初始化/自检，不执行 Migration、Seed 或不可回滚外部副作用。若第一个真实使用者设计时仍无必要，应删除悬空钩子，而不是长期保留无行为接口。

## 7. 会话与浏览器并发

Refresh 的租户来源以服务端 Session 记录为唯一权威；请求中的旧 Access Token/JWT `tenant_id` 不得覆盖它。上下文切换与 Refresh 并发时，以持久化 Session Version 形成线性化顺序，并在重试后重新验证用户、租户、权限和安全戳。

浏览器端当前单实例的 single-flight 不能覆盖多个 Tab。优先使用 Web Locks；不支持时使用 BroadcastChannel 协调“刷新中/已更新/已退出”状态。服务端是否提供极短重放宽限必须单独安全评审，不得为了跨 Tab 体验削弱 Refresh Token 重用检测。

## 8. 双管理端 headless 共享边界

Vue 与 Layui 保持功能、权限、租户、错误、关键流程和 E2E 对等；允许控件、布局和复杂交互形式不同，但必须给出可完成相同业务目标的等价路径。不得把“Layui 降级”解释为删除功能或绕过门禁。

可共享的无框架 ESM 层仅包含：ProblemDetails 解析、稳定错误/权限代码、Session 状态转换、刷新重试策略、导航白名单语义、OpenAPI DTO/协议夹具。不得共享 DOM 操作、Vue Store、Layui 组件、路由器实例或框架生命周期。Vue 迁入 Art Design Pro 时也必须经过这层契约，不得用模板请求/认证实现绕过；Layui 增加 JSDoc + `checkJs` 类型门禁。

Web Components 暂不引入。只有出现至少两个真实、复杂、跨端复用且现有 headless 层无法解决的交互组件，并完成可访问性、体积和维护成本评估后再建 ADR。

## 9. 业务内容多语言参考结构

系统错误、权限、菜单元数据和枚举继续使用稳定代码与资源文件。需要持久化翻译的业务实体由所属模块维护规范化表，例如：

```text
{owner}_{module}_{entity}_translation
TenantId? + EntityId + Locale  唯一
Name / Description / OtherTypedTranslatedFields
Version + CreatedAtUtc + UpdatedAtUtc
```

Full.NET 官方模块使用 `fn`，项目业务模块使用脚手架阶段冻结的 OwnerKey；租户实体必须包含租户边界。Fallback 在应用层按统一语言链执行。翻译字段保留明确列和索引，不建立跨模块 `fn_i18n_resources` EAV。默认也不在核心表增加 `Name_ZhCN/Name_EnUS` 列。只有不参与筛选、排序、唯一性和局部更新的长描述内容，才可在模块 ADR 与双库测试后使用 JSON。所有对象命名服从 [`rules/naming-conventions.md`](../../../rules/naming-conventions.md)。

## 10. SQL 变更安全

数据库变更采用 `expand -> migrate/backfill -> contract`。发布脚本默认禁止：

- `DROP TABLE/COLUMN`、`TRUNCATE`、直接重命名；
- 缩窄类型、直接增加无默认值的非空列；
- 应用 SQL 中 `SELECT *`；
- 无 `WHERE` 的 `UPDATE/DELETE`；
- 未分批的大表回填和未说明锁影响的索引/约束变更。

确有必要时必须提交机器可检查的临时豁免，包含数据库、脚本、风险、备份/验证、回滚或前滚策略、指定数据责任人、到期版本。开源协作不强制角色名称为 DBA，但破坏性变更必须有独立数据审查者；CI 静态扫描和双库集成测试仍是硬门禁。

同一 SQL 门禁还必须加载 Naming Profile：拒绝 `sys` 项目 OwnerKey、运行时动态表前缀、非规范表/列/约束名、超过共同 64 字符上限的数据库对象和 SQL Server/MySQL 命名漂移。存量名称只能由精确、带退役里程碑的债务清单放行。

## 11. 日志与审计可靠性

普通运行日志采用双通道：Debug/Information/常规 Warning 进入有界批量异步通道；Error/Critical 进入有独立容量、独立指标和本地短期 Spool/可靠 Sink 的高优先级通道。高优先级通道耗尽时触发健康降级和告警，不默认阻塞请求线程同步写网络或磁盘。

Audit 不是日志等级。登录、授权、资金、租户、配置和 Agent 副作用审计继续通过数据库事务或 Outbox 保存，并拥有独立保留、查询和外部导出 Provider；Kafka/Event Hub 可作为 Provider，不成为核心强制依赖。

## 12. 真实链路 E2E 与工具链

保留 Mock Playwright 作为快速、确定的双端 UI 契约测试，另增加最小真实栈：真实 API + 真数据库 + Redis + Vue/Layui。真实栈至少验证 HTTPS/反向代理假设、精确 CORS、Cookie、CSRF、登录、刷新、并发租户切换、退出和 ProblemDetails，不用 Mock Route 替代。

前端依赖按兼容性队列治理：Admin Web、共享包、uni-app/DCloud、E2E 各自记录受支持版本和上游约束。目标是同一队列内部一致、跨队列协议兼容，不是强制所有包使用同一 Vue/TypeScript/Vite 版本。跨客户端只共享 OpenAPI 模型、稳定代码和协议夹具，不共享平台存储或 UI。

## 13. 与既有基线的关系

- 本文补充而不替代总体架构设计；冲突时以更新日期较新的明确决策为准，并应回写总体架构摘要。
- Seed 继续服从既有 Baseline/Overlay 规格；本次只澄清不提供通用 Down。
- Admin.NET 功能对标和 Vue/Layui 双端对等要求不降低。
- AI/Agent 继续保持 M5+ 规划状态，近期实施优先级以后续硬化计划为准。
