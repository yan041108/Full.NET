using System.Diagnostics.Metrics;
using System.Globalization;

namespace Full.NET.Hosting.Observability;

/// <summary>拥有单宿主目录的互斥锁与磁盘计费；不清理目录外或非自有文件。</summary>
internal sealed partial class LocalFileLogStore : IDisposable
{
    private readonly object _gate = new();
    private readonly LocalFileLoggingOptions _options;
    private readonly TimeProvider _time;
    private readonly FileStream _lease;
    private readonly Dictionary<string, ManagedFile> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly SortedSet<ManagedFile> _closed = new(ManagedFileComparer.Instance);
    private readonly SortedSet<ManagedFile> _closedGeneral = new(ManagedFileComparer.Instance);
    private readonly Meter _meter;
    private readonly Counter<long> _failures;
    private readonly Counter<long> _removed;
    private long _bytes;
    private long _priorityBytes;
    private int _priorityFiles;
    private long _fileOrder;
    private long _nextDiagnostic;
    private long _nextCleanup;
    private int _writers = 2;
    private int _disposed;
    private readonly int _maxManagedFiles;

    /// <summary>已规范化的应用级日志根目录（含 ContentRoot 与 application 片段）。</summary>
    public string DirectoryPath { get; }
    /// <summary>普通级别通道 Writer；与 <see cref="Priority"/> 共享目录租约。</summary>
    public LocalFileLogWriter General { get; }
    /// <summary>Error 及以上通道 Writer；与 <see cref="General"/> 独立滚动文件。</summary>
    public LocalFileLogWriter Priority { get; }

    /// <summary>
    /// 创建目录租约、枚举既有自有文件并启动双 Writer；构造失败会释放已占用资源。
    /// </summary>
    public LocalFileLogStore(LocalFileLoggingOptions options, string contentRoot, string application,
        TimeProvider? timeProvider = null, int maxManagedFiles = 10_000)
    {
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
        ArgumentOutOfRangeException.ThrowIfLessThan(maxManagedFiles, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxManagedFiles, 10_000);
        _maxManagedFiles = maxManagedFiles;
        // 宿主名只作为安全目录片段；原应用名仍由日志资源字段承载。
        DirectoryPath = LocalLogFileLayout.ResolveDirectory(options.Directory, contentRoot, application);
        RejectLinkedAncestors(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        RejectLinkedAncestors(DirectoryPath);
        var leasePath = Path.Combine(DirectoryPath, ".fullnet-local.lock");
        if (File.Exists(leasePath) && (File.GetAttributes(leasePath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Local log lease must not be a link.");
        _lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _meter = new Meter(FullNetAsyncLogMonitor.MeterName);
        _failures = _meter.CreateCounter<long>("fullnet.logging.local_file.failures");
        _removed = _meter.CreateCounter<long>("fullnet.logging.local_file.files_removed");
        _meter.CreateObservableGauge<long>("fullnet.logging.local_file.bytes", () => Interlocked.Read(ref _bytes));
        try
        {
            // 读取范围有界；只有本出口严格命名的非链接文件计费和清理。
            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "fullnet-*.txt"))
            {
                if (!Owns(path)) continue;
                if (_files.Count >= _maxManagedFiles) throw new IOException("Too many managed local log files.");
                var length = new FileInfo(path).Length;
                var match = LocalLogFileLayout.OwnedName().Match(Path.GetFileName(path));
                var date = DateOnly.ParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture);
                AddFile(path, length, date, Path.GetFileName(path).StartsWith("fullnet-priority-", StringComparison.Ordinal), active: false);
                _bytes = checked(_bytes + length);
            }
            // 启动尚无并发 Writer，既有文件总数最多一万；先收敛存量，避免后台第一次写入承担全部工作。
            Cleanup(0, 2, startup: true);
            General = new LocalFileLogWriter(this, false, options, _time);
            try { Priority = new LocalFileLogWriter(this, true, options, _time); }
            catch { General.Dispose(); throw; }
        }
        catch { Dispose(); throw; }
    }

    internal FileStream Open(string channel, DateOnly date, out string path)
    {
        lock (_gate)
        {
            RejectLinkedAncestors(DirectoryPath);
            Cleanup(0, 1, priority: channel == "priority");
            path = Path.Combine(DirectoryPath, $"fullnet-{channel}-{date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.txt");
            // 首次打开两个真实文件完成写权限检查；仅供后台线程使用。
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65_536);
            AddFile(path, 0, date, channel == "priority", active: true);
            return stream;
        }
    }

    internal void Reserve(string path, int bytes)
    {
        lock (_gate)
        {
            var file = _files[path];
            Cleanup(bytes, priority: file.Priority);
            file.Length = checked(file.Length + bytes);
            if (file.Priority) _priorityBytes = checked(_priorityBytes + bytes);
            Interlocked.Add(ref _bytes, bytes);
        }
    }

    internal void Close(string path)
    {
        lock (_gate)
            if (_active.Remove(path)) AddClosed(_files[path]);
    }

    internal void WriterStopped()
    {
        if (Interlocked.Decrement(ref _writers) == 0) Dispose();
    }

    internal void Maintain()
    {
        lock (_gate) Cleanup(0);
    }

    internal void Report(string channel, Exception exception)
    {
        try { _failures.Add(1, new KeyValuePair<string, object?>("channel", channel)); }
        catch (Exception) { /* 指标监听器失败不能阻止文件清理或替代原始故障。 */ }
        var now = _time.GetUtcNow().UtcTicks;
        var next = Interlocked.Read(ref _nextDiagnostic);
        if (now >= next && Interlocked.CompareExchange(ref _nextDiagnostic, now + TimeSpan.TicksPerMinute, next) == next)
            LoggingInternalDiagnostics.Report("local_file", exception);
    }

    private void Cleanup(int requiredBytes, int requiredSlots = 0, bool priority = false, bool startup = false)
    {
        var now = _time.GetUtcNow().UtcTicks;
        if (_bytes <= _options.MaxDirectoryBytes - requiredBytes
            && _files.Count <= _maxManagedFiles - requiredSlots && now < _nextCleanup) return;
        // Maintain/Reserve 也可触发清理，不能只在打开新文件时检查目录链接。
        RejectLinkedAncestors(DirectoryPath);
        var cutoff = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).AddDays(-_options.RetentionDays);
        // 日期及日内顺序在登记时冻结；运行期不再对最多一万个路径反复解析、排序或扫描。
        // 限制的是单次 OS 删除次数，单个磁盘调用仍可能阻塞，不能将此预算描述为硬时间上限。
        var deletionBudget = startup ? _maxManagedFiles : 16;
        for (var removed = 0; removed < deletionBudget; removed++)
        {
            var candidate = _closed.Min;
            var expired = candidate is not null && candidate.Date < cutoff;
            if (!expired && Fits()) break;
            if (!expired && candidate is { Priority: true } && !priority && ProtectPriority(candidate))
                candidate = _closedGeneral.Min;
            if (candidate is null) break;
            if (!Owns(candidate.Path)) throw new IOException("Managed local log file changed ownership.");
            File.Delete(candidate.Path);
            _closed.Remove(candidate);
            _closedGeneral.Remove(candidate);
            Interlocked.Add(ref _bytes, -candidate.Length);
            if (candidate.Priority) { _priorityBytes -= candidate.Length; _priorityFiles--; }
            _files.Remove(candidate.Path);
            _removed.Add(1);
        }
        // 存在到期积压时下次 Reserve/Maintain 继续；无事件时仍由原 Writer 刷新周期推进，不新增任务或队列。
        _nextCleanup = _closed.Min is { } remaining && remaining.Date < cutoff ? now : now + TimeSpan.TicksPerMinute;
        if (!Fits())
            throw new IOException("Local log directory byte/file budget or bounded reclamation allowance is exhausted.");

        bool Fits() => _bytes <= _options.MaxDirectoryBytes - requiredBytes
            && _files.Count <= _maxManagedFiles - requiredSlots;
    }

    private bool ProtectPriority(ManagedFile candidate)
    {
        if (_options.PriorityReservePercent == 0) return false;
        var fileTarget = Math.Max(1, _maxManagedFiles * _options.PriorityReservePercent / 100);
        var byteTarget = _options.MaxDirectoryBytes * _options.PriorityReservePercent / 100;
        // 名额上限阻止大量极小优先文件占满目录；按完整文件保住字节目标，最多产生一个文件的粒度余量。
        // 完整文件的粒度余量也不能吃掉普通通道一个完整文件的空间，否则最大合法事件会被永久拒绝。
        return _priorityFiles <= fileTarget
            && _priorityBytes <= _options.MaxDirectoryBytes - _options.FileSizeLimitBytes
            && _priorityBytes - candidate.Length < byteTarget;
    }

    private void AddFile(string path, long length, DateOnly date, bool priority, bool active)
    {
        var file = new ManagedFile(path, length, date, priority, ++_fileOrder);
        _files.Add(path, file);
        if (priority) { _priorityFiles++; _priorityBytes = checked(_priorityBytes + length); }
        if (active) _active.Add(path);
        else AddClosed(file);
    }

    private void AddClosed(ManagedFile file)
    {
        _closed.Add(file);
        if (!file.Priority) _closedGeneral.Add(file);
    }

    private sealed class ManagedFile(string path, long length, DateOnly date, bool priority, long order)
    {
        public string Path { get; } = path;
        public long Length { get; set; } = length;
        public DateOnly Date { get; } = date;
        public bool Priority { get; } = priority;
        public long Order { get; } = order;
    }

    private sealed class ManagedFileComparer : IComparer<ManagedFile>
    {
        public static ManagedFileComparer Instance { get; } = new();
        public int Compare(ManagedFile? left, ManagedFile? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var date = left.Date.CompareTo(right.Date);
            return date == 0 ? left.Order.CompareTo(right.Order) : date;
        }
    }

    private static bool Owns(string path)
    {
        return LocalLogFileLayout.IsOwnedFileName(Path.GetFileName(path))
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static void RejectLinkedAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Local log directories must not traverse links.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lease.Dispose();
        _meter.Dispose();
    }
}

/// <summary>单消费线程独占文件流；按 UTC 日期/大小滚动，空闲轮询也按期刷新。</summary>
internal sealed class LocalFileLogWriter : IDisposable
{
    private readonly LocalFileLogStore _store;
    private readonly LocalFileLoggingOptions _options;
    private readonly TimeProvider _time;
    private readonly string _channel;
    private FileStream? _stream;
    private string _path = "";
    private DateOnly _date;
    private long _length;
    private long _lastFlush;
    private int _disposed;

    public LocalFileLogWriter(LocalFileLogStore store, bool priority, LocalFileLoggingOptions options, TimeProvider time)
    {
        _store = store; _options = options; _time = time;
        _channel = priority ? "priority" : "general";
        Open();
    }

    public void Emit(LogEnvelope envelope)
    {
        try
        {
            var bytes = envelope.Utf8Json;
            if (bytes.Length > _options.FileSizeLimitBytes) throw new IOException("Local event exceeds file budget.");
            if (_stream is null || _date != Today() || _length > _options.FileSizeLimitBytes - bytes.Length)
            { Close(); Open(); }
            _store.Reserve(_path, bytes.Length);
            // 快照已带行尾；不再次格式化原始日志对象，也不额外增加无界缓冲。
            _stream!.Write(bytes.Span);
            _length += bytes.Length;
        }
        catch (Exception exception)
        {
            _store.Report(_channel, exception);
            try { Close(); } catch (Exception closeError) { _store.Report(_channel, closeError); }
            throw;
        }
    }

    public void FlushIfDue()
    {
        if (_time.GetTimestamp() - _lastFlush < _options.FlushInterval.TotalSeconds * _time.TimestampFrequency) return;
        try { _stream?.Flush(); _store.Maintain(); _lastFlush = _time.GetTimestamp(); }
        catch (Exception exception)
        {
            _store.Report(_channel, exception);
            try { Close(); } catch (Exception closeError) { _store.Report(_channel, closeError); }
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
    private void Open()
    {
        _date = Today();
        _stream = _store.Open(_channel, _date, out _path);
        _length = 0; _lastFlush = _time.GetTimestamp();
    }
    private void Close()
    {
        var stream = _stream; _stream = null;
        try { stream?.Dispose(); }
        finally { if (stream is not null) _store.Close(_path); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { Close(); }
        catch (Exception exception) { _store.Report(_channel, exception); }
        finally { _store.WriterStopped(); }
    }
}
