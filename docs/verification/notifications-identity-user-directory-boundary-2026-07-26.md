# Notifications–Identity 用户目录边界验证记录

- 日期：2026-07-26
- 分支：`main`
- 状态：Build-verified；Notifications 双库场景通过
- 范围：Host 站内信收件人校验

## 已实现边界

1. Notifications 不再直接查询 `fn_identity_user`，其 SQL 只保留站内信自有表访问。
2. 发送服务通过已有 `IHostUserDirectory.FindActiveHostUserAsync` 校验收件人。
3. Identity 目录与旧 SQL 保持相同的有效性边界：用户必须属于 Host、`TenantId` 为空且处于启用状态。
4. 标题/正文验证、收件人不存在错误码、数据库事务、插入后回读和提交后实时推送语义均未改变。
5. 未新增公共 API、数据库对象、迁移、缓存、传输协议、项目或部署单元。
6. 跨模块表访问精确债务由 **4 条降至 3 条**；Notifications 生产源码中的 `fn_identity_user` 与旧查询标识引用均为 **0**。

## 测试先行证据

从精确债务登记删除 Notifications 条目、但尚未修改生产 SQL 时，所有权门禁按预期失败：

```text
Unregistered cross-module table access:
notifications -> fn_identity_user
@ src/Modules/Full.NET.Modules.Notifications/Persistence/InboxMessageSql.cs
```

改用 Identity 用户目录并删除旧 SQL 后，同一门禁恢复通过，完整 Architecture 套件为 **43/43**。

## 验证证据

| 门禁 | 结果 |
| --- | --- |
| Release solution build | **0 warnings / 0 errors** |
| Unit 全量 | **365/365** |
| Compatibility 全量 | **7/7** |
| Architecture 全量 | **43/43** |
| Naming | **23/23** |
| 项目 Skill 契约 | **52** 项通过 |
| Notifications SQL Server/MySQL API 场景 | **2/2**，失败 **0**，耗时 **53.728s** |
| 精确跨模块表访问债务 | **3** 条 |
| Notifications 禁止引用扫描 | `fn_identity_user` / `HostRecipientExists` 均为 **0** |

双库 API 场景覆盖成功发送、无效收件人 404 与稳定错误码、分页列表、未读计数、单条已读和全部已读。此次变更只影响该发送路径，且未改变共享合同或基础设施；因此使用新鲜的双库聚焦场景替代重复执行约 26 分钟的全量 Integration，仓库全量门槛仍保持 **172**。

## 治理复盘

- Rules：现有模块表所有权、消费者 Port、Dapper、Host 数据范围和双库验证规则完整覆盖本次问题；未发现需要升级为新规则的重复遗漏。
- Skills：`fullnet-module-delivery` 已覆盖跨模块 Port、显式 SQL、双库场景和完成前审计；本次无需新增或演进项目 Skill。
- 文档分层：实施步骤记录于 `docs/superpowers/plans/`，本文件只保存新鲜验证事实；没有新增或修改架构决策。
