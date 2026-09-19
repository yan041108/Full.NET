# 创建首个 CRUD 与环境诊断

本教程面向从 Full.NET 应用模板创建的新项目，演示 **diagnose** 只读诊断与 **CRUD 生成器** 贯通数据库、后端、OpenAPI 与 Vue 管理端。

## 前置条件

- 已安装 .NET 10 SDK（`dotnet --version` 可执行）
- 应用根目录包含 `src/Composition`、`src/Hosts`、`src/Modules` 与 `Full.NET.slnx`
- `appsettings.json` 已配置 `FullNet:Modules:Preset`（如 `minimal` 或 `platform`）

## 第一步：运行 diagnose

在应用根目录执行：

```bash
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -- diagnose --workspace . --profile development
```

成功输出包含机器可读行，例如：

```
DIAG_SDK_OK ok 检测到 .NET SDK 10.x.x。
DIAG_WORKSPACE_OK ok 工作区结构符合 Full.NET 应用布局。
DIAG_MODULES_OK ok 已配置 FullNet:Modules 模块预设或启用列表。
```

开发环境若尚未配置数据库连接，可能出现 `DIAG_CONNECTION_PLACEHOLDER warn ... hint=...`；按 hint 使用 user-secrets 或环境变量注入，**诊断不会输出连接字符串原文**。

生产配置使用 `--profile production`；缺少连接或秘密占位符将报告 `error` 并以非零退出码结束。

## 第二步：准备 CRUD Schema

示例主从单据见 `samples/enterprise-request/schema.json`（`master.detail` 场景：申请头 + 明细行）。字段、权限与 `dataScope` 须在 JSON 中显式声明。

## 第三步：预览生成计划

```bash
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -- \
  --schema samples/enterprise-request/schema.json \
  --workspace .
```

输出 `Create`/`Update`/`Unchanged` 行，默认不写盘。

## 第四步：应用生成

确认计划后追加 `--apply`：

```bash
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -- \
  --schema samples/enterprise-request/schema.json \
  --workspace . \
  --apply
```

重复执行应报告 `Unchanged`，人工修改的业务文件不会被覆盖。

## 第五步：模块接入（可选）

使用 `samples/enterprise-request/integration-target.json` 规划 Composition、模块入口与 Vue 路由接入：

```bash
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -- plan-module-integration \
  --schema samples/enterprise-request/schema.json \
  --repository . \
  --target samples/enterprise-request/integration-target.json
```

按 `Missing`/`Ready` 项完成接线后再执行 `apply-module-integration` 等子命令。

## 验证

1. 运行迁移并启动 Host.Api
2. 使用 Host 管理员登录管理端，访问 `/enterprise-requests`
3. 执行租户 CRUD 与跨租户拒绝用例

## 故障排查

| 机器码 | 含义 | 处理 |
| --- | --- | --- |
| `DIAG_SDK_MISSING` | 未检测到 SDK | 安装 .NET 10 SDK |
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