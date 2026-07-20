// 共享单实例上并行跑全量 DbUp 会互相抢锁/超时；Worker 取保守值，优先稳定而非极限吞吐。
// 保持方法级并行；每测独立数据库。
[assembly: Parallelize(Workers = 2, Scope = ExecutionScope.MethodLevel)]
