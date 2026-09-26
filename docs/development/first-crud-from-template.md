# 从模板到首个 CRUD（入口与待验收范围）

1. 组装并创建应用（见 [`templates/fullnet-app/README.md`](../../templates/fullnet-app/README.md)）。
2. 诊断：`pnpm run diagnose:development`（应用根目录）。
3. 配置 `ConnectionStrings:app`。仅在本地开发时显式设置 Migrator 的 `DOTNET_ENVIRONMENT=Development`（PowerShell：`$env:DOTNET_ENVIRONMENT='Development'`；Bash：`export DOTNET_ENVIRONMENT=Development`），再运行 `dotnet run --project framework/fullnet/src/Hosts/Full.NET.Host.Migrator -- --seed development`。默认 Production 环境拒绝 Development Overlay；生产仍只允许 Baseline。
4. 使用 [`Full.NET.CodeGeneration.Cli`](../../src/Tools/Full.NET.CodeGeneration.Cli/) 在应用工作区生成租户 CRUD（表名遵循 `{owner}_*` 命名，见 [`samples/enterprise-request`](../../samples/enterprise-request)）。模板 API 通过 `src/<name>.Composition/ApplicationModuleCatalog.cs` 装配应用模块；接入目标使用该应用自有项目和标准 `CreateModules()` 清单，不修改 `framework/fullnet/` 的受管官方目录。三个 Profile 的最小注册入口可复用同一应用清单；模板目前只提供 API 宿主，应用 Migrator 及业务迁移仍待接入与验收。
5. 二次生成应保留未登记的人工文件；人工修改的受管 Handler/SQL 应触发冲突并拒绝覆盖，不能把自动保留误解为自动合并。独立应用完整生成业务验收仍按总计划 F02 推进，具体入口与限制见[首个 CRUD 教程](create-first-crud.md)。

原框架仓库的快速验证使用矩阵包装器与最低发现门禁；模板真实双库验收由 GitHub Actions 执行。独立应用不含原仓库全部测试项目，不能在应用目录照抄 `tests/Full.NET.UnitTests` 命令。

企业申请样例的创建入口通过 `X-FullNet-Organization-Unit-Id` 传递机构选择，服务端按当前租户与用户校验组织权限；JSON 只包含业务可写字段，创建/修改/删除操作人由服务端确定。浏览器验收入口为 `enterprise-request.spec.mjs --project vue-admin`，实际租户 CRUD 与 API 的审批、CSV 导入验收分别记录。

生成客户端读取器会按 OpenAPI 明确声明的整数 JSON 编码，将合法整数字符串归一为既有 TypeScript `number`，并拒绝超出安全整数范围的值；不会转换金额字符串或其他普通文本。嵌套引用、数组和联合分支使用相同边界。需要超过 `Number.MAX_SAFE_INTEGER` 的业务值时，应另行定义显式字符串契约，不能静默截断精度。
