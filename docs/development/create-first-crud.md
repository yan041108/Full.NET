# 创建首个 CRUD 与环境诊断

本教程记录从 Full.NET 应用模板创建新项目后的只读诊断入口，以及 CRUD 生成与接入步骤。独立应用的完整生成 CRUD、OpenAPI、Vue、跨租户拒绝与再生成保护仍按总计划 F02 验收，不能把模板字典 CRUD 冒烟作为生成业务验收。

## 前置条件

- 已安装 .NET 10 SDK（`dotnet --version` 可执行）
- 独立应用根目录包含 `fullnet-app.json`、`framework-manifest.json`、`src/<name>.Host.Api`、`src/<name>.Host.Migrator` 与 `framework/fullnet/`；`src/Composition`、`src/Hosts`、`src/Modules` 是原框架仓库的布局
- `appsettings.json` 已配置 `FullNet:Modules:Preset`（如 `minimal` 或 `platform`）

## 第一步：运行 diagnose

在应用根目录执行：

```bash
pnpm run diagnose:development

# 等价入口；以下命令均从独立应用根目录执行
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- diagnose --workspace . --profile development
```

成功输出包含机器可读行，例如：

```
DIAG_SDK_OK ok 检测到 .NET SDK 10.x.x。
DIAG_WORKSPACE_OK ok 工作区结构符合 Full.NET 应用布局。
DIAG_MODULES_OK ok 已配置 FullNet:Modules 模块预设或启用列表。
```

开发环境若尚未配置数据库连接，可能出现 `DIAG_CONNECTION_PLACEHOLDER warn ... hint=...`；按 hint 使用 user-secrets 或环境变量注入，**诊断不会输出连接字符串原文**。

生产配置使用 `pnpm run diagnose:production` 或 `--profile production`；缺少连接或秘密占位符将报告 `error` 并以非零退出码结束。Profile 只接受 `development`、`production`，重复或未知参数拒绝执行。

诊断按目标工作区的 `global.json` 解析 SDK，检查宿主 `appsettings.json`、独立应用清单、所选模块引用及配置占位符。独立应用的根、API及已声明同名Migrator的基础JSON，其模块预设和数据库Provider必须都与冻结档案一致；相关文件缺失、无效或字段类型错误会返回脱敏错误，不能由API/根配置回退掩盖。无Migrator的旧应用仍可诊断，不会自动创建宿主。诊断不会执行初始化、迁移或数据库连接，也不证明配置中的地址可达；它不是完整ASP.NET Core配置加载器，不认证部署环境的全部覆盖来源。SDK缺失导致.NET CLI本身无法启动时，先安装.NET 10 SDK，再运行此入口。

## 第二步：准备 CRUD Schema

原框架仓库的示例主从单据见 [`samples/enterprise-request/schema.json`](../../samples/enterprise-request/schema.json)（`master.detail` 场景：申请头 + 明细行）。应用应准备自己的 `schema.json`，冻结项目 OwnerKey，并显式声明字段、精确权限与 `dataScope`；不能直接沿用原仓库的集成目标路径。

## 第三步：预览生成计划

```bash
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- \
  --schema schema.json \
  --workspace .
```

输出 `Create`/`Update`/`Unchanged` 行，默认不写盘。

## 第四步：应用生成

确认计划后追加 `--apply`：

```bash
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- \
  --schema schema.json \
  --workspace . \
  --apply
```

相同输入重复执行应报告 `Unchanged`；未登记的人工文件应保留，人工修改的受管产物应报告冲突并拒绝覆盖。生成产物落盘不等于模块已接入宿主或可运行。

独立应用的模板验收会用应用包内的 CLI 检查租户 CRUD 预览不写入产物、生成后相同输入为 `Unchanged`、人工文件保留，以及修改受管 SQL 后返回冲突且保持产物与清单字节。每阶段日志保存在 `.tmp/template-real-stack/application-crud/` 并由 Actions 上传；这项生成与保护检查不代替下方的模块接入、业务双库运行、权限或页面验收。

模板验收还会通过 `apply-module-integration` 将后端产物接入应用自有模块，经过候选编译后实际构建模块项目，并验证重复接入及人工 SQL 修改保护。日志位于 `.tmp/template-real-stack/application-crud-module/`。该阶段只验证模块编译和写盘保护，尚未将生成注册桥、授权贡献者和模块接入 API，也不执行业务迁移。

后续接线验收通过 CLI 将生成注册桥接入模块入口，将模块引用与实例加入应用自有 Composition，再构建 API 并重复接入检查 `Unchanged` 和源码字节。日志位于 `.tmp/template-real-stack/application-crud-host-wiring/`。为进入此阶段，冲突负例确认内容保留后会显式撤销验收自己追加的模块 SQL 测试注释；根目录的人工 SQL 与人工业务文件继续保留。API 编译仍不证明运行期 DI、授权、HTTP、业务双库或页面通过。

接线之后的运行探针复用当前应用启动装配代码，显式开启 DI 构建和作用域校验，检查两个生成服务的实例隔离、五条路由的精确权限元数据及 DTO 长整数 JSON 往返。日志位于 `.tmp/template-real-stack/application-crud-runtime/`。探针在应用自己的 `verification/CrudRuntimeProbe` 中构建，不修改 API 入口，不启动监听、后台服务或数据库；HTTP、迁移与双库业务仍需后续验收，探针成功也不代表生产配置或 Native 发布通过。

授权目录验收在运行探针之前创建应用拥有的无状态贡献者，以 Singleton 注册，使用不带 `clientRoute` 的显式授权目标执行完整 Host CLI 接入。生成四项 Tenant 权限、一项页面导航和三项操作后，追加验收自己的人工注释并重复接入，检查人工权限、所有相关产物与 Vue 路由字节。日志位于 `.tmp/template-real-stack/application-crud-authorization/`。运行探针随后通过实际 `IAuthorizationPolicyProvider` 物化权威目录，检查生成、人工与官方策略，以及未知权限拒绝；策略解析成功仍不代表真实请求已完成授权。

运行探针还通过实际 `IAuthorizationService` 执行四项生成策略：租户精确权限与租户超级管理员允许，缺权限、匿名、Host、Host超级管理员及缺失/非法作用域拒绝；四权限两两交叉，只允许同项权限。48次实际结果累计为12允许、36拒绝并进入报告，缺失执行结果的旧报告拒绝。这些主体由验收构造Claims，模拟认证后的授权输入，不替代JWT签名、会话、安全戳、HTTP中间件与跨租户数据隔离验证，亦未将构造主体用于应用请求。

## 第五步：模块接入（可选）

原仓库的 `samples/enterprise-request/integration-target.json` 是仓库布局示例，不适用于独立应用。准备应用自己的 `integration-target.json`，显式选择应用拥有的模块项目、入口与宿主接入位置；不得为了接入业务改写受管框架或恢复冻结 Layui 交付线。规划入口：

```bash
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- plan-module-integration \
  --schema schema.json \
  --repository . \
  --target integration-target.json
```

按 `Missing`/`Ready` 项完成接线后再执行 `apply-module-integration` 等子命令。

只交付 Vue 的目标 JSON 可以省略 `layuiRouterPath`，`clientRoute` 可以只提供 `routePath`、`vueRouteName`、`vueComponentPath`。如显式提供存量 Layui 控制器，`layuiControllerPath` 与 `layuiControllerExport` 必须成对；此兼容读取能力不授权恢复 Layui 开发。未知字段、非法路径或不完整配对仍拒绝。

`apply-client-route-integration` 要求模块聚合桥已由生成清单拥有、模块入口和 Composition 已完成接入、Vue 组件已存在；条件不满足时拒绝写盘。Vue-only 目标仅修改 Vue 路由，重复执行报告 `Unchanged`，不创建 Layui 文件。该结构接入检查不能代替宿主运行、精确权限或页面验收。

生成的授权片段是 `Permissions`、`Navigation`、`Actions` 的集合元素，应分别接入应用拥有的授权贡献者；不能把片段直接追加到 C# 文件末尾。自动接入要求三个标准集合及完整生成块，部分标记、人工改动或结构歧义拒绝修改。

完整编排入口 `apply-host-integration` 依次执行后端、模块入口、Composition、可选 Vue 与授权贡献者接入。目标 JSON 必须额外显式提供 `authorizationContributorPath`（应用拥有的现有 C# 文件相对路径）；Contributor 的接口实现、DI 注册与所需 using 由应用声明。该字段仅用于完整编排命令，其他逐阶段命令仍拒绝它，包括显式 `null`，避免忽略授权目标。

```bash
dotnet exec src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll apply-host-integration --schema <schema.json> --repository <应用根目录> --target <host-target.json>
```

上例 CLI 路径适用于仓库布局；独立模板应用使用其自带 CLI 路径。命令成功报告 `Applied HostIntegration`，输入无效返回 64，前置或受控冲突返回 2。共享编排分阶段提交，后续失败不代表前序步骤未写入，也不是全链事务；已有恢复/授权暂存材料先拒绝接入，需要人工审查。授权候选写入前复用隔离模块编译，只在临时投影中替换 Contributor；编译失败或取消时不提交授权文件，编译期间人工漂移仍由提交复核拒绝。应用 Migrator、实际权限注册/无权限及跨租户拒绝仍需 F02 完整运行验收；不得用命令退出 0 替代。

`TenantRequired` Schema 的生成权限使用 `AuthorizationScope.Tenant`；`HostOnly`、`Global` 保留 `Host` 权限范围，全局数据访问不自动授予租户权限。升级前已接入的 Host 授权块与新租户片段不一致时会保持原文并拒绝自动改写，应先人工审查作用域并完成实际授权验收。

## 验证

新创建应用从应用根运行 `dotnet run --project src/<name>.Host.Migrator -- --seed baseline`，迁移成功后才执行显式播种；省略 `--seed` 只迁移。仅本地开发环境显式选择 Development 后才能使用 `--seed development`，Production仍只允许Baseline。API和Migrator消费同一应用Composition，但Migrator只注册模块的迁移/播种入口，不能装入API Profile。现阶段Runner仍只运行冻结预设的框架脚本；生成业务SQL草案须完成编号、所有权、恢复与双库评审后显式接入，不能放进受管框架目录。旧应用的源码升级不会自动创建该应用拥有的宿主，需按新模板显式采用；默认结构校验兼容旧应用，创建发布前则强制要求同名Migrator与一致配置。

内容根声明 `fullnet-app.json` 时，Migrator 要求 `framework-manifest.json` 包含有效的预设迁移清单；文件缺失、清单不完整或 `unscoped` 会在解析数据库连接前停止，防止静默扩大为全部框架迁移。未声明应用的框架工作区保留原有非限定兼容行为。该检查依赖内容根中的应用声明，不替代发布目录的配置核验。

显式限定清单的脚本名必须唯一，并精确对应当前框架程序集中的 SQL Server/MySQL 成对资源；未知、截短、大小写漂移或带路径名称会在连接解析前拒绝。此校验允许有效子集，不验证 SQL 摘要、预设归属或数据库当前结构，也不是业务草案注册入口。

应用可以在自有 Migrator 中显式替换 `IDatabaseMigrationRunner`：包装框架 `DbUpMigrationRunner`，先等待框架迁移成功，再运行已编号并经双库评审的业务脚本，返回合计执行数；任一阶段失败或取消时，既有工作流不得继续播种。业务脚本只嵌入应用 Migrator，使用含 owner/module 的稳定 journal 名称，不能进入 API、模块项目或受管框架迁移目录。默认模板不会自动采纳业务草稿。

独立应用真实栈验收增加这一显式路径：先用应用包内 CLI 生成 `acme_catalog_product` 双库草稿，再将原文字节采纳为应用自有 `001_CreateProduct.sql` 并登记固定资源。首次运行要求业务脚本执行数为 1，重复迁移要求框架与业务均为 0；每个提供程序的采纳摘要和执行结果保存在 `.tmp/template-real-stack/<provider>/application-migration-*.json`，双库实际结果以对应提交的 Actions 为准。这一阶段验证迁移执行与记账，尚不证明生成业务 HTTP CRUD、跨租户拒绝、页面或完整 F02 验收通过。

真实栈还复用模块、Composition 与授权贡献者的 CLI 接线，将生成业务端点装入 API。对 `catalog/products` 的列表、详情、新增、更新、硬删除逐一发送匿名请求和真实 Host 引导管理员请求，分别要求 401 与 403，以及对应 ProblemDetails 机器码；404、重定向、500 或 Host 认证失效均不能计为权限拒绝成功。请求证据写入各提供程序目录的 `application-crud-http-denial.json`，不记录 Bearer 令牌，失败时保留已执行请求。本阶段只覆盖匿名和 Host 对租户业务的拒绝，不证明有权限租户 CRUD、无权限租户或跨租户数据隔离，实际结果仍以对应提交双库 Actions 为准。

租户 CRUD 阶段复用真实可用租户目录，精确选择 Development `local`，通过上下文切换 API 取得新令牌；业务请求不提供 TenantId，不直接写库或构造 Claims。验收新增、按 ID 与列表读取、版本更新，以及旧版本更新/删除返回 409 后再次读取确认内容与版本未变，最后硬删除并确认 404 与列表移除；响应同时核对 UUID v7、服务端 TenantId、字符串 Version 和标准机器码。报告为各提供程序目录的 `application-crud-tenant-http.json`，省略签发响应正文并脱敏令牌。该主体是 Host 管理员经授权切入租户上下文；实际结果由对应提交的双库 Actions 确认，不能据此证明普通租户账号精确权限、无权限租户或跨租户数据隔离。

双租户隔离阶段仅在内存续接最新会话，先经上下文 API 返回 Host，再通过租户开通 API 创建本次独立数据库中的第二租户。双方各创建一条产品，双向对对方 ID 的读取、更新、删除均要求 `catalog.products.not_found`／404，列表只含当前租户记录；切回后核对双方 Name、TenantId、Version 未变，最后分别删除自有记录并核验 404 与空列表。每次切换只使用新签发令牌，业务请求不传 TenantId。报告为 `application-crud-tenant-isolation.json`，不写入续接令牌或签发正文；双库实际行为以对应提交 Actions 为准。此阶段验证获授权 Host Actor 在不同有效租户上下文中的数据过滤，普通账号权限仍由独立阶段验收。

普通账号只读权限阶段通过公开 API 创建自定义角色和普通 Host 账号，分配租户上下文页面闭包 `tenancy.tenants.read`／`tenancy.tenants.switch`，产品仅授予 `catalog.products.read`。真实登录后携带服务端 CSRF Cookie 完成首次改密，核对 `/api/v1/me` 的非超级管理员标记及 Host/租户精确权限，再读取管理员创建的产品。列表和按 ID 读取应成功，创建、更新、删除应返回标准 403／`authorization.permission_denied`；管理员再次读取和列表核对原行未变且无新增行，然后清理产品并返回 Host。`application-crud-read-permission.json` 不记录密码、Cookie、签发正文或令牌；本地 Node 测试仅验证验收门禁，实际账号、会话与双库结果须检查对应提交 Actions。该阶段只覆盖普通 Host Actor 切入租户后的只读精确权限，完全无产品权限账号和各写权限独立正向仍待验收。

1. 运行迁移并启动 Host.Api
2. 使用对应租户与精确权限的账号登录管理端，访问应用实际接入的生成页面
3. 执行租户 CRUD、无权限及跨租户拒绝用例，再验证二次生成和人工修改保护；F02 完整验收尚未关闭

SQL Server 租户草案在建表后独立探测并创建租户聚集索引，使“表已创建、索引未完成、迁移未记账”的重跑可以补齐索引。MySQL 草案的索引仍在单条原子建表语句内，不修复外部删除索引的状态。两份草案均不修复任意错误的既有表结构，正式迁移仍需双库恢复评审与显式接入。

## 故障排查

| 机器码 | 含义 | 处理 |
| --- | --- | --- |
| `DIAG_SDK_MISSING` | 未检测到 SDK | 安装 .NET 10 SDK |
| `DIAG_APPSETTINGS_INVALID` | JSON 语法、结构或字段类型无效 | 修正配置类型，诊断不输出字段值 |
| `DIAG_WORKSPACE_INCOMPLETE` | 目录结构不完整 | 确认在应用根目录运行 |
| `DIAG_MODULES_MISSING` | 未配置模块预设 | 添加 `FullNet:Modules:Preset` |
| `DIAG_CONNECTION_PLACEHOLDER` | 开发环境缺连接 | user-secrets 或环境变量 |
| `DIAG_SECRETS_PLACEHOLDER` | 秘密仍为占位符 | 注入 Redis/加密密钥，勿提交仓库 |

## 实走记录（2026-09-17，企业预设收口）

在仓库根执行：

```bash
dotnet build src/Tools/Full.NET.CodeGeneration.Cli -c Release
dotnet exec src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll diagnose --workspace .
```

结果：`DIAG_WORKSPACE_OK`、`DIAG_SDK_OK`；连接串与 `FullNet:Modules` 为占位 warn（仓库根非应用模板，符合预期）。Enterprise Request 样例仅 Schema 测试通过（F09 骨架）。

无产品权限普通账号阶段复用上述公开账号、角色、登录与CSRF首次改密链，使用独立角色和账号，只授予租户上下文页面闭包 `tenancy.tenants.read`／`tenancy.tenants.switch`，不授予任何 `catalog.products.*` 权限。Host与租户 `/api/v1/me` 均核对这两项精确集合、非超级管理员及改密完成；管理员预建产品后，该账号对列表、按ID读取、创建、更新、删除五个入口都应返回403／`authorization.permission_denied`。管理员再读和列表确认原行版本内容保持且没有新增，然后清理行并返回Host。各提供程序报告为 `application-crud-no-permission.json`，不保存凭据；真实账号与双库结果仍须对应SHA Actions报告确认，各写权限独立正向还须单独验收。

普通账号创建权限阶段使用独立角色与账号，权限固定为产品页面闭包 `catalog.products.read`／`catalog.products.create` 和租户上下文 `tenancy.tenants.read`／`tenancy.tenants.switch`；仍通过真实登录、CSRF首次改密及 `/api/v1/me` 精确权限核验。它可列表/读取管理员预建行并创建新行201，新行必须是不同UUID v7、当前可信TenantId、预期Name和字符串Version1；更新与删除仍须403。管理员读取原行并核对列表恰含两条完整记录，再清理两行、核对普通创建行404，返回Host后仅内存续接新令牌。报告为 `application-crud-create-permission.json`，真实创建、拒绝与清理必须以对应SHA双库Actions为准；更新和删除权限独立正向仍需后续验收。

普通账号更新权限阶段使用独立角色与账号，固定授予产品 `catalog.products.read`／`catalog.products.update` 与租户上下文read/switch闭包，真实首次改密后核对Host/Tenant精确权限与非超级管理员。创建403，读取原行后更新200，将Version字符串1推进到2并保持Id/TenantId；旧版本1更新必须409／`catalog.products.version_conflict`，立即读取确认已提交内容和版本保持，删除仍403。管理员再读和唯一行列表检查持久化及无新增，再用Version2清理，回Host仅内存续接。报告为 `application-crud-update-permission.json`，实际账号/双库更新与冲突行为须对应SHA Actions确认；删除权限独立正向仍待补齐。

普通账号删除权限阶段使用独立角色与账号，固定授予产品 `catalog.products.disable`／`catalog.products.read` 和租户上下文read/switch。当前hard.delete路由沿用既有Disable权限机器码；不新增delete权限。真实首次改密及me精确权限核验后，创建和更新403，当前Version1产品用不匹配Version2删除须409且再读保持原行；正确Version1删除200返回原行。普通账号与管理员分别核对GET404和空列表，确认实际消失，再返回Host。报告为 `application-crud-delete-permission.json`，无需管理员重复删除；固定五类权限入口仍须以对应SHA双库Actions证明，不把Node替身计为真实账号/数据库通过。

OpenAPI 接入阶段在独立应用真实 API 就绪后匿名读取 `/openapi/v1.json`，对照只读生成文件 `contracts/openapi/products.generated.openapi.json`，逐项核对五个产品操作的路径（仅规范化尾斜杠）、operationId、成功状态和 Bearer 声明。三种写请求的字段集合不能暴露Id/TenantId/创建审计；五种成功响应的字段集合须与生成契约一致，列表比较items元素。只解析文档内components/schemas引用，拒绝缺失、外部及循环引用；报告 `application-crud-openapi.json` 保存实际比较和失败部分证据，生成文件字节保持。此阶段仅证明真实文档的路由、安全声明及字段接入，完整错误元数据、数字/字符串类型细节与Vue客户端仍须后续验收，不能仅凭Node替身认定双库文档服务通过。

认证错误元数据补齐：生成 Endpoint 的每个受保护操作现在显式声明401/403 `ProducesProblem`，与生成静态契约和既有真实HTTP拒绝保持一致；不改变运行授权或返回行为。OpenAPI门禁对五操作逐项要求这两状态具有 `application/problem+json` 与可解析ProblemDetails/status，报告 `authenticationProblems: 10`。404/409等完整操作错误集、机器码扩展schema及数字/字符串类型细节仍须后续，不把新增注解算作全部契约认证。

参数契约子集：同一真实文档门禁继续比较五操作的参数名称、query/path位置、必填性、基础标量类型与format，报告 `parameterShapes: 5`。路径ID必须保持必填UUID，分页保持可选int32，不能出现额外TenantId参数、缺失或重复参数。可选查询参数允许CLR可空schema与参数顺序差异；不由此放宽路径可空。此子集不验证分页默认值/范围、请求响应字段类型或完整错误契约，真实接入仍由对应SHA双库Actions证明。

认证错误基础字段子集：五操作各401/403的ProblemDetails须声明type/title/status/detail/instance；status非空类型为integer/int32，其余为string。容忍标准字段可空或非必填、业务扩展字段，文档内字段schema引用仍须可解析；外部、缺失或循环引用拒绝。报告每状态的 `authenticationProblemFields`，不以字段存在代替类型检查，也不据此声称机器码扩展、404/409等完整错误集已认证。

ASP.NET 数字读取兼容：实际文档的分页参数及ProblemDetails.status可以声明integer|string。门禁仅在类型恰为这两项、format为int32且pattern为 `^-?(?:0|[1-9]\d*)$` 时归一为整数；可空处理沿既有边界，路径UUID不适用。缺少整数约束、任意字符串/对象或int64仍拒绝。报告同时保留 `parameterDeclarations` 和 `authenticationProblemDeclarations` 原始schema声明，失败时可核对实际生成形态；规范化结果不等于改变运行时序列化。

生成模块人工修改保护：独立应用对六个受管产物（模块注册、Contracts、SQL、Endpoint、Feature、Record）逐个追加测试注释并重新apply。每轮须退出2；注册桥在规划前失败，stdout为空且stderr含确切原因与路径；五实体产物的完整计划仅目标Conflict、其他Unchanged。所有生成文件、manifest、人工文件及宿主内容字节保持；验收通过后仅撤销本轮注释。报告列出conflictArtifacts和六轮独立CLI证据。此阶段验证覆盖保护，不替代真实业务人工扩展或Vue再生成的完整验收。

远端证据：`e056a683` 的独立生成应用双库作业（[CI 36310169977](https://github.com/yan041108/Full.NET/actions/runs/36310169977/job/108594450730)）成功367/367、零失败/跳过。两库报告均完成五操作、五参数形态、十认证错误及基础字段、三请求与五响应字段比较；原始schema确认ASP.NET数字兼容约束，生成输入保持。此证据覆盖OpenAPI已实现子集，未覆盖后续六产物保护、完整业务错误或Vue，不据此关闭F02。

六产物保护远端证据：`1b5dab04` 的[独立应用双库作业](https://github.com/yan041108/Full.NET/actions/runs/36310555699/job/108595520455)成功370/370、零失败/跳过。两库各六轮实际CLI冲突均退出2，包含桥的规划前诊断和五实体完整冲突计划；文件保持及测试注释清理完成，后续真实CRUD与OpenAPI子集继续通过。此证据不替代人工业务扩展及Vue完整再生成，F02仍未关闭。

应用公开操作清单：仓库客户端生成工具可用 `node scripts/openapi/generate-fullnet-client.mjs --input <应用OpenAPI文件> --manifest <应用清单文件> --output <客户端产物目录>`；清单的publicOperationIds为唯一非空操作名数组，未列出的匿名操作拒绝生成。未指定manifest仍使用工具所在仓库或应用的默认清单。此入口提供应用契约边界输入；新模板分发路径见下文，Vue接入/编译仍待验收，不要用客户端生成成功替代页面验收。

新模板将客户端生成脚本及校验器分发至 `.fullnet-tools/openapi/`，默认OpenAPI和公开操作清单位于应用 `contracts/openapi/`。在应用目录运行 `node .fullnet-tools/openapi/generate-fullnet-client.mjs --check` 检查其冻结客户端基线；需要业务输入时显式传 `--input`、`--manifest`、`--output`。这些文件来自同一固定源码包及摘要，初始化后应用拥有副本；旧应用需显式采用，框架源码升级不会自动覆盖应用工具/契约。工具可运行不等于业务客户端或Vue接入已验收。

工具分发远端证据：`43bf0718` 的[独立应用双库作业](https://github.com/yan041108/Full.NET/actions/runs/36315494268/job/108609299088)成功374/374、零失败/跳过。实际模板创建核对四份工具/契约副本与冻结源码字节一致；SQL Server/MySQL 的 `application-client-tools.json` 均记录应用自带工具退出0、生成产物零漂移、stderr为空。此证据验证默认客户端基线，不覆盖业务客户端接线、Vue编译或页面使用，F02仍未关闭。

业务客户端独立目录：生成业务契约时可追加 `--http-module @fullnet/client-contracts`，让操作文件从共享包导入 `HttpClient` 与 `RequestOptions` 类型；默认仍引用 `../http.js`，保持框架基线。使用 `--check` 时须带相同引用参数。业务生成目录应由应用单独拥有，保留共享包已有认证和基础操作；使用方仍须配置包解析并显式接入业务适配器。此选项仅改变类型引用，不自动注册路由或改变运行时请求、认证及租户上下文。
