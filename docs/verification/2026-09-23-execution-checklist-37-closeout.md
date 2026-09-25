# 执行清单 37 — 更新日志 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **37**（Host 发布/编辑/撤回；用户已读与最新未读；版本排序与已读幂等；不含在线部署器）。

## 交付锚点

| 层 | 位置 |
|----|------|
| Host API | `/api/v1/platform/host-release-notes`（CRUD、publish、retract、delete） |
| 用户 API | `/api/v1/platform/my-release-notes`（列表、`latest-unread`、`/{id}/read`） |
| 模块 | `Full.NET.Modules.Platform`（`ManageHostReleaseNotes` / `ManageMyReleaseNotes`） |
| Vue | `HostReleaseNotesView.vue`、`MyReleaseNotesView.vue`、`ReleaseNoteUnreadPrompt.vue` |
| 迁移 | `134_PlatformReleaseNote`、`135_PlatformReleaseNoteActionPermissions`（双库） |
| 集成 | `PlatformReleaseNoteAssertions` |
| 单元 | `HostReleaseNoteManagementServiceTests`、`ReleaseNoteVersionSortKeyTests` |

## UP01 核验结论

- **已有**：与 Admin.NET「更新日志」能力对齐的专用 API 与管理页；撤回后用户侧列表不可见（集成断言已覆盖）。
- **本槽**：新增 real-stack `host-release-notes.spec.mjs`（发布→未读→已读幂等→撤回；Vue 创建发布撤回；Viewer 403）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `pnpm exec vitest run` …`HostReleaseNotesView.test.ts` + `host-release-notes.test.ts` | **4/4** |
| `dotnet test` …`FullyQualifiedName~ReleaseNote`（UnitTests） | **8/8** |
| `dotnet test` …`PlatformReleaseNoteAssertions` | 本机 Docker 不可用则跳过 |
| real-stack | `host-release-notes.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
