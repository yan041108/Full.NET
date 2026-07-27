# Integration 分片 UID 唯一性验证记录

- 日期：2026-07-27（Asia/Shanghai）
- 初始基线：`main@b837747e0d2a301747947a229468725690562826`
- 最终同步基线：`main@2abe451d4e7dd18145ed511f4b3f1238c1b03601`
- 原分支：`codex/integration-partition-uid-validation`
- 状态：实现与最终门禁已完成，已 fast-forward 合入 `main`；分支、worktree 注册和物理目录已清理

## 问题与边界

`verifyPartitionSets` 原先直接把全量发现结果的 UID 转成 `Set`。如果测试发现器异常返回两个相同 UID，
重复项会在集合构造时被静默折叠；只要分片包含该 UID，后续遗漏检查就可能错误地报告集合完整。

本切片只强化 Integration 分片发现的静态校验：

- 全量发现结果中的 UID 必须唯一；
- 既有跨分片重复、遗漏和额外测试检查保持不变；
- 不改变分片过滤器、canonical 测试数量、测试执行方式或 Docker 生命周期。

## RED / GREEN

| 阶段 | 证据 |
| --- | --- |
| RED | 在既有“分片集合拒绝重复和遗漏”测试中加入全量重复 UID 场景，命令返回 `Missing expected exception`，证明重复项被静默接受 |
| GREEN | 构造全量 UID 集合时先检查重复项；同一 Node 测试文件 **4/4** 通过 |

## 验证

| 门禁 | 当前结果 |
| --- | --- |
| Release Integration 测试程序集构建 | 0 warning / 0 error |
| Integration tooling | **4/4**，失败 0、跳过 0 |
| Integration 分片发现 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189**，无遗漏、额外或重复 UID |
| .NET canonical | 保持 **408/7/49/189** |
| 完整 Integration | 继承最终同步基线的新鲜结果 **189/189**，失败 0、跳过 0、stderr 0、**30m07s** |
| Governance | **11/11**，失败 0、跳过 0 |
| Skill 合同 | **52** 项通过 |
| workspace | 通过 |
| `git diff --check` | 通过 |

本切片不运行数据库用例：改动仅位于 Node 分片集合校验器及其测试，不改变任何 .NET、SQL、
容器或真实栈行为。完整 Integration 已由紧邻前序 Tenancy 切片在同一最终基线上执行，本切片在该基线生成的
Release 测试程序集上重新执行了完整分片发现门禁。

## 规则与 Skills 复盘

- 规则：缺口属于既有自动化门禁内部的集合唯一性遗漏，已由可失败回归断言直接固化；没有形成新的跨模块
  约束、重复遗漏或高风险事故证据，本次不新增或修改规则。
- Skills：修复过程是单一 Node 校验函数的测试先行闭环，没有形成三处以上需要工程判断的复用流程，也未暴露
  `fullnet-module-delivery` 缺口，本次不演进项目 Skill。
