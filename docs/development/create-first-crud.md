# 创建首个 CRUD 与环境诊断

本教程记录从 Full.NET 应用模板创建新项目后的只读诊断入口，以及 CRUD 生成与接入步骤。独立应用的完整生成 CRUD、OpenAPI、Vue、跨租户拒绝与再生成保护仍按总计划 F02 验收，不能把模板字典 CRUD 冒烟作为生成业务验收。

## 前置条件

- 已安装 .NET 10 SDK（`dotnet --version` 可执行）
- 独立应用根目录包含 `fullnet-app.json`、`framework-manifest.json`、`src/<name>.Host.Api` 与 `framework/fullnet/`；`src/Composition`、`src/Hosts`、`src/Modules` 是原框架仓库的布局
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

诊断按目标工作区的 `global.json` 解析 SDK，检查宿主 `appsettings.json`、独立应用清单、所选模块引用及配置占位符；不会执行初始化、迁移或数据库连接，也不证明配置中的地址可达。它不是完整 ASP.NET Core 配置加载器，不认证部署环境的全部覆盖来源。SDK 缺失导致 .NET CLI 本身无法启动时，先安装 .NET 10 SDK，再运行此入口。

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

1. 运行迁移并启动 Host.Api
2. 使用对应租户与精确权限的账号登录管理端，访问应用实际接入的生成页面
3. 执行租户 CRUD、无权限及跨租户拒绝用例，再验证二次生成和人工修改保护；F02 完整验收尚未关闭

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
