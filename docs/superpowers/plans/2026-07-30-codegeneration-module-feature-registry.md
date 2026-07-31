# CodeGeneration Module Feature Registry Implementation Plan

**Goal:** 为同一模块内的全部生成实体维护一个稳定的聚合注册桥，使手写模块入口只需长期调用一次服务注册和一次 Endpoint 注册。

**Scope:** 仅生成并编译验证 `Generated/FullNetGeneratedModuleFeatures.g.cs`；不自动修改模块入口、Composition、Vue/Layui 路由、迁移或客户端产物。

## Task 1: Establish registry ownership and deterministic generation

- [x] 先补工作区测试：首个实体创建聚合桥，第二个实体更新聚合桥并保留既有实体，顺序按实体 CLR 名称稳定排序。
- [x] 修改模块后端工作区：聚合桥与实体后端文件共享同一 Manifest；聚合桥缺失或被人工修改时关闭失败。
- [x] 保持 `CreateArtifacts` 的实体级语义，另行暴露聚合桥产物创建入口。

## Task 2: Compile the exact candidate module registry

- [x] 先补投影和真实 Apply 测试：编译探针改为调用稳定聚合方法，多实体 Apply 的候选聚合桥必须引用磁盘中已拥有的其他实体。
- [x] 临时编译投影接收显式候选产物，并从目标项目移除所有被候选替换的源文件。
- [x] Apply 仅在完整候选聚合桥真实 Release 编译通过后提交工作区。

## Task 3: Align read-only integration guidance

- [x] `plan-module-integration` 只检查 `AddFullNetGeneratedModuleFeatures` 与 `MapFullNetGeneratedModuleFeatures`。
- [x] 更新 CodeGeneration 路线图措辞，但不改 Jobs 行和 037 测试选择。
- [x] 运行聚焦测试、完整 CodeGeneration 测试、受影响计划、静态检查和临时目录泄漏检查。

## Acceptance

- 首次 Apply：五个实体后端文件加一个聚合桥。
- 第二实体 Apply：新增五个实体文件、更新同一聚合桥，Manifest 共十一项。
- 重复 Apply 幂等；人工修改任一受管实体或聚合桥均零写入失败。
- 聚合桥和探针通过目标模块真实 Release 编译；手写接入边界保持不变。

## Verification Evidence

- TDD RED：工作区测试先因缺少 `RegistryRelativePath` 无法编译；真实 Integration 随后准确暴露关闭 implicit usings 的目标模块缺少显式 `System` 引用。
- Release 构建：`Full.NET.Data.CodeGeneration` 与 `Full.NET.CodeGeneration.Cli` 均为 0 警告、0 错误。
- Unit：CodeGeneration `139/139`；全量 Unit `654/654`。
- Integration：CodeGeneration `14/14`；受影响 `inner` 阶段同时通过测试工具链 `38/38`、Governance `16/16` 与 CodeGeneration `14/14`。
- Integration 分片：SQL Server API `38`、MySQL API `38`、迁移 `70`、Infrastructure `79`，合计 `225`，无遗漏或重复。
- Naming：`23/23`；`git diff --check` 通过；本任务隔离构建目录和系统临时编译目录均无残留。
- `slice` 计划被共享工作区中并发的 Jobs/Notifications 变更扩大为约 7 分钟；本窗口没有重复执行范围外模块，完整集合仍由 main CI 分片门禁承担。
- 规则演进未命中；现有 `fullnet-module-delivery` 已覆盖流程，没有形成新的 Skill 契约缺口。
