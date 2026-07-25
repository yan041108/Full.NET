namespace Full.NET.Modules.Jobs.Execution;

/// <summary>可执行的任务处理器；由 Jobs 模块在 DI 中注册。</summary>
public interface IJobHandler
{
    string JobKey { get; }

    Task ExecuteAsync(CancellationToken cancellationToken);
}
