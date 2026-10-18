# 企业申请详情与提交客户端批次

基线 `6159d07423324861627c3639a287111da66121e9`，任务快照 `enterprise-detail-client-20261009`，开发分支 `codex/foundation-acceptance-20261003`。本批交付申请头详情、提交确认、生成提交操作和页面中英文显示；F09/F10 整体保持 `Build-verified`。明细行、附件、通知及最终独立应用/浏览器验收仍按总计划推进。

详情通过既有生成 GET 从服务端读取，不展示列表缓存作为详情；适配器验证完整模型与单据身份，Int64 版本继续规范化为安全整数。关闭、单据切换、租户/账号/权限变更和卸载同步取消并清空资料，旧成功、错误及 finally 不影响新查询。刷新互斥，失败清空旧详情并允许重试。金额为精确十进制字符串时保持原值，未知状态保留机器值。

提交按钮先展示确认，取消不写入；写请求取消不承诺服务端回滚。继续原 Submit 精确权限，关闭后旧结果不能刷新列表、关闭新确认或结束新加载态。服务端只补响应元数据，成功为既有 `EnterpriseRequestResponse`，错误声明 400/401/403/404/409；流程未发布的 400 经复审补齐。运行时 SQL Server/MySQL 导出规范一致，manifest 增至 572 项，SDK 由规范生成，适配器使用生成 POST、守卫和取消参数，不再拼写提交路径。没有 SQL、迁移、事务或授权实现变更。

实际失败证据：首轮 Vue/API 9 失败、30 通过，确认缺失详情入口、立即提交及未本地化字段；规范快照尚无提交操作时契约失败，补充 400 后旧快照仍被回归拒绝。接入生成操作后，一项存量取消测试仍在 RequestInit 中查找信号；已改为生成 HTTP 客户端约定的第三参数，保留撤权取消与迟到结果断言。新增提交确认框导致存量测试误取首个弹窗，改为按创建标题选择。失败不计为通过。

## 包体与语义

同一 Windows 本机、Node 24.12.0、Vue Release 生产构建，不改预算或 chunk 加载策略。初次构建的首屏静态 JS **1,439,515 B** 超过既有 **1,368,052 B +5%** 门禁。复用 32 组高频文案的私有常量，替换 702 个重复字面量后，完整字典重构前后逐字节一致；另与基线逐项比对两种语言各 **4,410** 个旧键及文案均相同，各仅新增 **32** 个申请键。复审独立确认等价，插值和键类型未改变。

| 最终产物 | minified | gzip | Brotli quality 4 |
| --- | ---: | ---: | ---: |
| 首屏 67 个静态 JS chunk | 1,432,778 B | 383,276 B | 382,364 B |
| EnterpriseRequestsView 延迟 JS | 22,561 B | 5,487 B | 5,501 B |

首屏 minified 相对首次候选减少 **6,737 B**；相对仓库预算基线为 **+4.73%**，gzip 为 **+3.95%**，两项均通过。gzip 相对首次候选略增，不宣称所有压缩指标都改善；Brotli 仅记录最终值。FullNetChart 和 VForm3 独立预算同样通过。没有首屏网络瀑布、交互耗时或容量实测，不将构建尺寸外推为运行性能。

## 本地验证

| 命令与范围 | 实际结果 | 原始证据 |
| --- | --- | --- |
| `pnpm --filter @fullnet/admin test -- src/views/EnterpriseRequestsView.test.ts src/views/enterprise-requests/EnterpriseRequestDetailDialog.test.ts src/views/enterprise-requests/EnterpriseRequestApprovalProgressDialog.test.ts src/views/enterprise-requests/enterprise-requests-page.test.ts src/api/enterprise-requests.test.ts src/composables/useAuthorizedViewScope.test.ts src/i18n src/navigation/catalog.test.ts src/router/index.auth-guard.test.ts src/router/index.performance.test.ts --maxWorkers=2` | 13 文件，107/107，32.21 秒 | `.tmp/enterprise-detail-vue-expanded.log` |
| `pnpm --filter @fullnet/client-contracts test` | 62 文件，261/261 | `.tmp/enterprise-detail-contracts.log` |
| `pnpm --filter @fullnet/admin-i18n test`；`pnpm test:localization` | 8/8；7/7 | `.tmp/enterprise-detail-i18n-final.log`、`-localization.log` |
| `pnpm --filter @fullnet/admin build`；`pnpm test:bundle-budgets` | 类型与生产构建通过；三项预算通过 | `.tmp/enterprise-detail-build-budget-fixed.log`、`-bundle-fixed.log` |
| `pnpm test:openapi`；生成及离线快照 `--check`；breaking 对比本批基线 | 206/206，零漂移；94 组冻结契约兼容 | `.tmp/enterprise-detail-openapi.log`、`-breaking.log` |
| 运行时 OpenAPI 导出，两库顺序执行 | 修正后 SQL Server 1/1，52.095 秒；MySQL 1/1，105.652 秒，零跳过，规范一致 | `.tmp/enterprise-detail-snapshot-final.log` |
| `FULLNET_TESTCONTAINERS_REUSE=0 pnpm test:integration:affected -- --snapshot enterprise-detail-client-20261009 --phase slice --reuse-build` | 受影响双库申请 12/12，零失败/跳过，221.974 秒；Release 构建零警告/错误，53.03 秒 | `.tmp/enterprise-detail-integration.log` |
| `pnpm test:aot:analyzers` | 零警告/错误，分析构建 106.08 秒，默认 JIT 还原图恢复通过 | `.tmp/enterprise-detail-aot.log` |
| `pnpm test:naming`；`pnpm test:governance` | 33/33；59/59，零失败/跳过 | `.tmp/enterprise-detail-naming.log`、`-governance.log` |
| 浏览器 spec 语法与静态契约 | 语法检查退出 0，18/18；尚未实际执行浏览器 | `.tmp/enterprise-detail-e2e-structure.log` |

测试使用任务工作目录与资源锁；未删除其他任务的进程、数据库、容器或锁。最终只读复审无剩余 P1/P2。完整生成应用、真实浏览器、新提交的 Linux 原生运行及容量测试本批未执行，保持 `Capacity-not-verified`；不合并、不发布。
