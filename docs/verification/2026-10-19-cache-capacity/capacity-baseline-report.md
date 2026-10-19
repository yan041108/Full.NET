# 缓存本地容量基线

状态：实验完成，**Capacity-not-verified**。容量实验输入 `e746155bbae5892577dd450038a058d1201653deee2aac2dceaa98bd8de27d48`，Native API SHA-256 `73c8a7c921bda1ef722543a555dd16c925831983c1cbe069862569fba4218f2b`。测试消费者正常编译当前 Main，仅显式链接容量生命周期驱动，输入指纹 `2c075f275f1f668caf105dd5017beb3a5b57e883ba77fab43572392ca85b8864`。正式样本 28 个，实测请求 1629173 次，全部样本均为本批实跑。生命周期 2/2 通过只代表驱动与排空完成，压力预算分别见下表。两个实验实例的限流均为每分钟 1,000,000 次，认证、会话及租户校验保留，生产默认额度未修改。

连接预算：每 API 池上限 40，普通许可 37、健康预留 2、独立授权预留 1；两个 API 池合计 80。声明未运行 Worker 的 10 个池连接和运维预留 20，总预算 110，声明服务端上限 151，实际 MySQL 上限另经快照核对。实验队列 64、等待 250ms；生产 Helm API 默认队列为零，不能声称生产配置等价。预算 SHA-256：`f73f8cfc72e7fbbf1973c52800c8dfaff0bb19e32467cfb0ee0de4e216b0cf28`。

| 上下文 | 样本 | 来源 | 请求数 | 成功 RPS | 429 | P50 ms | P95 ms | P99 ms | Max ms | 异步在途值之和最大值 | 错误率 | 本地预算 |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| SqlServer-C0 | control-16-1 | 本批实跑 | 90065 | 1500.03 | 0 | 7.13 | 47.72 | 55.06 | 137.27 | 20.00 | 0.00% | within-local-budget |
| SqlServer-C0 | control-16-2 | 本批实跑 | 93857 | 1563.65 | 0 | 7.02 | 45.91 | 54.19 | 72.60 | 24.00 | 0.00% | within-local-budget |
| SqlServer-S2 | closed-1 | 本批实跑 | 10267 | 171.13 | 0 | 2.86 | 45.26 | 49.58 | 56.76 | 2.00 | 0.00% | within-local-budget |
| SqlServer-S2 | closed-16 | 本批实跑 | 98911 | 1647.33 | 0 | 5.80 | 47.84 | 53.81 | 103.55 | 17.00 | 0.00% | within-local-budget |
| SqlServer-S2 | closed-64 | 本批实跑 | 124997 | 2081.63 | 0 | 22.36 | 71.21 | 94.07 | 266.32 | 84.00 | 0.00% | within-local-budget |
| SqlServer-S2 | closed-128 | 本批实跑 | 130706 | 2169.61 | 0 | 57.75 | 109.41 | 157.24 | 221.56 | 147.00 | 0.31% | within-local-budget |
| SqlServer-S2 | closed-128-repeat | 本批实跑 | 124757 | 2074.06 | 0 | 59.14 | 124.52 | 165.36 | 262.09 | 151.00 | 0.14% | within-local-budget |
| SqlServer-S2 | open-50 | 本批实跑 | 3001 | 49.99 | 0 | 4.53 | 51.49 | 52.59 | 55.69 | 3.00 | 0.00% | within-local-budget |
| SqlServer-S2 | open-200 | 本批实跑 | 12001 | 200.00 | 0 | 3.08 | 45.68 | 50.04 | 75.75 | 2.00 | 0.00% | within-local-budget |
| SqlServer-S2 | open-500 | 本批实跑 | 30001 | 499.94 | 0 | 3.23 | 43.13 | 49.34 | 56.82 | 10.00 | 0.00% | within-local-budget |
| SqlServer-S2 | open-1000 | 本批实跑 | 60001 | 999.96 | 0 | 4.24 | 46.25 | 52.08 | 88.79 | 12.00 | 0.00% | within-local-budget |
| SqlServer-S2 | open-1000-repeat | 本批实跑 | 60001 | 999.44 | 0 | 4.36 | 46.45 | 52.07 | 72.76 | 15.00 | 0.00% | within-local-budget |
| SqlServer-S2 | mixed-writes | 本批实跑 | 66581 | 1478.55 | 0 | 5.40 | 49.71 | 53.69 | 71.19 | 17.00 | 0.00% | within-local-budget |
| SqlServer-S2 | redis-delay | 本批实跑 | 64294 | 1427.50 | 0 | 5.46 | 49.69 | 53.80 | 3005.35 | 19.00 | 0.00% | within-local-budget |
| MySql-C0 | control-16-1 | 本批实跑 | 68989 | 1149.70 | 0 | 12.95 | 18.86 | 22.68 | 99.99 | 28.00 | 0.00% | within-local-budget |
| MySql-C0 | control-16-2 | 本批实跑 | 67248 | 1120.72 | 0 | 13.29 | 19.50 | 23.31 | 38.65 | 26.00 | 0.00% | within-local-budget |
| MySql-S2 | closed-1 | 本批实跑 | 14239 | 237.31 | 0 | 3.86 | 5.72 | 6.88 | 11.81 | 2.00 | 0.00% | within-local-budget |
| MySql-S2 | closed-16 | 本批实跑 | 77052 | 1284.11 | 0 | 10.15 | 27.39 | 34.89 | 51.98 | 20.00 | 0.00% | within-local-budget |
| MySql-S2 | closed-64 | 本批实跑 | 101825 | 1696.87 | 0 | 34.70 | 63.92 | 73.56 | 159.10 | 76.00 | 0.00% | within-local-budget |
| MySql-S2 | closed-128 | 本批实跑 | 27265 | 1684.36 | 0 | 69.88 | 148.92 | 177.07 | 239.46 | 137.00 | 1.23% | budget-exceeded |
| MySql-S2 | closed-64-repeat | 本批实跑 | 101498 | 1691.24 | 0 | 33.79 | 64.33 | 74.26 | 128.08 | 109.00 | 0.00% | within-local-budget |
| MySql-S2 | open-50 | 本批实跑 | 3000 | 50.00 | 0 | 5.72 | 8.02 | 9.46 | 32.43 | 0.00 | 0.00% | within-local-budget |
| MySql-S2 | open-200 | 本批实跑 | 12001 | 200.02 | 0 | 4.13 | 6.31 | 7.90 | 15.76 | 2.00 | 0.00% | within-local-budget |
| MySql-S2 | open-500 | 本批实跑 | 30001 | 500.03 | 0 | 4.96 | 7.85 | 16.26 | 60.63 | 4.00 | 0.00% | within-local-budget |
| MySql-S2 | open-1000 | 本批实跑 | 17019 | 897.77 | 0 | 7.44 | 295.31 | 479.52 | 1246.34 | 10.00 | 5.10% | budget-exceeded |
| MySql-S2 | open-500-repeat | 本批实跑 | 30001 | 500.02 | 0 | 4.83 | 7.33 | 10.98 | 56.53 | 3.00 | 0.00% | within-local-budget |
| MySql-S2 | mixed-writes | 本批实跑 | 57932 | 1287.32 | 0 | 10.48 | 24.14 | 32.95 | 48.87 | 27.00 | 0.00% | within-local-budget |
| MySql-S2 | redis-delay | 本批实跑 | 51663 | 1147.22 | 0 | 10.29 | 32.40 | 38.68 | 2123.24 | 22.00 | 0.00% | within-local-budget |

- One authenticated hot key, two Native APIs, Worker=0; no multi-tenant working-set or L1 memory slope measurement
- Shared host and generator CPU budget; open-loop drops may reflect generator or service limits
- Maximum sum of separately exported API gauges; node timestamps are asynchronous; not a simultaneous global peak or a guaranteed lower bound, and never VUs
- C0/S2 configure the same binary; both use isolated 1,000,000/min/node quota; production default quota unchanged; no historical-version improvement claim
- SQL totals include authorization, auditing and control calls; peer timing includes PUT and polling
- Redis command delay is not a disconnect or lost-message test; no 2K/5K/10K/Soak certification

原始 OTLP、k6、SQL/Redis、容器和主机资源证据在各样本目录。缺失的可选指标见每个 result.json 的 unavailableOptionalMetrics，不能按零解释。预算超限的原始退出99被保留，不算成功样本。
