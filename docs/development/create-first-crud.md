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
