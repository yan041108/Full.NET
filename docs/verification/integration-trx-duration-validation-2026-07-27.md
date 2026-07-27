# Integration TRX 耗时完整性验证记录

- 日期：2026-07-27（Asia/Shanghai）
- 初始基线：`main@4dcea9cb844bbbaa6a28fd6fc0cdb2f2328d3a06`
- 分支：`codex/integration-trx-duration-validation`
- 状态：实现与分支门禁已完成，等待前序任务合入后同步最终 `main`

## 问题与边界

TRX 分析器原先把缺失或格式非法的 `duration` 静默转换为 `0ms`。这种降级会让慢测试汇总低报耗时，
并让不完整的性能证据继续通过自动化门禁。

本切片只强化 Integration TRX 静态分析：

- 每个 `UnitTestResult` 必须提供合法的 `hh:mm:ss[.fraction]` 耗时；
- 分钟和秒必须位于 `0..59`；
- 缺失、格式错误或越界时立即报告具体测试名称；
- 不改变 TRX 聚合、排序、分片发现、.NET canonical 数量或 Docker 生命周期。

## RED / GREEN

| 阶段 | 证据 |
| --- | --- |
| RED | 在既有“TRX 报告按耗时降序输出最慢测试”方法中加入缺失和非法 `duration` 场景；命令返回 **3/4**，失败为 `Missing expected exception`，证明无效耗时被静默接受 |
| GREEN | 无效耗时改为显式失败，并覆盖缺失、非时间格式及 `00:60:00` 越界场景；同一 Node 测试文件 **4/4** 通过 |

## 验证

| 门禁 | 当前结果 |
| --- | --- |
| Integration tooling | **4/4**，失败 0、跳过 0 |
| Governance | **11/11**，失败 0、跳过 0 |
| Skill 合同 | **52** 项通过 |
| workspace | 通过 |
| `git diff --check` | 通过 |

本切片不运行数据库用例：改动仅位于 Node TRX 分析器及其测试，不改变 .NET、SQL、容器或真实栈行为。

## 规则与 Skills 复盘

- 规则：缺口属于既有测试证据分析器内部的输入完整性遗漏，已由可失败回归断言直接固化；没有形成新的跨模块约束、
  重复遗漏或高风险事故证据，本次不新增或修改规则。
- Skills：修复过程是单一 Node 校验函数的测试先行闭环，没有形成三处以上需要工程判断的复用流程，也未暴露
  `fullnet-module-delivery` 缺口，本次不演进项目 Skill。
