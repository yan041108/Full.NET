# Task 3A：E2E 场景数据发布物隔离报告

- 日期：2026-07-22
- 分支：`main`
- 起始基线：`d583f846cf1533a2fadc7af40ce7d3acdfe605e1`
- 范围：架构硬化 Task 3A；未进入 Task 3B 或 P1
- 状态：实现与不依赖容器的验证已完成；SQL Server/MySQL 真实栈因当前机器缺少容器运行时而未验证

## 1. 验收映射

| 要求 | 实现与证据 |
|---|---|
| 发布程序集拒绝测试场景类型、Contributor 名和配置节 | `SeedingDependencyRulesTests` 扫描 Identity、Tenancy、Organization、API、Worker、Migrator 程序集；检查类型/配置属性中的 `E2e`、`TestOnly`，并检查 Contributor 常量名称 |
| Identity 只注册生产 Baseline Contributor | 删除 `E2eHostViewerSeedContributor` 与 `IdentityE2eViewerOptions`，移除 `IdentityOptions.E2eViewer` 和 DI 注册；Unit 断言只注册 `HostAdministratorSeedContributor` |
| 场景查看者由测试目录经真实 API 创建 | 新增 `provision-viewer.mjs`：Bootstrap 管理员登录后依次调用 Host 角色、角色权限、Host 用户和用户角色 API |
| 准备脚本幂等 | 按固定角色代码和大小写不敏感用户名查询；出现重复立即失败；bootstrap 连续执行两次 provisioner，第二次复用相同角色和用户 |
| Host 不接收旧测试配置 | bootstrap 显式过滤 `Identity__E2eViewer__*`，不再注入旧配置模型 |
| 三宿主发布物零命中 | API、Worker、Migrator Release publish 后扫描 E2E 类型名、默认用户名和旧配置键，结果零命中 |
| Seed 事实源与门槛同步 | 更新 Development Seed 双库预期、Seed 验证记录、能力矩阵、README、getting-started、CI、Skill delivery-map 与测试门槛审计 |

## 2. RED 证据

先只修改 `tests/Full.NET.ArchitectureTests/SeedingDependencyRulesTests.cs`，未修改生产代码。

命令：

```powershell
dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-restore
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 27
```

结果：构建通过；测试 **28 总计、27 通过、1 失败、0 跳过**。新门禁按预期报告 7 项违规：

- `IdentityOptions.E2eViewer`；
- `IdentityE2eViewerOptions`；
- `E2eHostViewerSeedContributor`；
- Contributor 的 4 个编译器生成类型。

失败来自现有生产程序集携带 E2E 类型和配置节，不是测试发现、语法或环境错误。

## 3. GREEN 实现

1. Identity 删除 E2E Contributor、配置模型、Options 属性和 DI 注册；删除对应生产类型单测。
2. `HostAdministratorSeedContributorTests` 将模块 Contributor 注册契约收紧为唯一的生产 Baseline Contributor。
3. `DevelopmentSeedTests` 的 Development Contributor 集合改为 `identity.host_administrator` 与 `tenancy.local_tenant`，不再容忍发布程序集内的场景 Contributor。
4. 新 provisioner 只读取 `FULLNET_E2E_*` 测试环境变量；API 健康后通过真实管理 API 建立受限角色、四项查看权限、用户和用户角色关系。
5. bootstrap 连续执行两次 provisioner，并在传给 Migrator/API 的环境中剔除旧 `Identity__E2eViewer__*`。
6. 新 Architecture Test 同时保留既有 API/Worker 不依赖 Seed 执行器的断言；新增门禁只扫描批准的生产模块和 Host 程序集。

## 4. GREEN 验证

| 验证 | 结果 |
|---|---|
| `dotnet build Full.NET.slnx -c Release --no-restore` | 通过，0 警告、0 错误 |
| UnitTests `--minimum-expected-tests 332` | **332/332** 通过，0 失败、0 跳过 |
| CompatibilityTests `--minimum-expected-tests 7` | **7/7** 通过，0 失败、0 跳过 |
| ArchitectureTests `--minimum-expected-tests 28` | **28/28** 通过，0 失败、0 跳过 |
| `pnpm test:naming` | **23/23** 通过 |
| `pnpm test:governance` | **7/7** 通过；包含新增门槛一致性检查 |
| `pnpm test:skills` | `fullnet-module-delivery` **48/48** 合同检查通过 |
| `node --check` 两个真实栈脚本 | 通过 |
| API / Worker / Migrator Release publish | 三个宿主全部成功 |
| 发布物扫描 | `E2eHostViewer`、`IdentityE2eViewer`、`e2e-viewer`、`Identity:E2eViewer`、`Identity__E2eViewer` **零命中** |
| 临时发布目录 | 扫描后已从系统临时目录删除，未进入工作区 |

## 5. 未验证项

以下命令已执行，但在数据库启动前因外部环境失败：

```powershell
pnpm test:e2e:real
```

Testcontainers 报错：`Could not find a working container runtime strategy`。当前机器没有可用 Docker/容器运行时，因此：

- SQL Server 与 MySQL 的更新后 `DevelopmentSeedTests` 未新鲜重跑；
- SQL Server 与 MySQL 的受限查看者登录、导航裁剪和 API 403 未新鲜重跑；
- provisioner 连续执行两次的真实数据库去重效果仅由实现路径和既有 API 契约审查确认，未获得真实栈运行证据。

这些项目不得表述为通过；应在具备容器运行时的 CI 或开发机运行 `pnpm test:e2e:real`、`pnpm test:e2e:real:mysql` 以及 Integration 全量门槛 `109`。

## 6. 文件清单

### 生产与测试实现

- 删除 `src/Modules/Full.NET.Modules.Identity/Seeding/E2eHostViewerSeedContributor.cs`
- 删除 `src/Modules/Full.NET.Modules.Identity/Configuration/IdentityE2eViewerOptions.cs`
- 修改 `src/Modules/Full.NET.Modules.Identity/Configuration/IdentityOptions.cs`
- 修改 `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- 删除 `tests/Full.NET.UnitTests/Identity/E2eHostViewerSeedContributorTests.cs`
- 修改 `tests/Full.NET.UnitTests/Identity/HostAdministratorSeedContributorTests.cs`
- 修改 `tests/Full.NET.IntegrationTests/Seeding/DevelopmentSeedTests.cs`
- 修改 `tests/Full.NET.ArchitectureTests/SeedingDependencyRulesTests.cs`
- 新增 `tests/e2e/admin-real-stack/scripts/provision-viewer.mjs`
- 修改 `tests/e2e/admin-real-stack/scripts/bootstrap-stack.mjs`

### 文档、门槛与治理

- 修改 `README.md`
- 修改 `docs/development/getting-started.md`
- 修改 `.github/workflows/ci.yml`
- 修改 `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- 修改 `docs/verification/seed-dual-database-contract-2026-07-21.md`
- 修改 `docs/verification/test-threshold-audit-2026-07-19.md`
- 修改 `docs/roadmap/capability-status.md`
- 修改 `rules/development-quality.md`
- 修改 `rules/rule-evolution.md`
- 修改 `rules/skill-evolution.md`
- 修改 `tests/governance/agents-rules-consistency.test.mjs`
- 新增 `.superpowers/sdd/task-3A-report.md`

## 7. 规则与 Skill 复盘

- 规则已演进：既有候选 `C-20260721-test-threshold-audit-drift` 本次第二次命中，达到重复性门槛。`development-quality.md` 现要求四个 canonical 门槛来源与最新 `test-threshold-audit` 同步；治理测试已从 RED（规则缺失）转为 **7/7 GREEN**，候选登记已移除。
- Skill 已修改：只机械同步 `fullnet-module-delivery` delivery-map 的 Unit/Architecture 数量，Skill 合同 **48/48** 通过。
- Skill 候选已更新：`fullnet-seed-data-delivery` 记为第 5 次证据；因 SQL Server/MySQL 新真实栈路径尚未验证，不升级为新 Skill。

## 8. 自审结论

- 变更未进入 Task 3B/P1，没有数据库结构、公共 API 或序列化契约变化。
- 场景凭据和查看者默认值只存在于测试目录，不进入 Identity/Host 发布物。
- provisioner 使用现有受权 Host 管理 API，不直连数据库；固定自然键、重复检测和精确角色集合保证隔离测试数据库内的幂等语义。
- 用户现有未跟踪 `.txt` 日志未删除、未修改、不会提交。
- 其他代理正在修改的 `AGENTS.md`、`fullnet-module-delivery/SKILL.md` 与硬化计划不属于本提交，不会暂存。
- 唯一剩余关注点是当前机器缺少容器运行时导致双库真实栈未验证。
