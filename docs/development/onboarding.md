# Full.NET 人类阅读入口（Onboarding）

面向新加入的开发者与审查者。AI 代理仍以根目录 [`AGENTS.md`](../../AGENTS.md) 与 [`rules/`](../../rules/README.md) 为准；本文提供**最短阅读路径**、文档权威关系与**完整仓库地图**，描述意图，不复制强制规则正文。

## 1. 五分钟定位

| 问题 | 答案入口 |
|---|---|
| 现在到底能用到什么？ | [`docs/roadmap/capability-status.md`](../roadmap/capability-status.md)（唯一能力总览） |
| 和 Admin.NET 差多远？ | [`docs/roadmap/adminnet-feature-parity.md`](../roadmap/adminnet-feature-parity.md)（长期对标，≠ 已交付） |
| 怎么在本机跑起来？ | [`getting-started.md`](getting-started.md) |
| 架构为什么是模块化单体？ | [`ADR-0002`](../architecture/adr/ADR-0002-modular-monolith-evolution.md) |
| 写代码前读哪些规则？ | [`rules/README.md`](../../rules/README.md) → 至少 `development-quality`；动库/API/机器码再加 `naming-conventions`；动前端再加 `client-frontend` |
| Host.Api 怎么保持 Native AOT？ | [`native-aot-development-guide.md`](native-aot-development-guide.md)；强制边界见 [`rules/native-aot.md`](../../rules/native-aot.md) |
| 新增模块怎么交付？ | [`.agents/skills/fullnet-module-delivery`](../../.agents/skills/fullnet-module-delivery/SKILL.md) |

当前阶段一句话：**M2 安全与基础设施底座已可用；完整后台 CRUD 仍在路线图。** 禁止把路线图上的 `Mapped`/`Planned` 说成已交付。

## 2. 文档权威分层（防漂移）

| 层级 | 目录 | 怎么读 |
|---|---|---|
| 强制规则 | `rules/*.md`、`AGENTS.md` | **当前必须遵守**；与实现冲突时不得假装已合规 |
| 重大决策 | `docs/architecture/adr/` | 单项取舍与后果；冲突时先对齐 ADR |
| 已批准设计 | `docs/superpowers/specs/` | 长期基线与验收条件 |
| 实施步骤 | `docs/superpowers/plans/` | 可执行任务；勾选 ≠ 已验证 |
| 事实与审查 | `docs/verification/` | 带日期/基线的评估与测试证据；建议不自动改架构 |

`rules/` 与某份历史 `specs/` 若表述重叠：**以 `rules/` 与最新已批准 Spec/ADR 为准**，历史 Spec 是设计过程，不是第二套强制源。

详见 [`ADR-0001`](../architecture/adr/ADR-0001-document-artifact-governance.md) 与 `development-quality` §12.1。

## 3. 建议阅读顺序（首日）

1. 本文 + [`capability-status.md`](../roadmap/capability-status.md) 第 2–4 节。
2. [`getting-started.md`](getting-started.md)：构建、分层测试、AppHost、双库切换。
3. [`ADR-0002`](../architecture/adr/ADR-0002-modular-monolith-evolution.md) + 架构 Spec 中与你工作相关的章节（数据/安全/Outbox）。
4. 若改 Identity/会话：会话基础 Spec + 超级管理员 Spec。
5. 若改前端：[`client-frontend.md`](../../rules/client-frontend.md) + [`client-delivery-roadmap.md`](../roadmap/client-delivery-roadmap.md)。
6. 若改 Host.Api 可达闭包、JSON/配置源生成、Dapper AOT 或 native Provider：[`native-aot-development-guide.md`](native-aot-development-guide.md) + [`native-aot.md`](../../rules/native-aot.md)。
7. 动手前打开对应 `plans/`，按任务做，不要从零发明目录结构。

## 4. 仓库地图（按需下钻）

本节面向人类读者，解释每个子项目和关键目录"是什么、为什么这样设计"。
AI 代理仍以 `AGENTS.md` 和 `rules/` 为权威源；本节描述意图，不复制规则正文。

### 4.1 整体分层一览

```text
┌─────────────────────────────────────────────────────────────┐
│  src/Hosts/AppHost          仅 Dev，.NET Aspire 编排         │
├──────────────┬──────────────┬──────────────────────────────┤
│  Host.Api    │  Host.Worker │  Host.Migrator               │
├──────────────┴──────────────┴──────────────────────────────┤
│  src/Composition/           模块装配层（唯一权威清单）        │
├──────────────┬──────────────┬──────────────────────────────┤
│  Modules/    │  Modules/    │  Modules/...                  │
│  Identity    │  Tenancy     │  Organization / Settings 等   │
├──────────────┴──────────────┴──────────────────────────────┤
│  src/BuildingBlocks/        横切基础设施（16 个项目）         │
│  第一层：Abstractions（零依赖）                              │
│  第二层：*.Abstractions（依赖第一层接口契约）                │
│  第三层：具体实现（Dapper / MySql / Fusion / DbUp / …）      │
└─────────────────────────────────────────────────────────────┘

前端：ui/admin（Vue 3）  ui/admin-layui（Layui）  clients/uniapp
共享：packages/@fullnet/client-contracts  admin-i18n  design-tokens
```

**依赖方向严格单向**：Hosts → Composition → Modules → BuildingBlocks。
违反方向的引用会被 `tests/Full.NET.ArchitectureTests` 自动拦截。

---

### 4.2 src/BuildingBlocks/ — 横切基础设施

按依赖层次从底向上阅读，不按字母顺序。

#### 第一层：零依赖基础

**`Full.NET.Abstractions`**
整个依赖图的最底层，零 NuGet 依赖，只有接口和值类型。
所有上层项目都依赖它，因此它必须保持零依赖——一旦引入框架包，所有模块都会被迫跟随升级。
关键类型：
- `Result<T>` — Railway-Oriented 错误处理，业务流程不用异常驱动，失败路径有类型保证
- `ICurrentTenant` — 普通业务使用的只读租户上下文；只有经过架构门禁登记的请求解析、Worker、Migrator 与跨租户编排边界可以注入 `ICurrentTenantContextWriter`
- `UuidV7` — 应用端生成 UUID v7 主键，不依赖数据库自增，分布式友好且有序

#### 第二层：领域抽象（依赖 Abstractions）

**`Full.NET.Data.Abstractions`**
数据访问的接口契约层。业务模块只依赖此层，不直接依赖 Dapper，
目的是将来替换实现时业务代码零改动。
关键接口与类型：

| 类型 | 作用 |
|---|---|
| `IQueryExecutor` | SELECT 查询，返回只读结果 |
| `ICommandExecutor` | INSERT / UPDATE / DELETE，返回影响行数 |
| `IMultiResultQueryExecutor` | 一次往返取多张表，减少数据库往返次数 |
| `SqlStatement` | SQL 四元组 `(Name, Text, Scope, TenantBinding)`，每条 SQL 可追踪、可审查、可测试 |
| `SqlDataScope` | 枚举：`Global` / `TenantRequired` / `HostOnly`，SQL 层面强制租户隔离边界 |
| `SqlTenantBinding` | 枚举：`None` / `CurrentTenantId`，声明执行器是否注入受信任的当前租户参数 |
| `IOutboxWriter` | Outbox 写入抽象，业务代码通过此接口发布事件，与消息中间件解耦 |
| `IOutboxStore` | Outbox 读取抽象，后台 Worker 通过此接口轮询待发事件 |

**`Full.NET.Seeding.Abstractions`**
种子数据的接口契约。`IDataSeedContributor` 是所有种子贡献者必须实现的接口。
Migrator 宿主和测试宿主都需要播种，抽象层使两者可以独立演进而不耦合实现。

**`Full.NET.Realtime.Abstractions`**
实时推送的接口契约。业务模块通过 `IRealtimeNotifier` 发送通知，
不直接依赖 SignalR，方便将来换为 WebSocket 或 SSE 而不改业务代码。

#### 第三层：具体实现

**`Full.NET.Data.Dapper`**
`IQueryExecutor` 等接口的 Dapper 实现。
业务模块**禁止**直接引用此项目，必须通过 `Data.Abstractions` 的接口，
这个边界由 `ArchitectureTests` 自动门禁；细则见 `rules/development-quality.md`。

**`Full.NET.Data.MySql`**
MySQL 特有的类型映射和连接工厂。
Full.NET 同时支持 SQL Server 和 MySQL，此项目封装了两者的差异
（如 UUID 字节序、GUID 存储模式），上层代码不感知数据库类型。

**`Full.NET.Migrations.DbUp`**
基于 DbUp 的纯 SQL 迁移引擎，迁移脚本位于各模块的 `sql/` 子目录。
选择纯 SQL 而非迁移 DSL 的原因：SQL 对 DBA 完全透明，可直接审查执行计划，
双库兼容性完全可控，不存在 ORM 方言翻译的黑盒风险。

**`Full.NET.Caching.Fusion`**
FusionCache 的封装，通过 `.AsHybridCache()` 同时暴露 `IFusionCache` 和
`IHybridCache` 两个抽象，业务代码可以选择任意一个使用。
**安全关键缓存**（权限、会话）全局关闭 Fail-Safe，防止读取陈旧的安全数据。

**`Full.NET.Seeding.Dapper`**
`IDataSeedContributor` 的 Dapper 实现基类，提供幂等播种的公共逻辑。
种子数据采用"生产安全 Baseline + 环境 Overlay"策略：
Production 只允许 Baseline，测试专用数据不得进入发布物。

**`Full.NET.Realtime.SignalR`**
`IRealtimeNotifier` 的 SignalR 实现，Hub 注册和连接管理在此封装。
业务模块不感知 SignalR 细节。

**`Full.NET.Serialization.MemoryPack`**
高性能二进制序列化（MemoryPack 源生成），用于 Outbox 消息体等内部高吞吐场景。
对外 HTTP API 使用 System.Text.Json，两者边界明确，不混用。

**`Full.NET.Validation.FluentValidation`**
FluentValidation 的管道集成，将验证失败统一转换为 `Result<T>` 错误，
而不是抛出异常，保持 Railway 错误处理的全链路一致性。

**`Full.NET.Localization`**
多语言基础设施，全栈使用规范 BCP 47 语言标签。
**业务逻辑不得依赖翻译文本**（如用翻译后的字符串做条件判断），
只能依赖稳定机器码；细则见 `rules/development-quality.md`。

**`Full.NET.Hosting`**
宿主启动的公共扩展方法，包括中间件注册顺序、健康检查、CORS 基础配置。
各宿主 `Program.cs` 通过调用此项目的扩展方法完成装配，
避免三个宿主各自复制相同的启动代码导致漂移。

**`Full.NET.Modularity`**
模块化框架的核心，定义模块生命周期契约。
关键接口：
- `IFullNetModule` — 所有业务模块必须实现，包含 `AddServices` 和 `UseMiddleware` 两个生命周期方法
- `IModuleCatalog` — 模块清单抽象，`Composition` 层提供唯一实现，宿主不得自行维护清单

**`Full.NET.Data.CodeGeneration`**
Roslyn Source Generator，编译期为 `SqlStatement` 生成类型安全的包装代码，
减少手写样板，同时保持每条 SQL 完全可见、可追踪。

---

### 4.3 src/Modules/ — 业务模块

每个业务领域由"主项目 + 可选 Contracts 项目"组成。
**Contracts 项目**只包含对外契约（DTO、接口、枚举），其他模块可以引用，
但**禁止**引用主项目——这是模块间隔离的物理边界，违反时架构测试报错。

当前能力状态以 [`capability-status.md`](../roadmap/capability-status.md) 为准，下文不重复标注状态数字。

**`Full.NET.Modules.Identity` + `Identity.Contracts`**
认证与授权的核心模块，当前功能最完整。
职责：用户账号、角色、权限、JWT 签发、会话管理、TOTP 二次验证、登录限流。

目录结构说明：
```
Identity/
├── Features/         按用例垂直切片（Login、Logout、RefreshToken、GetPermissions…）
│   └── 每个功能/     Handler + Endpoint + Validator 三件套，互不干扰
├── Security/         JWT 验证、安全戳校验、会话状态拦截
├── Authorization/    RBAC 权限目录，动态投影 Endpoint 精确权限
├── Persistence/      Identity 相关的 Dapper SQL 查询实现
├── Seeding/          默认角色、超级管理员账号的生产安全 Baseline 种子
└── IdentityModule.cs 模块入口，实现 IFullNetModule，注册所有服务
```

关键设计决策：
- 超级管理员属于受保护的系统角色，不得绕过租户隔离和精确权限检查；细则见 `rules/development-quality.md`。
- 会话状态校验在中间件层而非 Handler 层，确保所有 Endpoint 统一受保护。

受邀入驻选择注册方式后，成员激活使用当时有效的租户业务角色配置，在 Identity 本地事务内授予角色并保存组织分配事件。账号的 Host 角色不受影响。机构和可选职位由 Organization Worker 最终一致地补齐普通隶属，不改变主部门/主职位，也不重启人工停用的关系。

开放注册同样创建统一账号，并在一个 Identity 事务中登记 Pending 成员和持久席位任务。登录后从企业切换页查看本人入驻进度，API 或 Worker 完成席位确认后才激活成员并赋予注册方式的业务角色。暂时失败可继续原任务，不重复创建账号或成员；管理员改变原成员后不能通过旧注册任务恢复。前端后台刷新仅查询进度，继续办理始终由用户显式触发。

组织分配意图位于 `fn_organization_registration_assignment`：`Pending` 表示待处理或等待重试，`Completed` 表示已处理，`Skipped` 表示原成员已撤销或激活版本发生变化。Worker 每五秒读取有界批次；机构或职位不可用时保留稳定错误码并延迟三十秒重试，修复原配置后由 Worker 接续。成员权威检查在组织事务外，组织关系和意图完成标记在本模块同一事务中提交。

**`Full.NET.Modules.Tenancy`**
多租户管理模块，负责租户创建、配置和隔离边界。
无独立 Contracts 项目，因为租户上下文抽象已在 `Abstractions` 层定义。
其他模块通过 `ICurrentTenant`（来自 `Abstractions`）获取当前租户，
不直接依赖此模块，避免隐式循环依赖。租户上下文写能力通过
`ICurrentTenantContextWriter` 单独暴露，并由 Architecture Tests 对生产使用点做精确登记；
普通 Endpoint、Handler 和领域服务不得根据请求输入直接切换租户上下文。

Host 企业目录的“企业生命周期”入口使用独立的读取、暂停、恢复和关闭权限。暂停保留数据，恢复重新校验商业条件；关闭仅终止企业访问并保留历史数据，不自动退款、删除账号或清理存储。关闭必须输入企业编码确认，版本冲突后先刷新状态再由用户重新确认。

所有者职责在当前企业的成员目录独立交接。尚未建立 Owner 时，由持久化超级管理员进入目标企业，以成员读取及 `identity.tenant_members.transfer_ownership` 权限、密码和环境要求的强认证，指定活动成员为首位 Owner。已有 Owner 时，仅该 Owner 可向另一名活动 Member/Admin 交接；窗口展示重新读取的双方身份与版本，冲突后先人工刷新再确认。原 Owner 转为 Admin，接收成员转为 Owner；业务 RBAC 授权仍独立管理，不会因交接自动增删业务角色。历史 Tenancy `OwnerUserId` 仅为只读元数据，不作为职责或交接成功依据。

Production 必须启用合格的强认证提供程序（内置 TOTP 对应 `Identity:EnableTotpStrongReauthentication=true`），操作者先完成 TOTP 注册，再提交当前密码及验证码；仅密码提供程序不能在 Production 执行交接。交接及不可变回执在 Identity 同一本地事务提交，回执不含验证凭据。关闭或超时后保留原操作供人工核对，核对不到回执仍表示结果未知，不自动重发；401 不刷新后重放。Owner 不能直接退出、移除、停用、退役或变更账号类型，先交接其所有企业职责；交接一家企业不会转移另一家的职责。

“租户上下文 → 退出当前租户”先读取本人在当前企业的成员状态和版本，再明确确认。所有者需要先完成交接，不能直接退出。并发冲突后手动读取新状态并重新确认；在途关闭只取消客户端等待，未知结果不会自动重发。退出确认成功后返回 Host；若切换上下文失败，可使用“返回 Host”重试切换，不重复退出。此操作只结束当前企业的成员身份，不删除全局账号或其他企业成员资格。切换账号、会话、企业或离开页面会销毁旧确认。

企业成员目录的“移除”先核对指定成员的当前身份与版本，再确认删除其本企业成员资格。所有者不可移除；版本冲突后先人工核对，再明确使用新版本。关闭在途窗口只停止客户端等待，重开原成员会保留未知结果，不自动重读或重发；人工核对已 Removed 时只报告当前事实，不再次移除。该操作不删除全局账号，也不改变其他企业身份。列表在会话、企业或读取权限变化时清空并取消旧读取，迟到结果不能回填。

退出或移除后，原企业的实时连接由服务端周期核证关闭，原凭据不能重新加入；无需等待客户端处理撤销消息，也不会关闭另一企业的独立会话连接。当前连接每 5 秒核证，独立授权租约上限 30 秒，不能把成员写入成功解释为所有网络连接已在同一瞬间断开。

成员退出后，其原企业的排队报表导出会在执行核证时转为授权失效终态；生成过程中丢失资格也会在上传前拒绝，不能把旧权限快照作为继续出文件的依据。这类终态不自动重新领取；另一企业中仍有资格的独立会话和任务保持有效。

导入的源文件上传、预览和排队不构成永久执行许可。后台领取和 Schema 逐行写入继续核对原会话及当前企业资格，退出 A 后拒绝 A 的后续职位写入并结束失效任务，B 的独立任务仍可执行。成员已退出时，旧凭据也不能读取该企业的错误回执。

企业暂停与成员退出具有不同恢复语义：暂停期间未领取的导入保持原队列，不推进状态或检查点；处理中失去企业资格时拒绝后续业务写入，也不能上传新错误回执。原租约及检查点保留，恢复启用且原会话、成员和权限仍有效后，未领取任务由调度继续，处理中任务须等原租约到期才可重领。无需反复点击重试或复制上传；另一企业的独立任务不会被重新执行。

邀请目录的“撤销”先读取当前邀请，核对邮箱、受邀身份、签发时间、有效期和版本后再确认。仅 Pending 邀请允许撤销；已经接受的邀请不能通过撤销移除成员，应使用成员移除流程。重复撤销返回原有状态及版本，不再次推进；接受与撤销竞争时仅一个终态成功。在途关闭后先人工核对当前状态，看到 Revoked 即可确认事实，不自动重发。详情与操作都受当前企业及独立权限保护，其他企业中的同一邀请 ID 视为不存在。

邀请页选择“已有账号”后，使用原密码登录或前往身份中心完成原有认证；成功后返回已授权的企业上下文，用户仍需手动接受邀请。身份中心往返只在当前标签页保留 15 分钟的一次接续目标，不携带邀请令牌到登录 URL，不支持任意跳转地址；认证失败或缺少企业入口时回到正常落地页。MFA 的校验继续由身份中心负责，这一接续功能不替代实际 MFA 旅程验收。

资料邮箱不是邀请归属证明；未凭令牌认领的邀请不会仅因邮箱相同而出现在本人邀请列表。请保留原邀请入口，完成原有 MFA 后明确使用该邀请令牌接受，再等待本人成员 Active 并进入企业。认证成功不会自动认领邀请或授予企业成员资格。

旧运营启用只允许恢复已完成开通且生命周期为 Active 的企业；Suspended 必须走恢复操作，Closing/Closed 不可重新启用。暂停、关闭和运营禁用共享 Tenancy 本地事务门闩，保留最后一家活动企业；关闭其他已停用企业不受该数量保护阻断。门闩由双库 362 迁移创建，`tenancy.lifecycle.guard_missing` 表示应修复迁移固定行后再试，不能跳过保护。企业关闭不等于禁用全局账号，在其他企业的成员资格仍由各自权威状态决定。

企业操作失败后，先区分已知拒绝、结果未知和已经提交后的补读失败，再决定接续入口：

| 场景 | 核对与继续方式 |
|---|---|
| 邀请已过期、被撤销或被其他会话接受 | 读取原邀请当前事实；Pending 且仍有效时才再次明确接受。需要新邀请时由有权限管理员签发，不修改旧终态。 |
| 入驻仍 Pending 或席位确认暂时失败 | 在本人入驻进度中继续原任务，先解决原错误；不重复注册账号。仅 Active 成员才能进入企业，组织分配状态不替代成员资格。 |
| 所有者交接超时或关闭在途确认 | 使用原操作的回执核对双方身份；未取得回执仍属未知。保留原意图，不因列表已变化而伪造成功，不重新提交另一笔交接。 |
| 退出成功但未返回 Host | 使用“返回 Host”重试上下文切换。先读到 Removed 时按现有事实恢复，不再次退出，不禁用全局账号。 |
| 移除或生命周期操作版本冲突 | 人工重读指定成员或企业状态，以新版本重新确认；旧窗口和旧确认不能继续使用。 |
| 暂停后恢复被商业条件拒绝 | 先在原订阅/额度入口处理拒绝原因，再读取最新企业版本并显式恢复；运营启用不能绕过生命周期和商业校验。 |
| 已成功的操作随后读取失败 | 保留成功回执，只重读原对象，不以读取失败推导写入失败或重放写请求。 |

运维接续应记录企业 ID、原任务或操作 ID、稳定错误码及发生时间。会话、成员、席位和企业生命周期各有独立权威状态；通过既有管理入口修复所属状态，不直接改表拼出“成功”，也不把另一企业的授权或平台切换权限补到普通成员上。无法确认的写入保持未知，由原功能的核对入口处理。

**`Full.NET.Modules.Organization` + `Organization.Contracts`**
机构 / 部门树管理。Contracts 项目供其他需要"机构"概念的模块引用，
避免这些模块直接依赖 Organization 主项目。

**`Full.NET.Modules.Settings` + `Settings.Contracts`**
系统配置与参数管理，支持租户级别的配置覆盖。
通过 Contracts 暴露配置读取接口，业务模块可以读取配置但不感知存储细节。

**`Full.NET.Modules.Auditing`**
操作审计日志，记录关键业务操作的执行人、时间、变更内容。
以后台写入为主，不阻塞业务主流程。

**`Full.NET.Modules.Files`**
文件存储管理，抽象本地存储和对象存储的差异，
业务模块通过统一接口上传/下载，不感知底层存储位置。

**`Full.NET.Modules.Jobs`**
后台任务调度，封装定时任务的注册和执行监控。

**`Full.NET.Modules.Notifications`**
消息通知，支持站内信、邮件等多种通知渠道。
通道扩展通过接口而非修改核心代码实现。

---

### 4.4 src/Composition/ — 模块装配层

**`Full.NET.Composition`**
这是整个项目中**唯一知道所有模块的地方**，是模块注册的单一权威源。

关键文件：
- `FullNetModuleCatalog.cs` — 模块清单，定义所有业务模块的注册顺序。
  各宿主**禁止**自行维护模块列表，防止宿主间注册漂移（Api 有 Worker 没有之类的问题）。
- `FullNetHostProfile.cs` — 按宿主角色（Api / Worker / Migrator）定义不同的模块子集。
  Worker 只加载后台服务相关模块，不加载 HTTP 中间件，减少不必要的依赖和暴露面。

**设计原因**：如果每个宿主的 `Program.cs` 各自注册模块，三个宿主之间极易出现漂移，
且难以在 Code Review 中发现。集中到 Composition 层后，增删一个模块只改一处，
架构测试可以验证每个宿主加载的模块集合符合预期。

---

### 4.5 src/Hosts/ — 运行宿主

四个宿主按运行角色严格分离，禁止合并，每个宿主有其独立的启动语义。

**`Full.NET.Host.Api`**
对外提供 HTTP API 的主宿主，加载完整模块集，包含所有 HTTP 中间件。
生产部署的主要进程，水平扩展时直接扩此宿主。

**`Full.NET.Host.Worker`**
后台任务宿主，只加载后台服务相关模块，不启动 HTTP 监听。
**分离原因**：后台任务不需要 HTTP 中间件（认证、限流、CORS 等），
分离后可独立扩缩容，也避免后台任务意外暴露 HTTP 端点。

**`Full.NET.Host.Migrator`**
数据库迁移宿主，启动后执行迁移和种子数据，**然后退出（exit code 0）**，不对外提供服务。
**分离原因**：迁移必须在 Api 和 Worker 启动前完成；
独立宿主可以在 Kubernetes Init Container 或 CI 流水线中精确控制执行时机。
**Api 和 Worker 禁止**在自身启动时自动执行迁移。

**`Full.NET.AppHost`**
.NET Aspire 编排宿主，**仅用于本地开发环境，不进入生产部署**。
负责启动 Api、Worker、Migrator 以及依赖的基础设施（数据库、Redis 等），
提供统一的开发仪表板，让本地多进程协调变为一键启动。

---

### 4.6 tests/ — 测试分层

四层测试各有明确职责，不可互相替代。

| 项目 | 职责 | 依赖 IO | 执行速度 |
|---|---|---|---|
| `Full.NET.UnitTests` | 纯逻辑测试，覆盖 Handler / Domain / 工具类 | 无 | 毫秒级 |
| `Full.NET.IntegrationTests` | 真实双库（SQL Server + MySQL）连接测试，验证 SQL 兼容性 | 数据库 | 秒级 |
| `Full.NET.ArchitectureTests` | 依赖方向、命名规范的机器门禁，防止架构退化 | 无 | 秒级 |
| `Full.NET.CompatibilityTests` | Admin.NET 适配层契约测试，防止兼容层静默破坏 | 无 | 秒级 |

**为什么需要 IntegrationTests**：Dapper 使用显式 SQL，两个数据库的 SQL 方言存在差异，
必须用真实连接才能发现兼容性问题，Mock 无法替代真实数据库行为。

**为什么需要 ArchitectureTests**：规则是文字约定，ArchitectureTests 是机器执行的约定。
典型断言：`BuildingBlocks` 不得依赖 `Modules`，
`Contracts` 项目不得依赖 ASP.NET Core，
测试专用 Seed Contributor 不得出现在发布物中。
这层测试是架构规则的自动执行者，防止架构随时间退化。

---

### 4.7 前端与共享包

**`ui/admin`**（Vue 3 主管理端）
使用 Vite 构建，面向现代浏览器，提供完整的后台管理界面。
技术选型和 UI 组件库边界见 [`rules/client-frontend.md`](../../rules/client-frontend.md)。

**`ui/admin-layui`**（Layui 对等管理端）
与 Vue 端功能同步，面向需要轻量 HTML/JS 部署的场景（无需 Node.js 构建环境）。
**所有后台模块必须在两端同步开发，两端都通过验收才能标记 `Verified`**；
细则见 [`rules/client-frontend.md`](../../rules/client-frontend.md)。

**`clients/uniapp`**
uni-app 多端基础，面向移动端和小程序场景，当前业务页面较少。

**`packages/@fullnet/client-contracts`**
前端 headless 层，包含 API 契约类型定义和请求函数。
Vue 端和 Layui 端共享此包，确保两端 API 调用逻辑一致，
防止业务逻辑在两端各自实现后出现漂移。

**`packages/@fullnet/admin-i18n`**
前端多语言资源包，Vue 端和 Layui 端共享，
避免翻译文本重复维护，也避免两端翻译不一致。

**`packages/@fullnet/design-tokens`**
设计令牌（颜色、间距、字体等），两端共享，保证视觉一致性。

---

### 4.8 docs/ — 文档体系

文档按权威层级分层，不同层级有不同的修改门槛；
完整治理规则见 [`ADR-0001`](../architecture/adr/ADR-0001-document-artifact-governance.md)。

| 目录 | 内容 | 权威级别 |
|---|---|---|
| `docs/architecture/adr/` | 架构决策记录，单项取舍与后果，变更需评审 | 高 |
| `docs/superpowers/specs/` | 已批准的长期架构设计规格，是实现的验收基线 | 高 |
| `docs/superpowers/plans/` | 可执行实施计划，任务粒度，勾选 ≠ 已验证 | 中 |
| `docs/verification/` | 带日期和基线的评估与测试证据，不自动改架构 | 事实记录 |
| `docs/roadmap/` | 能力状态总览和对标矩阵，定期更新的现状快照 | 现状快照 |
| `docs/development/` | 开发者指南（本文、getting-started 等） | 操作指导 |

`rules/` 目录不在 `docs/` 下，它是强制规则，权威级别**高于所有 `docs/` 内容**。
`rules/` 与某份历史 Spec 若表述重叠：以 `rules/` 与最新已批准 Spec/ADR 为准。

## 5. 近期优先工作（会变，以矩阵为准）

以 [`capability-status.md` §4](../roadmap/capability-status.md) 为准。2026-07-22 架构巡检后：

- P0 先移出 E2E Seed 发布物并恢复 Layui 客户端聚合门禁（硬化计划 Task 3A～3B）
- P1 随后关闭跨模块实现依赖、API 迁移能力、Migrator 完整 HTTP 装配和空健康检查（硬化计划 Task 4A～4D）
- Identity **用户管理**为已批准的首个业务纵向切片（见[计划](../superpowers/plans/2026-07-21-identity-user-management-vertical-slice.md)）
- Vue / Layui **长期并行**（后台模块必须双端同步）
- Outbox 死信 / 多 Worker 验证（硬化计划 Task 6）与 PR 集成冒烟加宽仍为近期工程项

## 6. 常见误读

| 误读 | 纠正 |
|---|---|
| “有状态矩阵行 = 生产可用” | 只有 `Verified` 且证据齐全才接近发布表述 |
| “有测试文件 = Build-verified” | 必须有可定位的新鲜通过记录 |
| “Integration 在 PR 绿了 = 双库全矩阵绿了” | PR 默认只跑冒烟；全量在 `main`/发布档 |
| “Vue 做完 = 管理端完成” | Vue 为唯一持续交付后台；仍须验证真实交互、权限及接口，Layui 已冻结 |
| “specs 日期更新 = 规则已改” | 规则变更必须进 `rules/` 并接受审查 |

## 7. 下一步

本地验证命令与双库说明 → [`getting-started.md`](getting-started.md)  
外部分析吸收背景 → [`external-review-2026-07-21.md`](../verification/external-review-2026-07-21.md)
