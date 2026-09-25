# 执行清单 53 — 部署期模块启用校验/预览 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **53**（`FullNet:Modules` 运行时只读分析、候选预设/显式列表 DAG 校验、管理预览；**不**运行时动态装载程序集）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `GET /api/v1/identity/modules/selection/runtime`；`POST …/validate` |
| 分析 | `IFullNetModuleSelectionPreview` / `FullNetModuleSelection` / `ModuleSelectionAnalysis`（官方 DAG、缺失依赖、未知模块） |
| 运行时边界 | `HostModuleSelectionQueryService` **不修改** DI；`DeploymentNotice` 明示需重启部署 |
| AOT/Endpoint | `ModuleSelectionEndpointTests`（未启用模块不注册 `/api/v1/{module}/*`） |
| Vue | `ModuleSelectionPreviewView`（运行时状态、预设/显式校验、问题列表） |
| 权限 | `identity.module_catalog.read`（`ModuleCatalogPermissions.Read`） |
| 契约 | `tests/openapi/identity-host-modules-contract.test.mjs`（含 selection 路径） |

## 清单 53 验收结论

- **已有**：Preset（Full/Minimal/Platform/Content）与显式 `Enabled` 列表校验；与 Composition 启动裁剪一致。
- **本槽**：`phase-c-53-module-selection-preview.spec.mjs`（runtime、Full 预设合法、Document 缺 Files 依赖 fail、页面冒烟）。
- **未做（刻意）**：在线改 `appsettings`、Plugin 热插拔、ReZero 动态 API。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`ModuleSelectionAnalysis` / `FullNetModuleSelection` | **10/10**（`d40de4e6`） |
| `dotnet test` …`ModuleSelectionEndpointTests`（Architecture） | **3/3** |
| `node --test` `identity-host-modules-contract.test.mjs` | **2/2** |
| real-stack | `phase-c-53-module-selection-preview.spec.mjs`（需本地 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
