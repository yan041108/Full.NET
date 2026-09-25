# 执行清单 63 — Document 访问日志/统计页签 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **63**（`DocumentStatisticsView` **统计** + **访问日志** 页签；首批可验证 **汇总指标集** `HostDocumentStatisticsResponse.summary` 及按扩展名分布；**不含**热门标签/批量分享等 DC03 另拆能力）。

## 选定指标集（首批）

| 指标 | 来源 |
|------|------|
| `totalItems` / `totalVersions` / `totalSizeKb` | `HostDocumentStatisticsQueryService` 聚合 |
| `shareCount` / `recycleBinCount` | 分享与回收站计数 |
| `todayCreatedCount` / `todayAccessCount` / `todayDownloadCount` | 当日 UTC 对齐计数 |
| `byType[]` | 扩展名分布表（页签「统计」） |
| 访问日志 | `GET /api/v1/document/host/access-logs` 分页 + 筛选 |

## 交付锚点

| 层 | 位置 |
|----|------|
| 统计 API | `GET /api/v1/document/host/statistics` |
| 日志 API | `GET /api/v1/document/host/access-logs` |
| 写入 | `DocumentAccessLogRecorder`（预览/下载/分享访问等） |
| 权限 | `document.host_statistics.read`；`document.host_access_logs.read` |
| Vue | `DocumentStatisticsView`（`document-statistics-tabs` / `document-access-logs-panel`） |
| 契约 | `document-host-statistics-v1.json`；`document-host-access-logs-v1.json` |
| 集成 | `DocumentAdminNetParityAssertions.VerifyStatisticsAsync`；`DocumentHostItemAssertions` 预览后日志 |

## 与 62 的分工

| 编号 | 同页不同槽 |
|------|------------|
| **62** | 「版本保留」策略与历史版本删除 |
| **63** | 「统计」汇总 + 「访问日志」只读运维 |

## 清单 63 验收结论

- **已有**：预览失败（422）仍记 `preview` 访问日志（集成断言）；统计 Host-only 授权。
- **本槽**：`phase-c-63-document-statistics-access-logs.spec.mjs`；`document-real-stack.mjs` 增补 access-logs/preview 辅助。
- **未验**：按分类图表扩展、热门标签筛选、批量分享统计（另编号）。

## 停止边界

- DC03 热门/推荐标签、批量分享列表筛选 **不** 并入本槽。
- 不与 Auditing 模块 `auditing/access-logs` 混淆（身份访问审计 vs 文档业务访问）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `node --test` `document-host-statistics-contract.test.mjs` + `document-host-access-logs-contract.test.mjs` | **2/2** |
| `dotnet test` …`DocumentAuthorizationContributor` | **1/1**（`d40de4e6`） |
| real-stack | `phase-c-63-document-statistics-access-logs.spec.mjs`（可叠加 `document-statistics.spec.mjs`） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
