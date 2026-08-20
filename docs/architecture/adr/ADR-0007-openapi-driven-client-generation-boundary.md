# ADR-0007：OpenAPI 驱动客户端生成边界

- 状态：已批准
- 决策日期：2026-08-21
- 批准者：项目所有者在当前任务中确认按推荐判断制定标准并更新开发计划
- 适用范围：标准 HTTP API、`packages/client-contracts`、Vue 主管理端、`Full.NET.Data.CodeGeneration` 的 TypeScript 客户端产物，以及后续真实外部 SDK
- 替代关系：细化总体架构 Spec 中“客户端通过 OpenAPI 解耦”和“只有真实证据才引入完整生成式 SDK”的旧表述；不替代现有 OpenAPI 兼容门禁、ProblemDetails、权限、租户和前端安全规则

## 1. 上下文

Full.NET 当前 Vue 调用链已经具备一套安全且可验证的自有边界：

```text
Vue 页面
→ ui/admin/src/api/*.ts 业务 API 函数
→ @fullnet/client-contracts DTO 与运行时守卫
→ createHttpClient
→ Full.NET API
```

`createHttpClient` 统一处理 Access Token、并发 Refresh、Cookie 凭据、`Accept-Language`、ProblemDetails、401 单次重试、Blob、`204 No Content` 和取消。生产 API 模块把 JSON 作为 `unknown` 接收，再通过共享契约运行时守卫验证。OpenAPI 轻量冻结夹具、破坏性变更检查以及 Vue 调用点—共享契约—路由覆盖清单已进入 CI。

与此同时，HTTP 契约仍在多个位置重复表达：C# Request/Response、运行时 `/openapi/v1.json`、`contracts/openapi/*-v1.json`、手写 TypeScript DTO/守卫、手写 Vue API 路径，以及数据库 Schema 驱动的 CRUD TypeScript 模板。重复维护会让可空性、分页、文件、`204`、操作名称和字段演进发生漂移。

直接采用 OpenAPI Generator 默认 `typescript-fetch` Class SDK 也不能自动解决问题：默认 Runtime、Configuration 和错误模型会与 Full.NET 自有认证/刷新/语言/ProblemDetails 边界重叠；第三方生成命名一旦泄漏到页面，生成器升级会扩大迁移面；仅生成 TypeScript 类型不能替代不可信 JSON 的运行时校验。

## 2. 决策驱动因素

1. 客户端线协议必须只有一个最终生成权威。
2. 不能退化现有认证、租户、语言、ProblemDetails、Blob、`204` 与取消语义。
3. JSON 运行时校验必须失败关闭，不能用静态类型断言代替。
4. 页面和业务 Composable 需要稳定、可读、可测试的业务函数，而不是第三方模板 API。
5. 生成必须确定、可离线复现、可审计，并遵守生成物所有权和安全删除规则。
6. 现有 45 个生产 Vue API 模块不能在没有试点证据时一次性重写。
7. CRUD 生成器不得继续维护独立于真实 Endpoint 的第二套 HTTP 契约。

## 3. 候选方案

### 方案 A：继续全部手写

保留当前 DTO、守卫和 API 模块，只加强覆盖清单。

- 优点：无新工具和迁移成本，运行时边界完全可控。
- 缺点：仍需重复维护路径、参数、DTO 和守卫；覆盖清单只能证明映射存在，不能从标准 Schema 自动生成一致实现。

### 方案 B：直接采用 OpenAPI Generator 默认完整 SDK

从 `/openapi/v1.json` 生成 `typescript-fetch` Class、模型和 Runtime，页面直接调用生成 Class。

- 优点：落地快，路径和静态类型自动生成，生态成熟。
- 缺点：复制 Full.NET HTTP Runtime；生成器命名和模板类型泄漏到页面；ProblemDetails、Refresh、Cookie、语言、Blob 与运行时严格校验需要大量定制；升级影响面过大。

### 方案 C：OpenAPI 驱动的低层生成＋Full.NET 稳定适配层

运行时标准 OpenAPI 经规范化后形成仓库快照；生成模型、运行时守卫、参数编码和低层 Operation；`createHttpClient` 继续承担传输横切语义；`ui/admin/src/api` 保持薄业务适配层。

- 优点：消除主要契约重复，同时保护现有安全和错误边界；生成工具可替换；页面不受模板升级影响；适合渐进迁移。
- 缺点：需要稳定 `operationId`/Tag、标准快照、模板/生成器和迁移清单；试点阶段会暂时共存手写与生成契约。

## 4. 决策

采用方案 C。

### 4.1 权威链路

```text
C# Endpoint + Request/Response + 显式 OpenAPI 元数据
→ 运行时 /openapi/v1.json
→ 规范化、经验证的仓库内标准 OpenAPI 快照
→ 低层 TypeScript 模型 + runtime guards + operations
→ ui/admin/src/api 薄业务适配层
→ Vue 页面/Store/Composable
```

Endpoint 运行时 OpenAPI 是客户端线协议的源头，仓库快照是确定性生成输入。现有 `contracts/openapi/*-v1.json` 是轻量兼容夹具，不是标准 OpenAPI，也不是生成输入；两者在迁移完成前并行存在并互相校验关键身份。

### 4.2 生成边界

生成层允许产生：

- 请求、响应、分页和枚举模型；
- 不可信 JSON 的运行时守卫；
- Path、Query、Header、Body、multipart 参数编码；
- JSON、Blob、Void/`204` Operation；
- 确定性 barrel exports 和生成清单。

生成层禁止承担：

- Access Token 存储和注入策略；
- 并发 Refresh 与 401 重试；
- Cookie、CSRF、租户切换和语言协商；
- ProblemDetails 归一化；
- Vue 状态、缓存、权限门、路由、页面和交互；
- 业务重命名、流程编排或自动重试有副作用操作。

这些责任继续分别归 `createHttpClient`、会话/权限基础设施、`ui/admin/src/api` 和 Vue 业务层。

### 4.3 稳定 Operation 身份

参与生成的 Operation 必须显式声明：

- 全局唯一 lowerCamelCase `operationId`：`{module}{Verb}{Resource}[Qualifier]`；
- 恰好一个 PascalCase 主 Tag：`{Module}{Resource}`；
- 完整请求、成功响应、ProblemDetails、鉴权和非 JSON响应元数据。

例如：

```text
identityListHostUsers
identityCreateHostUser
identityResetHostUserPassword
filesDownloadHostFileContent
```

`operationId`、Tag、路径、JSON 字段和 Schema 名一旦进入已发布生成快照，按公共契约治理，不得因模板审美静默重命名。

### 4.4 运行时验证

JSON Operation 的底层传输结果类型必须是 `unknown`。只有生成运行时守卫成功后才能返回业务 DTO；守卫失败返回稳定客户端错误码并保留服务端 Trace/ProblemDetails 边界，禁止把畸形成功响应改写成伪造的 HTTP 500 ProblemDetails。

Blob、multipart、SSE、文件流和 `204` 不进入 JSON 守卫：

- Blob 使用 `requestBlob`；
- `204` 使用 `request<void>` 并验证响应无 JSON 解析；
- multipart 由 Operation 负责字段编码，但认证、Cookie 和错误仍经过共享 HTTP Runtime。

### 4.5 工具边界

“OpenAPI 驱动生成”是永久架构决策；具体生成器是可替换工具选择。首个试点以固定版本 OpenAPI Generator `7.24.0`、npm 包 `@openapitools/openapi-generator-cli@2.40.1` 的 `typescript-fetch` 模型作为候选基线，评估默认模板加最小自有模板能否满足本 ADR。

只有同时满足以下条件才保留该第三方工具：

1. 不把其 Runtime、Configuration 或 Class 暴露给 Vue 页面；
2. JSON 返回保持 `unknown → guard → DTO`；
3. 能注入 `HttpClient` 并保留 Blob、`204`、ProblemDetails 与 Refresh 行为；
4. 同输入连续生成两次零差异；
5. 生成输出可通过项目 TypeScript、许可、漏洞和 Golden File 门禁；
6. 自定义模板规模有界，只覆盖 Operation/guard/exports，不 fork 整个生成器。

任一条件失败时，停止第三方工具扩张，保留标准 OpenAPI、命名、快照和适配层决策，改用最小仓库自有生成器消费同一快照。不得为了保留工具而放宽 Full.NET Runtime 或安全规则。

### 4.6 生成所有权

- 生成文件只使用 `.generated.ts` 后缀并由稳定清单记录路径和摘要；
- 人工代码只引用生成公开入口，不修改生成文件；
- 生成前后都验证输入 Schema、版本、模板摘要和输出目录；
- 陈旧产物删除遵守 R-20260730 的 claim、复验、墓碑和 recovery 边界；
- CI 从仓库快照生成，不直接访问开发、测试或生产 Swagger URL；
- 生成器、配置、模板和快照全部进入版本控制，不依赖全局安装或 `latest`。

## 5. 迁移与停止门禁

第一阶段只选择三类试点：

1. Identity Host Users：普通 JSON、分页、Path/Query/Body、创建和状态动作；
2. Files Host Files：multipart 上传与 Blob 下载；
3. Settings Host Config Entries：`204 No Content`、批量动作与普通 JSON。

三个试点必须同时通过以下门禁后，才允许迁移第四个模块：

- 运行时 OpenAPI 与规范化快照一致；
- `operationId`、Tag、Schema 和状态码完整且稳定；
- 生成两次零差异；
- 所有 JSON 畸形响应被守卫拒绝；
- Refresh、Cookie、语言、ProblemDetails、Blob、multipart、`204` 和取消行为与迁移前一致；
- Vue 页面只依赖原业务适配函数，页面调用点无批量改名；
- 生成依赖通过许可、漏洞和包体影响审查；
- OpenAPI、client-contracts、Vue Unit/Build 与对应真实 API 聚焦测试通过。

如果任一语义无法保持，试点保持 `Implemented/Experimental`，禁止批量迁移；修正标准或工具后重新执行完整门禁。

## 6. 后果

### 正面后果

- HTTP 客户端契约收敛到真实 Endpoint OpenAPI；
- 稳定 Operation 身份支持 Vue、uni-app、Flutter 和未来外部 SDK；
- 页面与第三方生成器解耦；
- 运行时 JSON 校验和 Full.NET HTTP 安全语义不退化；
- CRUD 生成器不再维护独立 HTTP 真相。

### 成本与风险

- Endpoint 需要补齐稳定 `operationId`、Tag 和完整 Schema；
- 迁移期存在轻量夹具、标准快照、手写守卫和生成守卫的受控共存；
- 第三方工具可能需要 Java/Docker 和模板维护，必须通过试点门禁后才能固化；
- 生成输出规模、依赖和 CI 时间需要单独观测，不能凭“自动生成”假设成本更低。

## 7. 验证与复审

实施按 [`2026-08-21-openapi-driven-client-generation.md`](../../superpowers/plans/2026-08-21-openapi-driven-client-generation.md) 执行。完成三个试点后必须形成基于当时提交的 Verification，记录工具版本、模板规模、生成时间、产物数量/体积、零漂移、行为测试和未验证项；只有该证据可以解除批量迁移停止门禁。

以下事件触发 ADR 复审：

- 生成器要求替换 `createHttpClient` 或暴露第三方 Runtime 给页面；
- OpenAPI 3.1/组合 Schema 无法稳定生成守卫；
- 新客户端需要与 Vue 不同的线协议解释；
- 生成时间、产物体积或升级漂移超过项目可接受门槛；
- 首个真实外部 SDK 需要公开版本与发布策略。
