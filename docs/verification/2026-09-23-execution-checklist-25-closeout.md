# 执行清单 25 — 枚举转字典 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **25**（静态枚举目录预览、确认写入 Host 字典；幂等、不覆盖人工标签）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `GET …/enum-catalogs/{key}/dict-generation-preview`、`POST …/dict-generation` |
| 规划 | `HostEnumCatalogDictGenerationPlanner`、`HostEnumCatalogDictGenerationService` |
| 权限 | `settings.enums.read`、`settings.enums.generate_dict` |
| Vue | `EnumCatalogsView.vue`（`data-testid` 生成/确认字典） |
| 契约 | `enum-catalogs.ts`、`EnumCatalogContracts` |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`HostEnumCatalogDictGenerationPlannerTests` | 规划/冲突/跳过逻辑 |
| real-stack | `host-enum-catalogs.spec.mjs` 增补清单 25 API 幂等 + Vue 预览对话框 |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
