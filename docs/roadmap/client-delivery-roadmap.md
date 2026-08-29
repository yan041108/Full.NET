# Full.NET 客户端交付路线图

> **2026-08-02 决策：** Vue 3 管理端是后台产品唯一持续交付线；Layui 管理端进入存量冻结，不再新增功能，也不参与后续能力的 `Verified` 门槛。此前“Vue/Layui 长期并行”决策由本路线图和 [`rules/client-frontend.md`](../../rules/client-frontend.md) 替代，历史验证记录继续保留为当时证据。

## 1. 交付目标

客户端按真实产品形态分工，不再为同一后台业务维护两套实现：

| 轨道 | 目录 | 定位 | 优先级 |
| --- | --- | --- | --- |
| Vue 管理端 | `ui/admin` | 唯一后台产品交付线；Vue 3 + TypeScript + Vite + Element Plus + Art Design Pro 基线 | P0 |
| Layui 存量端 | `ui/admin-layui` | 冻结的历史实现；只接受明确授权的安全、许可、迁移或退役任务 | Frozen |
| 共享 Web 契约 | `packages/client-contracts`、`packages/admin-i18n`、`packages/design-tokens` | OpenAPI/ProblemDetails/权限/租户/运行时校验和设计令牌 | P0 |
| uni-app | `clients/uniapp` | H5、微信小程序、支付宝小程序业务客户端 | P1 |
| Flutter | `clients/flutter` | Android/iOS 原生与 Windows/macOS/Linux 桌面端 | P2 |
| .NET MAUI | 尚未创建 | 仅在 C#/Windows 企业需求命中独立决策门禁后建立模板 | Decision Gate |

Vue 管理端必须优先形成完整、可访问、可真实验收的业务闭环。所有客户端继续通过 OpenAPI、标准状态码、ProblemDetails、稳定权限码和租户语义与服务端解耦，不共享具体 UI 组件。

## 2. 当前实现快照

| 能力 | 状态 | 当前证据 | 下一缺口 |
| --- | --- | --- | --- |
| C0 公共契约 | Build-verified | 会话、ProblemDetails、租户、导航、主要模块 API 客户端与运行时校验已存在 | 授权树和逐操作权限契约 |
| C1 Vue 壳层 | Build-verified | Art Design Pro 壳层、路由、主题、导航、会话、国际化与现有模块页面 | 富文本、人工辅助技术验收、进一步视觉收敛 |
| C1-Legacy Layui | Frozen | 历史壳层、页面、测试和真实栈证据保留 | 不再补齐功能；另行制定退役计划 |
| C2 后台业务 | Implementing | Identity、Tenancy、Organization、Settings、Auditing、Files、Notifications、CodeGeneration 等已有 Vue 切片 | 逐页面/逐操作授权；未完成模块继续只做 Vue |
| C3 uni-app | Build-verified foundation | 三目标工程与基础契约已建立 | 按[批准计划](../superpowers/plans/2026-08-30-workflow-designer-form-runtime.md)交付首个 Workflow 表单纵向样例；当前仍未实现 |
| C4 Flutter | Designing | 框架、组件与设计令牌边界已确定 | 工程基线和首个真实业务样例 |

测试发现数量只维护在 [`eng/testing/test-matrix.json`](../../eng/testing/test-matrix.json)，本路线图不复制门槛数字。

## 3. 完成状态

| 状态 | 判定 |
| --- | --- |
| `Mapped` | 已有功能归属、依赖与风险映射 |
| `Designing` | API、页面和验收场景正在设计 |
| `Implementing` | 服务端、共享契约或 Vue 已开始实现，但纵向切片未关闭 |
| `Implemented` | 服务端和 Vue 目标功能均已实现，完整验收尚未结束 |
| `Build-verified` | 聚焦 Unit/Architecture/契约/构建和受影响集已通过，但发布、人工或生产等价证据仍缺失 |
| `Verified` | 服务端双库、精确权限、租户、Vue 错误处理/可访问性/关键流程和真实栈 E2E 全部通过 |
| `Frozen` | 存量成果保留但停止功能扩张，不承诺与活动产品线对等 |

Layui 的缺失、失败或不兼容不再阻止新功能进入 `Implemented`、`Build-verified` 或 `Verified`。但任何任务也不得把未验证的 Vue 能力借 Layui 历史证据误报为已完成。

## 4. Vue 后台交付门禁

每个后台纵向切片按以下顺序关闭：

1. 冻结 OpenAPI、ProblemDetails、权限码、租户和并发语义；
2. 为页面和每个服务端业务操作定义稳定权限码；
3. Endpoint 显式绑定精确权限，未知或缺失授权失败关闭；
4. 更新 `packages/client-contracts` 的类型与不可信 JSON 校验；
5. Vue 使用本地可信路由/组件白名单和响应式权限门，无权限业务按钮不进入 DOM；
6. Unit 覆盖页面、单操作授权、撤销和错误；
7. SQL Server/MySQL Integration 覆盖权限、租户和直接 API 403；
8. Vue Mock/真实栈 E2E 覆盖关键流程、权限隐藏、绕过拒绝、可访问性和错误恢复；
9. 受影响集、Vue 生产构建、客户端审计和许可证门禁通过后更新状态。

详细权限模型见 [Vue 页面/操作授权设计](../superpowers/specs/2026-08-02-vue-action-authorization-design.md)、[Identity Users 样板计划](../superpowers/plans/2026-08-02-vue-action-authorization.md)和[三级授权补齐与 W4–W5 计划](../superpowers/plans/2026-08-03-vue-action-authorization-w4-w5.md)。

## 5. 阶段计划

### C0：公共客户端契约底座

持续维护：

- 标准 HTTP、ProblemDetails、CSRF、会话刷新与跨 Tab 协调；
- 可信租户上下文、动态导航结构校验与本地组件白名单；
- 页面/操作授权树、稳定权限码和权限撤销版本；
- OpenAPI 漂移、Source Generation JSON 与 TypeScript 运行时校验；
- 统一设计令牌、多语言机器码边界和许可证审计。

**退出条件：** Vue 不自行猜测服务端协议；未知导航、权限和 JSON 结构失败关闭；所有公共契约由测试冻结。

### C1：Vue 管理壳层

技术基线：Vue 3、TypeScript、Vite、Element Plus、Pinia、Vue Router、Art Design Pro；ECharts 按需加载；Tiptap Core 按明确计划接入。

必须具备：登录/退出/刷新、租户切换、动态导航、全局错误、403/404/500、主题/布局、键盘与窄屏可用、受控通知与实时连接、精确页面/按钮权限。

**退出条件：** Vue 在真实 API 上完成核心会话与管理路径；模板 Mock、任意动态组件和未经审计资产不进入发布物。

### C2：后台核心模块

| 波次 | 后台能力 | 当前状态 | 下一动作 |
| --- | --- | --- | --- |
| C2.1 | 用户、角色、菜单、超级管理员、在线会话、API Key | **Build-verified** | Identity W0–W1 精确动作权限与双库迁移已完成；模块分组 UI 仍待 Task 0 后续切片 |
| C2.2 | 租户、套餐、机构、职位、职级、用户隶属 | **Build-verified** | W2 粗粒度写权限已拆分；持续补 Vue 单操作真实栈 E2E |
| C2.3 | 配置、字典、枚举、Grid 偏好、审计 | Build-verified slices | 完成 Vue 体验与精确操作权限，不再建设 Layui |
| C2.4 | Files、Notifications、Jobs、CodeGeneration | **Verified**（Vue） | W4 迁移 071–076 已收口；见[W4–W5 验证](../verification/vue-action-authorization-w4-w5-closeout-2026-08-03.md) |
| C2.5 | Document 及后续 Admin.NET 吸收模块 | **Verified**（Document Host 切片） | W5 迁移 077–080 已收口；租户文档等后续切片独立规划 |

**退出条件：** Full.NET 1.0 范围内后台能力的服务端、双库、Vue 和真实栈验收完成；权限授权页面可以独立选择每个页面和业务操作。

### C3：Layui 冻结与退役治理

- 禁止新增页面、业务按钮、API Adapter、共享契约消费、生成模板和功能对等 E2E；
- 保留历史源代码和验证记录，防止无授权破坏性删除；
- Critical/High 安全修复、许可证处置和迁移辅助必须单独授权并保持最小范围；
- 另行评估归档、只读发布、移出默认工作区或最终删除的时点和迁移说明。

**退出条件：** 新功能 diff 不包含 `ui/admin-layui/**`；CI 和文档不再把 Layui 当成产品完成门槛。（2026-08-08：默认 `test:clients`、`build:clients`、E2E 与包体预算已退役 Layui 活动门禁；冻结扫描与 `test:e2e:layui-frozen` 保留为显式例外。）

### C4：uni-app H5/微信/支付宝

继续使用 uni-app Vue 3 + uni-ui，分别验证 H5、微信小程序和支付宝小程序构建。优先选择能复用现有 OpenAPI/权限/租户契约的真实业务样例，不复制后台管理功能。

首个批准业务样例是 Workflow 表单发起/办理：后台 VForm3 Draft 由服务端编译为 `WorkflowFormSchema`，uni-app 自研静态 `FullNetFormRenderer`，不引入 VForm3/Element Plus，也不直接解释原始 VForm JSON。页面进入 `subPackages`，同一 FormVersion 在 H5/微信/支付宝保持字段语义一致；包体、30/100 字段渲染和三目标构建按[设计器/跨端计划](../superpowers/plans/2026-08-30-workflow-designer-form-runtime.md)验证。

### C5：Flutter 原生移动与桌面

采用 Flutter Material 3 + Cupertino + Full.NET 设计令牌，为手机、平板和桌面提供自适应业务客户端。Flutter 不重复承担 H5，功能按真实客户端需求选择，不追求后台全量对等。

### C6：Vue 代码生成与高级能力

- 同一 `FullNetCrudSchema` 生成后端、双库 SQL、精确动作权限、生成就绪的 OpenAPI 元数据、Vue 页面和基础测试；TypeScript API 最终从运行时 OpenAPI 的规范化快照生成，不再由数据库 Schema 模板独立猜测；
- CRUD 默认生成 `read/create/update/delete`，其他动作必须由 Schema 明确声明；
- 不再新增 Layui 页面、JavaScript API 或路由生成能力；
- Realtime、文件、导入导出、打印、表单设计器、大屏和 AI/Agent 工作台分别建立 Vue 适配切片。

OpenAPI 驱动客户端生成按 [`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md) 和[专项实施计划](../superpowers/plans/2026-08-21-openapi-driven-client-generation.md)执行。`document-statistics.ts` 已迁移（现 230 条 `generated`）；Document 模块与 `vue-client-coverage-v1.json` 所列 45 个 Vue 生产 API 模块均已登记 manifest，OpenAPI 客户端单模块迁移阶段收官。

### C7：.NET MAUI 决策门禁

只有真实 C#/Windows 企业需求、Flutter 不适配证据、维护团队与发布目标明确时才建立 MAUI 模板，并新增独立 ADR；否则保持未创建。

## 6. 后端里程碑映射

| 里程碑 | 客户端交付 |
| --- | --- |
| M0/M1 | 公共协议、会话、ProblemDetails、Vue 最小壳层 |
| M2 | Identity/Tenancy/Organization、RBAC、页面/操作授权、Vue 核心流程 |
| M3 | Settings/Auditing/Files/Notifications/Jobs/CodeGeneration Vue 页面；uni-app 基础客户端 |
| M4 | Vue 后台 1.0 全部验证、Layui 退役治理、Workflow 表单 uni-app 三目标纵向样例、客户端安全/许可/E2E 加固 |
| M5+ | 后续 Admin.NET 模块 Vue 对标、Flutter、AI/Agentic Web、按需 MAUI |

## 7. CI 与质量门禁

| 变化范围 | 必须执行 |
| --- | --- |
| 公共契约 | client-contracts 类型检查/测试、OpenAPI 漂移、消费者受影响测试 |
| Vue 后台模块 | Vue typecheck、Unit、生产构建、对应 Mock/真实栈 E2E、权限直接 API 403 |
| 权限/数据库行为 | Unit、Architecture、SQL Server/MySQL Integration、迁移恢复、Vue 权限 E2E |
| Layui 冻结端 | 新功能必须零 diff；明确例外任务运行其聚焦测试并记录授权 |
| uni-app | 三目标构建；H5 真实浏览器 E2E |
| Flutter | format、analyze、test 和目标平台构建 |
| 第三方依赖 | 许可证、漏洞、资产来源和发布物审计 |

本地只执行任务快照命中的影响集，完整集合保留给 `main` CI 互斥分片。测试数量只维护在测试矩阵。

## 8. 主要风险与控制

| 风险 | 控制 |
| --- | --- |
| 粗粒度 `*.write` 造成越权 | 每个服务端业务动作独立权限；存量按资源迁移；Architecture 禁止新增多动作粗权限 |
| 只隐藏按钮、直接 API 可绕过 | Vue 不渲染 + Endpoint 同权限码 + 直接 API 403 Integration/E2E |
| 授权页与真实 Endpoint 漂移 | 代码拥有的 Authorization Catalog；目录—Endpoint—Vue 一致性门禁 |
| 动态导航成为任意组件加载 | 严格运行时校验 + 本地组件/路由白名单；未知标识拒绝 |
| Layui 继续消耗主线资源 | 冻结目录零新增；不参与 `Verified`；例外需所有者授权 |
| 公共契约演进破坏冻结客户端 | 记录为退役债务，不反向限制 Vue/服务端正确设计 |
| Admin.NET/layuiAdmin 许可污染 | 只参考功能和公开体验；禁止复制未获 MIT 再发布授权的源码/资产 |
| Vue 模板覆盖 Full.NET 安全层 | 保留自有认证、权限、租户、ProblemDetails 和 OpenAPI 边界，禁止模板 Mock 替换 |

## 9. 下一批可执行计划

按以下顺序推进：

1. 执行[Vue 页面/操作精确授权计划](../superpowers/plans/2026-08-02-vue-action-authorization.md)，以 Identity Users 关闭首个完整样板；
2. 建立全后台页面/操作权限清单，并按 Identity → Tenancy/Organization → Settings/Auditing → Files/Notifications/Jobs/CodeGeneration → Document 分波次清零粗权限；
3. 后续 Admin.NET 吸收任务只创建 Vue 页面和 Vue E2E，修订模板/计划中的 Layui 新增项；
4. 调整客户端 CI/脚本，把 Layui 从新增功能聚合门禁移到冻结/退役检查；
5. 为 Layui 制定独立退役计划，决定归档、只读发布、迁移说明和最终移除条件；
6. 在 Vue 主线继续推进 Tiptap、ECharts 业务图表、可访问性和统一视觉体验；
7. Workflow 核心首切片达到 Build-verified 后，执行[设计器与跨端表单计划](../superpowers/plans/2026-08-30-workflow-designer-form-runtime.md)，作为 uni-app 首个真实业务纵向样例；
8. 完成该样例的三目标与包体门禁后，再启动 Flutter 首个纵向样例；
9. [OpenAPI 驱动客户端生成计划](../superpowers/plans/2026-08-21-openapi-driven-client-generation.md) 单模块迁移已收官：`document-statistics.ts` 已完成（现 230 条 `generated`）；`vue-client-coverage-v1.json` 所列 45 个 Vue 生产 API 模块均已登记 manifest。

每个计划结束时只更新真实受影响的状态与验证记录，不以计划存在、历史 Layui 证据或局部构建替代 Vue 真实验收。
