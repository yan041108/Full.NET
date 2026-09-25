# 执行清单 38 — 行政区域 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **38**（区域树/子节点/分页查询、CRUD、版本化导入预览与应用、Vue 级联；固定种子数据、不接入未批准地图厂商）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `/api/v1/regions/administrative-regions`（children、tree、import/preview、import/apply） |
| 模块 | `Full.NET.Modules.Regions` |
| Vue | `AdministrativeRegionsView.vue`、`AdministrativeRegionCascader.vue` |
| 种子 | Baseline 中国行政区划样本（如 `110000`、`440000`） |
| 集成 | `RegionsAdministrativeRegionAssertions` |
| 单元 | `AdministrativeRegionImportDiffEngineTests` 等 |

## RG01 核验结论

- **已有**：Host 区域管理与级联组件；导入差异引擎支持 merge/replace 预览。
- **本槽**：新增 real-stack `host-administrative-regions.spec.mjs`（树查询 + CRUD + 导入预览 + Vue 创建删除 + Viewer 403）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~AdministrativeRegion`（UnitTests） | **7/7** |
| `pnpm exec vitest run` …`AdministrativeRegionsView.test.ts` | **2/2** |
| `dotnet test` …`RegionsAdministrativeRegionAssertions` | 本机 Docker 不可用则跳过 |
| real-stack | `host-administrative-regions.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
