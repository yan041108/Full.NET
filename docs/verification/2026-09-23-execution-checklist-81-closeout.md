# 执行清单 81 — uni-app 首个平台审批/消息链路 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **81**；**选定平台：微信小程序**（`build:mp-weixin`、`mp-weixin-identity-session` / `mp-weixin-application-session`）；本槽页面级验收以 **H5 同源运行时**代理（`isBusinessRuntimeAvailable` 对 H5/MP-WEIXIN 为 true）；**不**在本槽承诺支付宝小程序发布证据或真机微信审核。

## 选定切片

| 项 | 值 |
|----|-----|
| 客户端 | `clients/uniapp`（Vue 3 + uni-ui） |
| 登录 | H5：Refresh Cookie + 闭包内 Access Token；微信：`createMpWeixinIdentitySession`（无 refresh 恢复） |
| 审批 | 分包 `pages/workflow`：`todos` / `todo-detail`；`workflow-todo-client`；权限 `workflow.todos.*`；幂等 `createIdempotencyKey` |
| 消息 | 分包 `pages/notifications`：`inbox` / `inbox-detail`；`inbox-messages-client`；`notifications.inbox.*` |
| 契约测试 | `workflow-pages-contract.test.ts`；`inbox-pages-contract.test.ts`；`mp-weixin-identity-session.test.ts` |
| 真实栈（可选） | `tests/e2e/uniapp-h5/tests-real/workflow-approval.spec.mjs`（需 Host + H5 dev） |
| 本槽 E2E | `tests/e2e/uniapp-h5/tests/phase-c-81-workflow-inbox-h5.spec.mjs`（API mock，登录→待办→站内信） |

## 交付锚点

| 层 | 位置 |
|----|------|
| 会话 | `application-session.ts`（条件编译 H5 / MP-WEIXIN） |
| 工作流 UI | `pages/workflow/todos.vue`（`data-testid="workflow-open-inbox"`）；`todo-detail.vue` |
| 站内信 UI | `pages/notifications/inbox.vue` |
| 构建 | `pnpm --filter @fullnet/uniapp build:mp-weixin` |
| 单元/契约 | `@fullnet/uniapp` vitest（workflow/inbox/mp-weixin 相关） |

## 清单 81 验收结论

- **已有**：移动待办列表/详情审批驳回、站内信列表与权限门；与 Host Workflow/Notifications API 契约对齐。
- **本槽**：closeout + H5 mock E2E；C 区证据登记；**未**跑 `build:mp-weixin` / 微信开发者工具真机。
- **未验**：支付宝 `mp-alipay` 专有能力、App Store 发布；Flutter 见 **82** closeout（Phase C 收官）。

## 停止边界

- **82**：Flutter 首个明确业务场景客户端。
- 禁止在 uni-app 复制完整 `ui/admin` 后台；禁止 Token 写入 `setStorageSync`。

## 本机验证

| 命令 | 结果 |
|------|------|
| `pnpm exec vitest run` `workflow-pages-contract` `inbox-pages-contract` `mp-weixin-identity-session` | **8/8**（`d40de4e6`） |
| `pnpm --filter @fullnet/uniapp-h5-e2e test` …`phase-c-81` | **未在本机关闭**（需 H5 dev server） |
| `pnpm --filter @fullnet/uniapp build:mp-weixin` | **未在本机关闭**（清单 81 平台构建门禁） |

**状态**：Build-verified（契约 + 源码审计）；升 **Verified** 受 Gate0、可选 real-stack 绿与 Gate C 约束。
