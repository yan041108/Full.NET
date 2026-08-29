# BuildingBlocks 反向模块引用门禁验证

## 范围

- 基线提交：`22cd0c2b6d594c19289d002130401dda209807a3`
- 任务快照：`caching-abstraction-boundary`
- 本切片只补充项目引用防漂移门禁，不改变缓存运行时、TTL、L1/L2、Backplane 或 Native AOT 行为。

## 变更

新增 `BuildingBlockProjectDependencyBoundaryTests`，动态枚举 `src/BuildingBlocks` 下全部项目文件，并检查每个 `ProjectReference` 的目标项目名。任何以 `Full.NET.Modules.` 开头的目标（包括 `*.Contracts`）均失败。

该检查不依赖手写程序集清单，因此新增 BuildingBlock 或 Messaging Provider 时也会自动进入门禁。

## RED / GREEN

- RED：测试首次运行因 `BuildingBlockProjectDependencyGuard` 尚不存在而编译失败（`CS0103`），证明新门禁没有复用既有漏检逻辑。
- GREEN：补充最小项目文件扫描器后，聚焦测试 `2/2` 通过；其中一个合成断言明确覆盖 `Full.NET.Modules.Settings.Contracts` 前缀。

## 未验证与后续

- 缓存 SDK 边界仍未拆分。Tenancy 与 Settings 当前仍直接消费 `HybridCache` / `IFusionCache` 及具体 Options；该变更需要单独定义性能契约，并保持标签失效与故障回退语义。
- 本门禁只约束编译期项目引用，不替代源码级模块所有权、运行时依赖或缓存行为测试。

## 新鲜验证

- 聚焦 Architecture：`BuildingBlockProjectDependencyBoundaryTests`，`2/2` 通过。
- API Native AOT Architecture selection：`73/73` 通过。
- `pnpm test:aot:analyzers`：构建成功，`0` 警告、`0` 错误。
- `pnpm test:inner -- --snapshot caching-abstraction-boundary`：影响选择器判定无 Integration 目标。
- `pnpm test:governance`：`52/52` 通过。
