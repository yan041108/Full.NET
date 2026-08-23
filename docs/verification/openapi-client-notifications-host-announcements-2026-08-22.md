# OpenAPI 客户端迁移：Notifications Host Announcements 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`notifications-host-announcements`（`ui/admin/src/api/host-announcements.ts`）
- 计划：[`2026-08-22-openapi-client-notifications-host-announcements.md`](../superpowers/plans/2026-08-22-openapi-client-notifications-host-announcements.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Notifications Host Announcements 切片已通过完整 OpenAPI 客户端门禁。4 个 Operation 具备稳定 `operationId` 与主 Tag `NotificationsHostAnnouncements`；`host-announcements.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 175 条）。

下一默认项为 `inbox-messages.ts`；禁止并行批量改写。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `notificationsListHostAnnouncements` | `listHostAnnouncements` |
| `notificationsCreateHostAnnouncement` | `createHostAnnouncement` |
| `notificationsUpdateHostAnnouncement` | `updateHostAnnouncement` |
| `notificationsPublishHostAnnouncement` | `publishHostAnnouncement` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 通过 |
| `OpenApiOperationIdentityRulesTests` | 1/1，通过 |
| `npx vitest run src/api/host-announcements.test.ts` | 2/2，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
