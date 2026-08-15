# Cursor 多任务审查与纠正记录（2026-08-08）

## 范围

- 基线：`586bedd1e78d194b993c78b21fbdcabec2415bab`
- 审查提交：`3f45047c..586bedd1`
- 重点：架构缺口 Tasks 1–7 的代码、验证声明、模块依赖、Files Claim 状态机、能力矩阵和后续执行队列。

## 发现与纠正

| 级别 | 发现 | 纠正 |
|---|---|---|
| P0 | Files 删除在事务外查询 open claim，Claim 与删除可发生检查—写入竞态；原验证记录还把该偏离写成完成条件。 | 删除保护移入 Files 本地事务；SoftDelete 增加 open-claim 条件，Claim 改为只从仍 Ready/未删除文件条件插入，竞争失败返回稳定错误。 |
| P0 | 已 Released 的同 payload 幂等键会再次返回成功；调用方可能提交新引用但 claim 保持 Released。 | Released 作为终态失败关闭；新增回归测试。 |
| P1 | 合并修复把任意“反向模块依赖”作为 Contracts 引用豁免，可能静默放过新的双向契约环。 | 通用豁免收紧为 Identity→Organization 唯一精确债务，并附原因与移除任务；任意反向依赖负向 fixture 已锁定。 |
| P1 | 本地事务扫描器把 `Full.NET.Modules.Files.Contracts` 解析为模块 `files.contracts`，迫使同模块调用看起来像跨模块债务。 | 模块目录名统一去除 `.Contracts` 后缀；Files/Identity 契约所有者 fixture 与完整事务目录门禁通过。 |
| P1 | 能力矩阵仍列出已完成的 5 个事务债务、2 个缓存 allowlist、Document claim 和 Vue 契约门禁，可能让 Cursor 重复开发。 | 能力状态与优先级按当前实现和剩余风险更新。 |
| P1 | Identity 机构投影验证只证明版本写入和测试内回填；原计划要求的断点、dry-run、差异对账及可运行入口尚未交付。 | Task 3 已交付 reconcile 端点与 keyset/dry-run/apply；见 [`cursor-post-review-follow-up`](../superpowers/plans/2026-08-08-cursor-post-review-follow-up.md)。 |

## RED→GREEN 证据

- RED：`Claim_rejects_reuse_after_matching_claim_was_released`、`Claim_fails_when_file_becomes_unavailable_before_pending_insert`、`Delete_checks_open_claims_inside_files_transaction` 共 3 项按预期失败。
- RED：`Reverse_module_dependency_does_not_implicitly_authorize_a_contract_cycle` 按预期失败。
- GREEN：Files 聚焦 Unit 5/5；修复后完整 Unit 1147/1147、Architecture 93/93。
- GREEN：`pnpm test:integration:affected -- --base 586bedd1e78d194b993c78b21fbdcabec2415bab --phase slice` 命中 Files，SQL Server/MySQL 聚焦 2/2 通过。

## 剩余边界

当前修复使用事务内双检查与条件 DML 关闭已确认漏洞，但尚缺两套数据库上“Claim/删除同时起跑”的高竞争测试和显式统一行锁顺序（仍为 P0 队列项）。Identity→Organization 反向契约债务已于 2026-08-08 后续计划 Task 2 退役：Organization 实现 `Identity.Contracts` 中的 consumer-owned Port，Architecture 反向契约目录为空。后续执行清单见 [`2026-08-08-cursor-post-review-follow-up.md`](../superpowers/plans/2026-08-08-cursor-post-review-follow-up.md)。
