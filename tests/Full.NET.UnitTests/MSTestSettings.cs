/// <summary>单元测试程序集并行策略；方法级并行，类内顺序由测试自行保证。</summary>
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
