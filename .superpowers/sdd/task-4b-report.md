# Task 4B 收尾报告：API 移除迁移执行能力

- 日期：2026-07-22
- 分支：`main`
- 基线：`788b6f4`
- 原实现提交 SHA：`e6245e1698e9f0bc92406743d5c59115a8e27112`
- Review 修复提交 SHA：由追加提交后的 `git rev-parse HEAD` 结果及任务回报记录；Git 提交对象不能在自身内容中预写自身 SHA。
- 范围：仅关闭 API Host 的 DbUp 迁移执行职责，不实施 Task 4C。

## 1. RED 证据

前一实现代理已在共享工作区完成测试与最小实现，因此未回退正确实现重跑破坏性 RED。采用新架构契约与基线差异建立可审计 RED：

```powershell
git grep -n -e 'Full.NET.Migrations.DbUp' -e 'AddFullNetMigrations' -e 'IDatabaseMigrationRunner' 788b6f4 -- `
  src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj `
  src/Hosts/Full.NET.Host.Api/Program.cs `
  tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs
```

结果：基线准确命中 API 项目对 `Full.NET.Migrations.DbUp` 的项目引用、`Program.cs` 的命名空间与 `AddFullNetMigrations` 注册，以及测试夹具从 API DI 解析 `IDatabaseMigrationRunner` 的二次迁移路径。新增 `Migration_execution_is_owned_by_migrator_and_excluded_from_api_host` 契约会拒绝前三项生产命中，失败原因与 Task 4B 目标一致。

## 2. GREEN 实现

1. API 项目删除 `Full.NET.Migrations.DbUp` 项目引用。
2. API `Program.cs` 删除迁移命名空间与 `AddFullNetMigrations` 注册。
3. `FullNetApiFactory.InitializeAsync` 保留启动 API 前直接构造 `DbUpMigrationRunner` 的一次迁移，删除 API Scope 中解析并再次执行迁移器的路径；租户与管理员初始化仍在 API Scope 内执行。
4. Architecture Test 扫描全部 `.csproj`：生产迁移消费者只允许 `Full.NET.Host.Migrator`，测试项目可显式消费；同时扫描 API 手写 C# 源码并拒绝 `AddFullNetMigrations`/`IDatabaseMigrationRunner`。
5. Architecture 门槛从 30 提升为 31，并同步 README、getting-started、CI、项目 Skill delivery-map、门槛审计、计划、能力矩阵与架构复核记录。

## 3. 新鲜验证

| 验证 | 结果 |
| --- | --- |
| `dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release` | 通过，0 warning / 0 error |
| Architecture Tests，`--minimum-expected-tests 31` | **31/31** 通过 |
| `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release` | 通过，0 warning / 0 error |
| SQL Server/MySQL API：filter=`Api_resolves_tenant_and_returns_standard_http_contract` | **2/2** 通过；测试夹具仍可完成迁移、租户与管理员初始化 |
| SQL Server/MySQL migration idempotence：filter=`migration_is_idempotent_and_creates_binary_outbox_schema` | **2/2** 通过 |
| `dotnet publish src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj -c Release --no-restore` | 通过；111 个文件 |
| 发布物扫描 | `.deps.json` 中 `Full.NET.Migrations.DbUp` **0** 命中；`Full.NET.Migrations.DbUp*.dll` **0** 个 |
| `pnpm test:governance` | **7/7** 通过 |
| `pnpm test:skills` | `fullnet-module-delivery` **48 checks** 通过 |
| `dotnet build Full.NET.slnx -c Release --no-restore` | 通过，0 warning / 0 error |

发布扫描使用的独立临时目录为 `C:\Users\Administrator\AppData\Local\Temp\fullnet-task-4b-api-52b7a35f1f1c48d59dc7d188dd9d7969`。自动清理命令被安全策略拒绝后未继续删除；该目录位于仓库外，不影响工作区或提交。

## 4. 文件清单

### 实现与测试

- `src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj`
- `src/Hosts/Full.NET.Host.Api/Program.cs`
- `tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs`
- `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`

### 门槛与文档

- `README.md`
- `docs/development/getting-started.md`
- `.github/workflows/ci.yml`
- `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- `docs/superpowers/plans/2026-07-18-architecture-hardening.md`
- `docs/roadmap/capability-status.md`
- `docs/verification/architecture-review-2026-07-22.md`
- `docs/verification/test-threshold-audit-2026-07-19.md`
- `.superpowers/sdd/task-4b-report.md`

## 5. 自审与复盘

- 范围：未修改 Migrator Profile、模块注册或 Task 4C 文件；根目录未跟踪 `.txt` 日志未读取为权威证据、未修改、未暂存。
- API 契约：未改变公开 HTTP API、JSON、权限、租户或数据库结构；测试项目仍可显式消费迁移组件。
- 双库：SQL Server 与 MySQL API 聚焦、迁移幂等均在当前容器栈实际执行通过；没有用单库结果推断双库。
- 发布边界：项目引用、源码令牌和最终发布物三层均已检查。
- 规则复盘：`rules/development-quality.md` 第 3 节第 8 条已经明确禁止 API 引用、注册、解析或执行 DbUp，并规定仅 Migrator/显式测试基础设施可执行；本次新增自动化防回归，未出现达到升级门槛的新事实，因此无规则变化。
- Skills 复盘：本次流程已由 `fullnet-module-delivery` 覆盖；仅机械同步 Architecture 门槛至 delivery-map，`pnpm test:skills` 通过。没有重复且稳定的新判断工作流，也没有新增/实质演进 Skill。
- 关注点：Task 4C 仍须把 Migrator 从完整 HTTP 模块装配收敛到最小 Migration/Seed Profile；本任务不提前处理。

## 6. 最终 Git 门禁

提交前重新执行 `git diff --check`、Task 4B 路径暂存清单核对、`git status --short --branch` 与 `git branch --show-current`。最终提交只包含上述 Task 4B 文件；未跟踪 `.txt` 保持原状。

## 7. 独立 review 修复

Reviewer 指出两个 Important：直接迁移消费者扫描使用 `Path.GetFileName` 解析反斜杠 Include，在 Linux 上可能漏报；同时 API 门禁没有递归遍历 `ProjectReference`，无法阻止间接重新携带迁移程序集。

### 7.1 Review RED

先加入两个临时 `.csproj` 项目图夹具：

1. `Migration_project_reference_scanner_handles_both_separator_styles` 同时使用 `..\Migration\...` 与 `../Migration/...`。
2. `Api_project_dependency_closure_detects_transitive_migration_reference` 构造 API→Bridge→DbUp 两跳依赖，并混用正、反斜杠。

```powershell
dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll `
  --no-ansi --progress off --minimum-expected-tests 33
```

RED 结果：Build 通过，0 warning / 0 error；Architecture **32/33**，`Api_project_dependency_closure_detects_transitive_migration_reference` 按预期失败，实际闭包只有 `Api/Full.NET.Host.Api.csproj` 与 `Bridge/Full.NET.Migration.Bridge.csproj`，缺少第二跳 `Migration/Full.NET.Migrations.DbUp.csproj`。分隔符夹具在当前 Windows 主机通过；它与闭包夹具均在实现前加入并由同一失败运行覆盖，不能把 Windows 通过伪写成分隔符 RED。首次 Task 4B 契约未在原实现前实际运行的历史事实仍按第 1 节记录，不以后补 review RED 冒充首次 RED。

### 7.2 Review GREEN

- 直接消费者扫描复用统一的 `GetProjectNameFromReference`，先把 `\` 规范为 `/`，不再依赖宿主平台解释 Include。
- 递归闭包把两种分隔符转换为当前平台分隔符，以绝对路径解析每个 ProjectReference，并用已访问集合防止项目环导致无限遍历。
- 主门禁继续保留“生产直接消费者只允许 Migrator”和 API 源码令牌检查，同时新增真实 API 项目递归闭包不得包含 `Full.NET.Migrations.DbUp`。
- Architecture 门槛从 **31** 更新为 **33**，四处 canonical 门槛同步为 **342/7/33/109**。

首次 GREEN：Architecture **33/33**，Build 0 warning / 0 error。

追加提交前最终刷新：Architecture **33/33**；`pnpm test:governance` **7/7**；`pnpm test:skills` **48 checks**；`dotnet build Full.NET.slnx -c Release --no-restore` 0 warning / 0 error；`git diff --check` 通过。规则复盘仍由现有 `development-quality.md` 第 3 节第 8 条完整覆盖，无新增规则；Skill 仅机械同步测试门槛，无实质演进。
