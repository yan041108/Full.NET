# Admin.NET.Pro 刷新后增量对标审计（2026-08-30）

## 1. 范围与结论

- 参考仓库：`G:\wwwroot\github_fork\Admin.NET.Pro`
- 分支：`v2.1`
- 上次基线：`3c65392d8e9c543411b9469a400fe4deee86dc15`（2026-08-07）
- 当前基线：`09d38bd82603ca23b2e39644376906bd1023a42f`（2026-08-28）
- 本轮范围：`3c65392d..09d38bd8`，31 个提交、315 个变更文件
- 自最初基线累计：`3879b035..09d38bd8`，59 个提交、399 个变更文件

本轮没有证据支持把任何 Full.NET 能力升级为 `Verified`。新增明确缺口是：

1. Identity 用户导入导出仍是 JSON 文本/JSON 文件体验，缺少受控 Excel 模板下载、工作表配置、文件上传解析和逐行错误回执。
2. MCP Server/Tool 暴露仍为 M5+ `Mapped`；必须先建立静态工具目录、逐工具权限、租户边界、审计、限流和 Native AOT 契约，禁止复制本机 HTTP 回环与请求身份透传方案。
3. Observability Admin 日志控制面缺口被进一步确认；实现必须限制根目录和文件名、限制尾读窗口/响应大小、支持取消与活动文件共享读取，禁止任意路径读取。

Workflow 新增的业务完成通知和“设计已修改未发布”状态已经被 Full.NET 的待审 Spec 以不可变版本、内容哈希、Outbox/Inbox 和业务结果事件覆盖，不改变 `Mapped / Spec pending review` 状态。

## 2. 逐提交处置

| 提交 | 日期 | Admin.NET.Pro 变化 | Full.NET 处置 |
| --- | --- | --- | --- |
| `38275a9e` | 2026-08-08 | 日志文件目录、流式读取、尾读和下载 UI | `Gap confirmed`：归入 Observability Admin；只吸收受控日志查看目标，不复制路径拼接实现。 |
| `9bfd3f9f` | 2026-08-08 | 租户名称、管理员姓名、手机号、邮箱前端校验 | `No status change`：Full.NET 租户开通模型不含管理员联系字段；现有 Identifier/Name/Domain 有服务端校验。若扩展联系人资料，必须由服务端权威校验。 |
| `730e1322` | 2026-08-08 | 优化日志尾读速度 | `Gap confirmed`：与 `38275a9e` 合并处置；性能实现需有界缓冲，避免整文件与反复字符串前插。 |
| `55228152` | 2026-08-09 | Furion 升级 | `No status change`：Full.NET 不使用 Furion。 |
| `2e53c2ad` | 2026-08-10 | Furion 升级 | `No status change`。 |
| `5202fbb4` | 2026-08-10 | Web_Artd 请假示例与 Furion 升级 | `Deferred/Compatibility`：Layui/第二管理端已冻结；示例不构成功能交付证据。 |
| `903ca712` | 2026-08-10 | Workflow 设计器网格线 | `Mapped`：属于未来 Workflow Vue 设计器体验，不提前创建实现。 |
| `89f5cd1d` | 2026-08-11 | 导入模板支持自定义工作表 | `Gap`：Full.NET Identity 当前仅 JSON 导入导出；记录为 ImportExport 用户文件体验切片。 |
| `f0c174b2` | 2026-08-11 | 合并导入模板 PR | `No status change`：合并提交，处置同 `89f5cd1d`。 |
| `0cdd40eb` | 2026-08-11 | Furion 与依赖升级 | `No status change`。 |
| `314e95c0` | 2026-08-11 | 分支合并 | `No status change`。 |
| `0971bb01` | 2026-08-12 | Furion 日志性能升级 | `No status change`：Full.NET 使用自有有界异步日志管道。 |
| `90e24352` | 2026-08-16 | Furion 升级并移除 Newtonsoft.Json | `Covered by architecture`：Full.NET 已统一 System.Text.Json，不改变状态。 |
| `38aee1a7` | 2026-08-17 | JSON 科学计数法转 long 修复 | `No status change`：属于 Furion 转换器兼容修复；Full.NET 保持强类型 System.Text.Json 契约。 |
| `9a4fa42d` | 2026-08-19 | Furion 与依赖升级 | `No status change`。 |
| `7398bb07` | 2026-08-20 | 报表 SQL 失败即停、命令超时 | `Mapped`：Reporting 尚未实施；未来 Spec 必须定义只读 SQL、数据源授权、超时、行数/大小上限和取消。 |
| `57ba4868` | 2026-08-21 | 增加 MCP 服务 | `New mapped capability`：进入 Agents/MCP M5+，不得据此创建无权限的通用 API 工具暴露。 |
| `bc614073` | 2026-08-21 | 键值 JSON 转动态单一对象 | `Rejected implementation`：开放 `object`/动态 DTO 不符合 Host.Api Native AOT 静态闭包；第三方适配按具体、可源生成契约实现。 |
| `4e3fe1bf` | 2026-08-23 | Vite/VXE 配置与依赖维护 | `No status change`。 |
| `1dea1438` | 2026-08-23 | 去除 Newtonsoft.Json、掩码处理调整 | `Covered by architecture`：Full.NET 已使用 System.Text.Json；脱敏继续走自有审计边界。 |
| `f2acca7a` | 2026-08-23 | MCP 工具改用本机 HTTP 回环并转发 Header | `Rejected implementation`：扩大身份转发、SSRF/代理信任、限流豁免和双重中间件语义风险；Full.NET 应直接调用静态 Tool Handler。 |
| `3b8a6fdc` | 2026-08-24 | MCP 回环 URL、Header、参数绑定配置化 | `Rejected implementation`：配置化未消除回环与身份代理风险；未来必须单独安全/AOT Spec。 |
| `acb381f3` | 2026-08-25 | 枚举字符串 JSON、默认关闭 UTC | `No status change`：Full.NET 保持显式 BCP 47/UTC/序列化契约，不随参考实现改变时间语义。 |
| `0a056254` | 2026-08-25 | Furion JSON 转换器升级 | `No status change`。 |
| `ddcd4843` | 2026-08-26 | 枚举 JSON 与依赖升级 | `No status change`。 |
| `d4d48fcf` | 2026-08-27 | 枚举序列化修复 | `No status change`。 |
| `149adf08` | 2026-08-27 | Workflow 审批完成/驳回/取消通知业务模块 | `Covered by pending Spec`：Full.NET 已规定版本化结果事件、事务 Outbox 与消费方 Inbox；禁止同步任意 HTTP 回调替代可靠交付。 |
| `25e9a2c9` | 2026-08-27 | Workflow “设计已修改未发布”及已发布版本只读查看 | `Covered by pending Spec`：草稿、不可变版本、内容哈希与版本读取已设计；实施时补显式差异状态。 |
| `76edbebf` | 2026-08-27 | Workflow 菜单组件名修正 | `No status change`。 |
| `e2f755d6` | 2026-08-27 | 上级部门审批节点空范围校验修复 | `Mapped`：未来审批人解析规则必须作为版本化节点语义并覆盖双库/组织目录测试。 |
| `09d38bd8` | 2026-08-28 | Furion JSON 序列化升级 | `No status change`。 |

## 3. Full.NET 证据对照

| 能力 | 当前证据 | 判断 |
| --- | --- | --- |
| Tenancy 权威校验 | `ProvisionTenantCommandValidator` 对 Identifier、Name、Domain 执行服务端校验；Host 更新契约只允许 Name/Version | Admin.NET 新增的是不同字段集的客户端 UX，不降低 Full.NET Tenancy 状态。 |
| Identity 导入导出 | `UsersView.vue` 导出 `host-users.json`，导入通过文本框解析 JSON；`IdentityUserManagementContracts`/Endpoint 提供批量行 API | 核心批量能力已存在，但 Excel 文件体验未交付。 |
| Workflow 业务联动 | `2026-08-20-workflow-module-design.md` §1、§4、§5 明确不可变版本、内容哈希、Outbox/Inbox 与完成/拒绝结果事件 | 新增参考能力已被设计覆盖，仍需 Spec 审查后才能编码。 |
| MCP | 路线图已把 Agent/MCP/Agentic Web 放在 M5+；生产源码无 MCP Server/Tool 注册 | 状态保持 `Mapped`，新增安全/AOT 设计门禁。 |
| 日志文件管理 | Hosting 已有结构化日志、异步管道和健康检查；无管理端日志文件目录/尾读/下载 Endpoint | Observability Admin 缺口继续成立。 |
| Reporting | 对标矩阵为 `Mapped`，无生产 Reporting 模块 | 报表 SQL 超时与失败语义进入未来 Spec，不改变状态。 |

## 4. 推荐后续顺序

1. **Identity ImportExport 小切片**：先做文件格式与安全规格，再实现模板下载、受限 `.xlsx` 上传、大小/行数限制、公式注入防护、逐行验证和错误回执；服务端批量 API继续作为唯一权威写入口。
2. **Observability Admin 日志控制面**：按实例、固定日志根和稳定文件 ID 列举；只允许有界尾读/下载，加入精确权限、审计、取消、活动文件共享读取和路径越界测试。
3. **Workflow Spec 复审**：把显式 `DraftChangedSincePublish`、发布版本只读 API、完成/拒绝/取消事件和上级组织审批人解析验收项写入首切片计划；不照搬同步回调。
4. **MCP 延后**：仅在 AI/Agents 获得独立授权后建立 Spec；工具静态登记、参数/结果源生成、逐工具权限、人审高影响操作和完整审计是前置条件。
5. **Reporting 延后**：与 ImportExport 分模块立项，优先解决数据源授权、只读语句、超时/取消、分页与导出背压。

## 5. 未验证边界

- 本轮是源码静态审计，未构建或运行 Admin.NET.Pro，不证明其新增实现运行正确或安全。
- 未运行 Full.NET 运行时测试；本轮不修改生产代码、公共契约、数据库或客户端行为。
- 所有参考实现只作为能力事实源；许可证、安全、Native AOT、双数据库与模块边界仍以 Full.NET 规则为准。
