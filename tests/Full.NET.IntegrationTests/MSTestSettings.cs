// 共享单实例上并行跑全量 DbUp 会互相抢锁/超时；Worker 取保守值，优先稳定而非极限吞吐。
// 保持方法级并行；每测独立数据库。
/// <summary>集成测试程序集并行策略；限制 Worker 数以降低 DbUp 争用。</summary>
[assembly: Parallelize(Workers = 2, Scope = ExecutionScope.MethodLevel)]
