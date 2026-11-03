# 企业申请审批退回旅程

基线 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，任务快照 `foundation-f10-return-journey-20261103`。保留原单级审批/驳回夹具，仅为新增场景发布两级人工审批。

真实 SQL Server + Kestrel + Vue + Edge + 独立 Worker 场景 1/1 通过，零跳过/零 flaky。后台保留生产 Outbox、Workflow 恢复活动及通知循环，没有直接调用 Handler 或活动处理器：

1. 从业务页面创建并提交申请，一级审批通过。
2. 二级待办选择实际已完成的一级步骤并退回；同一真实 HTTP 请求投递两次，返回完全相同固定回执。
3. 退回期间申请仍为 Submitted，业务主从编辑保存不可用；不能把流程退回误当作业务草稿解锁。
4. Worker 激活原轮次，重新办理一级和二级，业务回写 Approved 并在消息中心出现该单据完成通知。

1440/390px 截图已复核，当前待办区域 axe 通过。首轮发现流程编号、签名说明及驳回按钮对比度不足；修正后下一轮又暴露退回按钮正常/交互态对比度不足。均使用已有设计令牌和正文颜色修复，最终完整场景通过；早期失败不计通过。集成项目定向 Release 构建零警告/错误；最终 Vue 类型检查与生产构建随紧接的报表配置增量通过，见[关联验证](2026-11-03-reporting-template-configuration.md)。

证据路径：`C:\Users\dai27\AppData\Local\Temp\fullnet-f10-return-evidence-08fc5482fa924f1ab09dcb5d5647b1c7`，含浏览器 JSON、桌面/窄屏截图；最小构建/测试日志按批复制。测试源码为 `EnterpriseRequestFoundationBrowserUiTests.Real_browser_return_round_replays_and_completes_business_with_sql_server` 和 `enterprise-request-return.spec.mjs`，使用矩阵选择器 `enterprise-request-return-browser`。

只新增业务消费者场景与复现后的局部样式修复，无执行内核、SQL 或 HTTP 契约变化。未重跑全量 Workflow 或双库内核矩阵；原内核证据按原范围复用。强杀进程、中断回写和处理中撤权仍未由本场景证明，F10-R1 总项保持开放。容量状态仍为 `Capacity-not-verified`。
