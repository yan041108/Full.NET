using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Hosting.Observability;

/// <summary>持有一次投影预算的租约；未完成或失败时释放预留。</summary>
public sealed class HttpLogCaptureLease : IDisposable
{
    private readonly HttpContext _httpContext;
    private readonly HttpLogCaptureTarget _target;
    private readonly string _projectionKey;
    private readonly HttpLogCaptureBudget.Reservation _reservation;
    private readonly int _maxBytes;
    private int _completed;

    internal HttpLogCaptureLease(
        HttpContext httpContext,
        HttpLogCaptureTarget target,
        string projectionKey,
        HttpLogCaptureBudget.Reservation reservation,
        int maxBytes)
    {
        _httpContext = httpContext;
        _target = target;
        _projectionKey = projectionKey;
        _reservation = reservation;
        _maxBytes = maxBytes;
    }

    /// <summary>在已取得许可后执行受控投影工厂。</summary>
    /// <typeparam name="TProjection">静态登记的投影 DTO。</typeparam>
    /// <param name="boundedProjectionFactory">仅在许可后调用的投影工厂。</param>
    /// <param name="jsonTypeInfo">静态源生成的 JSON 元数据。</param>
    /// <returns>不含原始异常的安全捕获结果。</returns>
    public HttpLogCaptureResult Capture<TProjection>(
        Func<TProjection> boundedProjectionFactory,
        JsonTypeInfo<TProjection> jsonTypeInfo)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return new HttpLogCaptureResult(HttpLogCaptureState.Failed, null);
        }

        var factoryStarted = false;
        try
        {
            if (boundedProjectionFactory is null || jsonTypeInfo is null)
            {
                return new HttpLogCaptureResult(HttpLogCaptureState.Failed, null);
            }

            // 只认可静态登记的具体 DTO 和本程序集源生成元数据，拒绝调用方自定义 Converter。
            var requestProjection = _projectionKey == HttpLogCaptureProjectionKeys.Pagination
                && typeof(TProjection) == typeof(HttpPaginationLogProjection)
                && ReferenceEquals(
                    jsonTypeInfo,
                    HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection);
            var responseProjection = _projectionKey == HttpLogCaptureProjectionKeys.PaginationResult
                && typeof(TProjection) == typeof(HttpPaginationResultLogProjection)
                && ReferenceEquals(
                    jsonTypeInfo,
                    HttpLogCaptureJsonContext.Default.HttpPaginationResultLogProjection);
            if (_target != HttpLogCaptureTarget.B2InternalSummary
                || (!requestProjection && !responseProjection))
            {
                return new HttpLogCaptureResult(HttpLogCaptureState.NotAllowed, null);
            }

            if (_httpContext.RequestAborted.IsCancellationRequested)
            {
                return new HttpLogCaptureResult(HttpLogCaptureState.Failed, null);
            }

            factoryStarted = true;
            var projection = boundedProjectionFactory();
            if (requestProjection
                && (projection is not HttpPaginationLogProjection pagination
                    || pagination.Page < 0
                    || pagination.PageSize is < 1 or > 1_000))
            {
                return new HttpLogCaptureResult(HttpLogCaptureState.NotAllowed, null);
            }

            if (responseProjection
                && (projection is not HttpPaginationResultLogProjection response
                    || response.Page < 0
                    || response.PageSize is < 1 or > 1_000
                    || response.TotalCount < 0
                    || response.ItemCount < 0
                    || response.ItemCount > response.PageSize))
            {
                return new HttpLogCaptureResult(HttpLogCaptureState.NotAllowed, null);
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(projection, jsonTypeInfo);
            if (bytes.Length > _maxBytes)
            {
                return new HttpLogCaptureResult(HttpLogCaptureState.BudgetExceeded, null);
            }

            if (_httpContext.RequestAborted.IsCancellationRequested)
            {
                return new HttpLogCaptureResult(HttpLogCaptureState.Failed, null);
            }

            var result = new HttpLogCaptureResult(
                HttpLogCaptureState.Captured,
                Encoding.UTF8.GetString(bytes),
                _httpContext,
                _target);
            var itemKey = requestProjection
                ? HttpOperationPayloadProjection.B2ResultItemKey
                : HttpOperationPayloadProjection.B2ResponseResultItemKey;
            _httpContext.Items[itemKey] = result;
            _reservation.Commit(bytes.Length);
            return result;
        }
        catch (Exception)
        {
            // 投影失败是 B2 旁路失败；原始异常消息和对象绝不进入日志。
            return new HttpLogCaptureResult(HttpLogCaptureState.Failed, null);
        }
        finally
        {
            // 工厂一旦开始运行，失败尝试仍占用本秒名额，防止反复超限或抛错绕过 CPU 限流。
            if (factoryStarted)
            {
                _reservation.Commit(_maxBytes);
            }

            _reservation.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _reservation.Dispose();
        }
    }
}
