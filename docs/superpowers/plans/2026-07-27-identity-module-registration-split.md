# Identity 模块注册职责拆分实施计划

**目标：** 将 `IdentityModule.AddServices` 中认证、授权、领域服务和 HTTP 策略注册拆入四个内部职责边界，同时保持公共 API、服务生命周期、Scheme、策略、Handler、Seed 与 JSON 行为不变。

**架构：** `IdentityModule` 继续作为唯一公开模块入口，先调用既有 `AddMigrationServices`，再按固定顺序调用四个 `internal` 扩展。Unit Test 通过规范化 `ServiceDescriptor` 快照锁定组合等价性，并验证重复注册后的关键运行时契约。

**范围：** 不修改 Endpoint、SQL、迁移、Dapper、生产客户端或 Integration 测试方法；本切片只新增 2 项 Unit Test。真实栈复验发现的旧 Vue 上下文选择器只在既有 Playwright 辅助函数内修正，不增加客户端测试数。任务选定时的 `395/7/49/189` 基线已由协调中的 Jobs 故障隔离切片增加 1 项 Unit，本切片最终将 canonical 更新为 `398/7/49/189`。

## Task 1：建立组合根行为护栏

- [x] 新增 `IdentityModuleRegistrationTests`：
  - 模块入口与显式职责管线的注册描述符完全等价。
  - 重复注册后认证、授权、命令、播种、限流、CORS 与 JSON 契约仍可解析且保持单一。
- [x] 确认 RED：四个 `AddIdentity*` 内部扩展尚不存在，测试项目编译失败。
- [x] 测试容器使用 `Testing` 环境及既有临时签名配置，完整提供生产 Host 会注入的 `IHostEnvironment`，不增加测试专用生产入口。

## Task 2：拆分四个内部注册职责

- [x] `IdentityAuthenticationServiceCollectionExtensions` 接管 Session/JWT、Data Protection、TOTP、API Key、RSA/Token 与认证 Scheme；使用内部标记保证重复注册不会重复添加命名 Scheme。
- [x] `IdentityAuthorizationServiceCollectionExtensions` 接管授权目录、权限快照、Session Context、Navigation、DataScope、Policy Provider 与 Result Handler。
- [x] `IdentityDomainServiceCollectionExtensions` 接管本地化、错误资源、Host 管理服务、目录映射、验证器、Command Handler 与 Cookie Writer。
- [x] `IdentityHttpPolicyServiceCollectionExtensions` 接管来源校验、CORS、限流与 JSON Context；Identity 自有 `IConfigureOptions<JsonOptions>` 通过 `TryAddEnumerable` 注册并防止重复插入 Context。
- [x] `IdentityModule.AddServices` 缩减为迁移注册与上述四个固定顺序调用。
- [x] 新增测试 **2/2**、完整 Identity Unit **122/122**，失败与跳过均为 0。

## Task 3：状态、门槛与验证收口

- [x] 等待首轮 Jobs、OpenAPI route-key、session refresh 与 Jobs 故障隔离依次合入并清理后，只同步一次最新 `main@40976e5`。
- [x] 更新四处 Unit canonical 为实际发现数 `398`；确认测试门槛审计已精确记录“无 Web Locks 时 `localStorage` 跨 Tab 短租约”，且 storage SecurityError 降级后的 client-contracts 为 **76**、Vue 聚合为 **201**。
- [x] 更新架构硬化 Task 15、能力矩阵和独立验证记录；未借结构重构提升 Identity/RBAC 整体成熟度。
- [x] 执行 Release、Unit、Compatibility、Architecture、双库 Identity 聚焦 Integration、客户端真实栈登录冒烟及全部静态门禁。
- [x] 完成规则与 Skill 遗漏复盘、三轮结构化代码审查、`git diff --check` 和范围审计。
- [ ] 提交后合并到本地 `main`，删除 `codex/identity-module-registration-split` 分支及工作树，不推送远端。
