// 集成测试瓶颈是每测全量 DbUp 与双库对称，而非 CPU；共享容器下提高 Worker 可缩短墙钟。
// 保持方法级并行；每测独立数据库，无需再对迁移类加 [DoNotParallelize]。
[assembly: Parallelize(Workers = 6, Scope = ExecutionScope.MethodLevel)]
