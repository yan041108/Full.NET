# 缓存 SDK 10.0.402 兼容性验收

本批用于验证既有缓存方案在 SDK 10.0.402 / Runtime 10.0.13 下的兼容性。**2026-10-15 20:46（Asia/Shanghai）验收通过，0 个本批待验收项。** 原缓存计划 14/14 的历史验收结论保持独立。

## 范围与来源

- 开工 HEAD：`f1ec6cb24853084c111e5a1ed64582389411c6b2`，分支 `main`，任务快照 `cache-sdk402-compat-20261015`。
- 原 SDK402 冻结来源：`69d8cc2230cd0ed3a76627427a11d3b565c79218dd669637174d4c4882616780`。
- 测试等待修复后来源：`868b60c20bd10df07069ce4378868c4a62318447982a3816d0da2d0c6be7474c`，8,044 项输入记录，含明确不存在的可选文件。
- 私有副本仅将 SDK 固定为 10.0.402，并将两份工具锁中的 ILLink 10.0.12 更新为 10.0.13；本批未改变仓库全局 SDK 设置。后续只修正一个集成测试方法，生产代码保持原缓存验收来源一致。
- 活动运行目录：`artifacts/cache-hardening/fullnet-cache-sdk402-reviewed-ff31c37d`。源码目录及清单、旧结果复用依据见该目录 `source-manifest.json`、`repair-reuse.json`。
- Linux SDK 镜像：`sha256:a847393cc8bfda8329d899358b7c7ab542a295691d56e2870f6ea86dab08a414`，Ubuntu 24.04 x64，容器预算 2 CPU / 12 GiB；原生运行 CLR 堆预算 2 GiB。

## 测试修复与结果复用

初次完整缓存集成实际为 **44 通过、1 失败、0 跳过**，MTP 退出 2，监督父进程退出 1。原始 TRX 保留于 `fullnet-cache-sdk402-reboot-a92b025f/integration/integration.trx`，SHA256 为 `129b9db342389151e6b8c69a3921564506885c52766978552fbcd01ae831fdfb`。

唯一失败方法为 `CacheRedisFailureRecoveryTests.Disconnected_peer_exits_old_L1_with_TTL_and_rejoins_actual_backplane`。FusionCache 2.9.0 的收信事件先于 L1 消息处理，原测试将收信当作处理完成屏障，可能立即读到旧值。顺序依据为 [v2.9.0 官方源码](https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/src/ZiggyCreatures.FusionCache/Internals/Backplane/BackplaneAccessor_Async.cs#L240)。

修复同时等待实际收信与节点读到新值，并预置 30 秒本地旧值作为阳性对照，长于 10 秒等待窗口，排除自然过期伪装订阅恢复。断连后的短 TTL 收敛和实际 Remove 广播断言均保留；未增加任意延时或修改生产实现。

修复后单一方法实际 **1/1 通过、0 跳过**，退出 0；新 TRX SHA256 为 `0af5cf545c290154c650bd2efee247eedc1dc5b901b49d25508a6b0dce16006a`。完整 45 项通过依据为原未改方法的 44 个 Passed 身份与本次 1 个 Passed 身份的精确并集，**不是同一轮 45/45**。原失败父批次继续保持失败，不生成合并伪造 TRX。

复用校验采用严格 XML 解析、唯一 UID、完整计数及实际退出码，10 项校验器检查通过。源码差异限于该方法，测试辅助逻辑与其他方法保持一致。只读复核发现并修复了旧发现日志固定摘要与新行号输出冲突；旧日志另存 `previous/`，新日志按完整 UID 集核对。

## 集中验收状态

| 范围 | 实际结果 | 证据与复用边界 |
| --- | --- | --- |
| 缓存及授权 Unit | 259/259 通过 | SDK402 实跑结果；本次仅改 Integration 方法，复用严格核对的原始 TRX |
| Architecture | 235/235 通过 | SDK402 实跑结果；复用未受影响结果 |
| API / Worker AOT 分析 | 均退出 0，零警告/错误 | `fullnet-cache-sdk402-resume-d4024f19` 的未受影响分析结果 |
| Integration 增量构建 | 退出 0，零警告/错误，1m21.35s | 私有卷仅替换目标测试文件，保留其他输入与编译产物 |
| 完整缓存 Integration | 45 个身份通过 | 原 44 项 + 新 1 项，完整发现集合重新核对 |
| API / Worker Native 发布 | 均退出 0 | 77 个资产图、两角色编译器与运行时均核对为 10.0.13；精确告警白名单门禁通过 |
| Native 核心 | 42/42 通过，零失败/跳过 | 新 SDK402 API/Worker 产物实际运行；10/10/10/10/2 五组均退出 0，完整 UID 并集核对通过 |

`node artifacts/cache-hardening/verify-sdk402-completed.mjs` 实际退出 0，凭证为活动目录下 `sdk402-completed-proof.json`。该入口独立核对完整冻结输入与单一方法差异、封存脚本、259/235 原始 TRX、缓存 44+1 身份并集、五份 Native 原始 TRX、全部阶段退出码及两套 ELF 产物摘要。历史 SDK401/400 的原生结果不计入本批 SDK402 结果。

实际监督父进程 15704 通过存活进程句柄取得退出码 0；运行终态为 `passed-and-locks-released`，新增容器为空、自然清理确认，正式锁正常释放。源码前后门禁和原始运行退出码均为 0。API 原生文件为 141,847,520 字节，Worker 为 91,347,336 字节，SHA256 与发布记录一致。收口时 Root HEAD 已由其他任务推进至 `ffa23d0f7f5819b747d8502e5f6a5d49f2ff9fb9`，本批来源仍绑定上述冻结清单。

`node artifacts/cache-hardening/check-sdk402-owned-files.mjs` 核对本次报告编码、拥有文件空白和分支/状态；`git diff --check` 按本任务路径检查。`git diff --no-index --check` 比较原/新测试文件时，退出 1 表示存在预期内容差异，同时要求标准输出、错误输出均无检查诊断，不将真实错误当作通过。

## 限制与失败记录

范围绑定上述冻结缓存来源。当前主工作区含其他任务的并行改动，本批不能作为当前 `main` 全量验收或生产容量证明；容量状态仍为 `Capacity-not-verified`。离线还原使用 `NuGetAudit=false`，未声明完成新的在线漏洞审计。

此前还原失败、父进程终态缺失、主机关机中断及缺少 Linux Node 的入口失败均保留原证据；无完整退出与 TRX 的批次不计通过。主机关机与更早的内存耗尽事件分别记录，不推断二者具有因果关系。本次发现日志冲突在排队阶段撤下等待者后修正，未启动数据库或 Native；只归档自身普通锁，未修改外来锁或停止其他任务资源。
