namespace Full.NET.IntegrationTests;

// 本次程序集运行创建的临时库与清理动作绑定；固定 schema 模板不属于此登记簿。
internal sealed class OwnedTestDatabases
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Func<string, Task>> _owned = new(StringComparer.Ordinal);
    private bool _closed;

    internal async Task<string> CreateAsync(Func<string, Task> create, Func<string, Task> drop,
        Func<string, Task>? configure = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (_closed) throw new InvalidOperationException("测试库登记已关闭。");
            var name = "fullnet_it_" + Guid.NewGuid().ToString("N");
            // 仅成功建库才取得所有权；建库失败不能据库名前缀接管服务器上的同名库。
            await create(name);
            _owned.Add(name, drop);
            // 授权等后置步骤失败仍须保留已建库的所有权；整段初始化与清理互斥。
            if (configure is not null) await configure(name);
            return name;
        }
        finally { _gate.Release(); }
    }

    internal async Task RetireAsync(params Func<Task>[] removeContainers)
    {
        await _gate.WaitAsync();
        try
        {
            // 等待在途建库与配置后关闭登记，私有容器移除期间不能接收新的所有权。
            _closed = true;
            var failures = new List<Exception>();
            foreach (var remove in removeContainers)
            {
                try { await remove(); }
                catch (Exception error) { failures.Add(error); }
            }
            // 删除私有容器已删除其中全部库；任一容器失败则保留登记供诊断或重试。
            if (failures.Count > 0) throw new AggregateException("自有测试容器清理失败。", failures);
            _owned.Clear();
        }
        finally { _gate.Release(); }
    }

    internal async Task CleanupAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _closed = true;
            var failures = new List<Exception>();
            foreach (var (name, drop) in _owned.ToArray())
            {
                try
                {
                    // 清理动作捕获建库时的 Provider 和服务器连接，不重新解析可变环境。
                    await drop(name);
                    _owned.Remove(name);
                }
                catch (Exception error) { failures.Add(error); }
            }
            // 失败项保留以允许重试，成功项不再重复执行；一个故障不能阻断其他库的释放。
            if (failures.Count > 0) throw new AggregateException("自有测试库清理失败。", failures);
        }
        finally { _gate.Release(); }
    }
}
