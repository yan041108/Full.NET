using Full.NET.Abstractions.Messaging;
using Full.NET.Composition;
using Full.NET.Modules.EnterpriseRequest;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Host.Worker;

/// <summary>独立测试进程的静态应用目录；故障挂起仅存在于此测试项目。</summary>
internal static class ApplicationWorkerModuleCatalog
{
    internal static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var existing = services.Where(d => d.ServiceType == typeof(IHostedService)).ToHashSet();
        services.AddFullNetApplicationModules(configuration, FullNetHostProfile.Worker, [new EnterpriseRequestModule()]);
        // 只保留本次业务恢复直接使用的模块循环；官方入口随后照常注册生产 Outbox。
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && !existing.Contains(d)
            && d.ImplementationType?.Name is not ("WorkflowRecoveryHostedProcessor" or "NotificationDeliveryHostedProcessor")).ToArray()) services.Remove(descriptor);
        var mode = configuration["RecoveryFixture:Pause"] ?? "none";
        if (mode == "none") return;
        var file = configuration["RecoveryFixture:GateFile"] ?? throw new InvalidOperationException("缺少本测试拥有的故障检查点文件。");
        if (mode == "submission")
        {
            var original = services.Single(d => d.ServiceType == typeof(IIntegrationEventHandler) && d.ImplementationType?.Name == "EnterpriseRequestSubmittedHandler");
            services.Remove(original);
            services.Add(ServiceDescriptor.Describe(typeof(IIntegrationEventHandler), sp =>
                new SubmittedGate((IIntegrationEventHandler)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), file), original.Lifetime));
        }
        else if (mode == "outcome")
        {
            var original = services.Single(d => d.ServiceType == typeof(IWorkflowInstanceCompletedSink) && d.ImplementationType?.Name == "WorkflowInstanceCompletedEnterpriseRequestSink");
            services.Remove(original);
            services.Add(ServiceDescriptor.Describe(typeof(IWorkflowInstanceCompletedSink), sp =>
                new OutcomeGate((IWorkflowInstanceCompletedSink)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), file), original.Lifetime));
        }
        else throw new InvalidOperationException("不支持的恢复测试挂起点。");
    }

    private static async Task PauseAsync(string file, Guid messageId, CancellationToken cancellationToken)
    {
        // 完成写入后再原子显示标记，父进程不会把部分写盘当成已越过提交边界。
        await File.WriteAllTextAsync(file + ".tmp", messageId.ToString("D"), cancellationToken);
        File.Move(file + ".tmp", file);
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private sealed class SubmittedGate(IIntegrationEventHandler original, string file) : IIntegrationEventHandler
    {
        public string EventType => original.EventType;
        public IReadOnlyList<string> LegacyEventTypes => original.LegacyEventTypes;
        public int SchemaVersion => original.SchemaVersion;
        public IntegrationEventIdempotencyStrategy IdempotencyStrategy => original.IdempotencyStrategy;
        public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            await original.HandleAsync(context, payload, cancellationToken);
            await PauseAsync(file, context.MessageId, cancellationToken);
        }
        public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) => throw new NotSupportedException("恢复场景必须携带真实消息上下文。");
    }

    private sealed class OutcomeGate(IWorkflowInstanceCompletedSink original, string file) : IWorkflowInstanceCompletedSink
    {
        public async Task HandleAsync(IntegrationEventContext context, WorkflowInstanceCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            await original.HandleAsync(context, integrationEvent, cancellationToken);
            await PauseAsync(file, context.MessageId, cancellationToken);
        }
    }
}
