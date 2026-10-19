using System.Diagnostics;
using System.Diagnostics.Metrics;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper;

/// <summary>数据库准入获取结果；Rejected/Timeout 映射为容量异常而非数据库错误。</summary>
internal enum DatabaseConnectionAcquireOutcome
{
    Success,
    Timeout,
    Canceled,
    Rejected,
    Failure,
}

/// <summary>真实连接生命周期阶段；持有时间包含业务使用与释放，不等同于 SQL 查询时间。</summary>
internal enum DatabaseConnectionPhase { Admission, Open, Hold }

/// <summary>调用方选择的准入模式；不表示实际取得了某个预留池的许可。</summary>
internal enum DatabaseConnectionAccessMode { Normal, Critical, Authorization }

/// <summary>连接准入等待、持有与排队深度的 OpenTelemetry 指标。</summary>
internal sealed class DatabaseConnectionTelemetry : IDisposable
{
    internal const string MeterName = "fullnet.data.connection_pool";

    private readonly string _provider;
    private readonly string _hostRole;
    private readonly Meter _meter = new(MeterName);
    private readonly Histogram<double> _wait;
    private readonly Histogram<double> _hold;
    private readonly Histogram<double> _phaseDuration;
    private readonly Counter<long> _acquisitions;
    private long _inUse;
    private long _queued;

    public DatabaseConnectionTelemetry(
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<DatabaseCapacityOptions> capacityOptions)
    {
        _provider = GetProviderName(databaseOptions.Value.Provider);
        _hostRole = GetHostRoleName(capacityOptions.Value.HostRole);
        _wait = _meter.CreateHistogram<double>(
            "fullnet.db.connection.wait",
            unit: "s",
            description: "进入数据库准入边界到连接获取完成或失败的等待秒数。");
        _hold = _meter.CreateHistogram<double>(
            "fullnet.db.connection.hold",
            unit: "s",
            description: "数据库连接成功打开到实际释放的持有秒数。");
        _phaseDuration = _meter.CreateHistogram<double>(
            "fullnet.db.connection.phase.duration",
            unit: "s",
            description: "按调用方准入模式记录的准入、连接打开与持有阶段耗时，包含失败和取消的实际尝试。");
        _acquisitions = _meter.CreateCounter<long>(
            "fullnet.db.connection.acquire",
            unit: "{acquisition}",
            description: "数据库连接获取结果计数。");
        _meter.CreateObservableGauge(
            "fullnet.db.connection.admission.in_use",
            ObserveInUse,
            unit: "{permit}",
            description: "当前被数据库会话占用的普通与关键准入许可证总数。");
        _meter.CreateObservableGauge(
            "fullnet.db.connection.admission.queued",
            ObserveQueued,
            unit: "{request}",
            description: "当前等待数据库准入许可证的请求数量。");
    }

    internal void RecordAcquisition(
        DatabaseConnectionAcquireOutcome outcome,
        TimeSpan elapsed)
    {
        var tags = CreateTags(GetOutcomeName(outcome));
        _wait.Record(elapsed.TotalSeconds, tags);
        _acquisitions.Add(1, tags);
    }

    internal void RecordHold(TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { "provider", _provider },
            { "host_role", _hostRole },
        };
        _hold.Record(elapsed.TotalSeconds, tags);
    }

    /// <summary>阶段直方图不按成功/失败拆系列，首次失败不会丢失已有阶段的前置基线。</summary>
    internal void RecordPhase(DatabaseConnectionPhase phase, DatabaseConnectionAccessMode accessMode, TimeSpan elapsed)
    {
        var tags = CreateBaseTags();
        tags.Add("phase", phase switch
        {
            DatabaseConnectionPhase.Admission => "admission",
            DatabaseConnectionPhase.Open => "open",
            DatabaseConnectionPhase.Hold => "hold",
            _ => "unknown",
        });
        tags.Add("access_mode", accessMode switch
        {
            DatabaseConnectionAccessMode.Authorization => "authorization",
            DatabaseConnectionAccessMode.Critical => "critical",
            DatabaseConnectionAccessMode.Normal => "normal",
            _ => "unknown",
        });
        _phaseDuration.Record(elapsed.TotalSeconds, tags);
    }

    internal void PermitAcquired() => Interlocked.Increment(ref _inUse);

    internal void PermitReleased() => Interlocked.Decrement(ref _inUse);

    internal void WaiterQueued() => Interlocked.Increment(ref _queued);

    internal void WaiterDequeued() => Interlocked.Decrement(ref _queued);

    public void Dispose() => _meter.Dispose();

    private Measurement<long> ObserveInUse() => new(
        Interlocked.Read(ref _inUse),
        CreateBaseTags());

    private Measurement<long> ObserveQueued() => new(
        Interlocked.Read(ref _queued),
        CreateBaseTags());

    private TagList CreateTags(string outcome)
    {
        var tags = CreateBaseTags();
        tags.Add("outcome", outcome);
        return tags;
    }

    private TagList CreateBaseTags() => new()
    {
        { "provider", _provider },
        { "host_role", _hostRole },
    };

    private static string GetProviderName(DatabaseProvider provider) =>
        provider switch
        {
            DatabaseProvider.SqlServer => "sqlserver",
            DatabaseProvider.MySql => "mysql",
            _ => "unknown",
        };

    private static string GetHostRoleName(DatabaseHostRole hostRole) =>
        hostRole switch
        {
            DatabaseHostRole.Api => "api",
            DatabaseHostRole.Worker => "worker",
            DatabaseHostRole.Migrator => "migrator",
            _ => "unknown",
        };

    private static string GetOutcomeName(DatabaseConnectionAcquireOutcome outcome) =>
        outcome switch
        {
            DatabaseConnectionAcquireOutcome.Success => "success",
            DatabaseConnectionAcquireOutcome.Timeout => "timeout",
            DatabaseConnectionAcquireOutcome.Canceled => "canceled",
            DatabaseConnectionAcquireOutcome.Rejected => "rejected",
            DatabaseConnectionAcquireOutcome.Failure => "failure",
            _ => "failure",
        };
}
