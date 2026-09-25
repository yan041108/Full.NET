# 执行清单 31 — Files 虚拟目录与元数据 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **31**（虚拟目录树、文件元数据修订、按目录筛选、引用只读查询；修订冲突 409）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `host-folders`（tree/create/update/delete）、`host-files` update/references、`folderId` 列表筛选 |
| 服务 | `HostFolderManagementService`、`HostFileManagementService.UpdateMetadataAsync` |
| Vue | `HostFilesView.vue`（目录树、编辑元数据、引用抽屉） |
| 单元 | `HostFileMetadataUpdateTests`、`HostFolderQueryServiceTests` |

## 范围外（仍标部分）

- 批量上传/批量删除：API 已有；独立子切片可另补 E2E。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`HostFileMetadataUpdateTests` | 元数据移动与修订 |
| real-stack | `host-files.spec.mjs` 增补清单 31 |

**状态**：Build-verified（核心子切片）；升 Verified 受 Gate0 / D-83 约束。
