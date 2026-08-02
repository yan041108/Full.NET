# Admin.NET 大型插件独立执行队列

- 日期：2026-08-01
- 基线：`main` @ `4962729`
- 对应计划：[`2026-07-30-adminnet-design-absorption-program.md`](../superpowers/plans/2026-07-30-adminnet-design-absorption-program.md) Task 11
- 路线图权威条目：[`adminnet-feature-parity.md` §4.1](../roadmap/adminnet-feature-parity.md)

## 结论

吸收计划 Task 1–10 已合入 `main`。Document、Workflow、DataApproval、ImportExport/Reporting、AI/Agents 进入独立执行队列，在 Gate G4 批准并各自形成带日期 Spec 之前保持 `Mapped`。

**2026-08-02 更新：** Document 已提交 Gate G4 规格草案 [`2026-08-02-document-module-design.md`](../superpowers/specs/2026-08-02-document-module-design.md)，状态仍为 `Mapped`，待评审批准后升为 `Planned`。

本任务**未**创建：

- 空模块项目或投机性 `*.Contracts` 程序集；
- Document / Workflow / DataApproval / ImportExport / Reporting / AI 的模块规格；
- 任何运行时代码、迁移或客户端页面。

## 激活顺序与边界

1. Files Provider + 字段投影 → Document（显式 Files 契约；自有分类/标签/版本/分享/权限/日志）。
2. Notifications + Jobs 恢复 → Workflow（不可变定义版本、实例、步骤、待办、抄送、执行日志与恢复）。
3. Workflow → DataApproval（显式用例契约；禁止任意 HTTP 中间件拦截）。
4. 字段投影稳定后 → ImportExport / Reporting。
5. 权限、配额、审计基线具备后 → AI / Agents（供应商中立抽象 + 显式 Tool 权限/审计）。

每项退出门禁与官方纵向切片一致：SQL Server/MySQL 迁移与恢复、标准 API、权限、租户/数据范围、适用的 Outbox、Vue/Layui、E2E、运维文档与许可证据。

## 验证

- 文档任务：已核对路线图大型模块行仍为 `Mapped`，且仓库中不存在 Document/Workflow/DataApproval/AI 空项目骨架。
- 未运行 Integration（无服务端行为变更）。
- 规则/Skill 演进：未触发。
