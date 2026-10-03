namespace Full.NET.Hosting.Observability;

/// <summary>HTTP 投影独立的每秒事件与字节预算；不与普通日志队列共用。</summary>
internal sealed class HttpLogCaptureBudget
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private BudgetWindow _window = new();
    private long _windowStartedAt;
    private bool _windowStarted;

    public HttpLogCaptureBudget(TimeProvider timeProvider) =>
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public bool TryReserve(
        int eventMaxBytes,
        int eventsPerSecond,
        int bytesPerSecond,
        out Reservation? reservation)
    {
        reservation = null;
        if (eventMaxBytes <= 0
            || eventsPerSecond <= 0
            || bytesPerSecond < eventMaxBytes)
        {
            return false;
        }

        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            if (!_windowStarted
                || _timeProvider.GetElapsedTime(_windowStartedAt, now)
                    >= TimeSpan.FromSeconds(1))
            {
                _window = new BudgetWindow();
                _windowStartedAt = now;
                _windowStarted = true;
            }

            if (_window.Events >= eventsPerSecond
                || _window.Bytes > bytesPerSecond - eventMaxBytes)
            {
                return false;
            }

            _window.Events++;
            _window.Bytes += eventMaxBytes;
            reservation = new Reservation(this, _window, eventMaxBytes);
            return true;
        }
    }

    private void Finish(Reservation reservation, int? actualBytes)
    {
        lock (_gate)
        {
            if (actualBytes is int committedBytes)
            {
                reservation.Window.Bytes -= reservation.ReservedBytes - committedBytes;
            }
            else
            {
                reservation.Window.Events--;
                reservation.Window.Bytes -= reservation.ReservedBytes;
            }
        }
    }

    internal sealed class BudgetWindow
    {
        public int Events;
        public int Bytes;
    }

    /// <summary>单事件预算预留；旧时间窗的归还只影响其原有计数。</summary>
    internal sealed class Reservation : IDisposable
    {
        private readonly HttpLogCaptureBudget _owner;
        private int _finished;

        internal Reservation(
            HttpLogCaptureBudget owner,
            BudgetWindow window,
            int reservedBytes)
        {
            _owner = owner;
            Window = window;
            ReservedBytes = reservedBytes;
        }

        internal BudgetWindow Window { get; }

        public int ReservedBytes { get; }

        public void Commit(int actualBytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(actualBytes);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(actualBytes, ReservedBytes);
            if (Interlocked.Exchange(ref _finished, 1) == 0)
            {
                _owner.Finish(this, actualBytes);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _finished, 1) == 0)
            {
                _owner.Finish(this, actualBytes: null);
            }
        }
    }
}
