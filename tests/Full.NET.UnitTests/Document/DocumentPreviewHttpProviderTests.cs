using System.Net;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Microsoft.Extensions.Options;
using NSubstitute;
using Errors = Full.NET.Modules.Document.Contracts.DocumentErrorCodes;

namespace Full.NET.UnitTests.Document;

/// <summary>通过真实 HttpClient 序列化借用输入，并检查响应释放、实际字节预算与正文取消。</summary>
[TestClass]
public sealed class DocumentPreviewHttpProviderTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Output_remains_readable_after_response_disposal_and_input_remains_borrowed(bool seekable)
    {
        using var input = new TrackedStream([1, 2]);
        var body = new TrackedStream([3, 4, 5], seekable);
        var provider = Provider(new Handler(async (request, token) =>
        {
            var payload = await request.Content!.ReadAsStringAsync(token);
            StringAssert.Contains(payload, "application/msword; charset=utf-8");
            return Response(body);
        }));
        var result = await provider.ConvertAsync(Input(input), "unused");
        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(body.Disposed);
        Assert.IsFalse(input.Disposed);
        await using var output = result.Value!.PdfContent;
        Assert.AreEqual(3L, result.Value.PdfLength);
        Assert.AreEqual(3, output.ReadByte());
        Assert.AreEqual(4, output.ReadByte());
        Assert.AreEqual(5, output.ReadByte());
        Assert.AreEqual(-1, output.ReadByte());
    }

    [TestMethod]
    [DataRow(4L)]
    [DataRow(3L)]
    [DataRow(-1L)]
    public async Task Oversized_response_is_rejected_from_declared_or_actual_bytes(long declared)
    {
        using var input = new TrackedStream([1]);
        var body = new TrackedStream([1, 2, 3, 4], false);
        var provider = Provider(new Handler((_, _) => Task.FromResult(Response(body, declared < 0 ? null : declared))));
        var result = await provider.ConvertAsync(Input(input), "unused");
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Errors.OfficePreviewOutputTooLarge, result.Error!.Code);
        Assert.IsTrue(body.Disposed);
        Assert.IsFalse(input.Disposed);
    }

    [TestMethod]
    public async Task Understated_source_length_cannot_bypass_actual_upload_budget()
    {
        using var input = new TrackedStream([1, 2, 3, 4], false);
        var called = false;
        var provider = Provider(new Handler(async (request, token) =>
        {
            await request.Content!.CopyToAsync(Stream.Null, token);
            called = true;
            return Response(new TrackedStream([1]));
        }));
        var result = await provider.ConvertAsync(Input(input, 1), "unused");
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Errors.OfficePreviewInputTooLarge, result.Error!.Code);
        Assert.IsFalse(called);
        Assert.IsFalse(input.Disposed);
    }

    [TestMethod]
    public async Task Declared_oversized_source_is_rejected_before_sending()
    {
        using var input = new TrackedStream([1]);
        var called = false;
        var provider = Provider(new Handler((_, _) => { called = true; return Task.FromResult(Response(new TrackedStream([1]))); }));
        var result = await provider.ConvertAsync(Input(input, 4), "unused");
        Assert.AreEqual(Errors.OfficePreviewInputTooLarge, result.Error!.Code);
        Assert.IsFalse(called);
        Assert.AreEqual(0L, input.Position);
    }

    [TestMethod]
    public async Task Empty_response_is_conversion_failure_and_is_released()
    {
        using var input = new TrackedStream([1]);
        var body = new TrackedStream([]);
        var result = await Provider(new Handler((_, _) => Task.FromResult(Response(body)))).ConvertAsync(Input(input), "unused");
        Assert.AreEqual(Errors.OfficePreviewConversionFailed, result.Error!.Code);
        Assert.IsTrue(body.Disposed);
    }

    [TestMethod]
    public async Task Caller_cancellation_during_body_read_propagates_and_releases_response()
    {
        using var input = new TrackedStream([1]);
        using var cancellation = new CancellationTokenSource();
        var body = new WaitingStream();
        var pending = Provider(new Handler((_, _) => Task.FromResult(Response(body))))
            .ConvertAsync(Input(input), "unused", cancellation.Token);
        await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.IsTrue(body.Disposed);
        Assert.IsFalse(input.Disposed);
    }

    [TestMethod]
    public async Task Provider_deadline_covers_body_after_headers_have_arrived()
    {
        using var input = new TrackedStream([1]);
        var body = new WaitingStream();
        var pending = Provider(new Handler((_, _) => Task.FromResult(Response(body))))
            .ConvertAsync(Input(input), "unused");
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.AreEqual(Errors.OfficePreviewConversionFailed, result.Error!.Code);
        Assert.IsTrue(body.Entered.Task.IsCompletedSuccessfully);
        Assert.IsTrue(body.Disposed);
    }

    [TestMethod]
    public async Task Transport_failure_is_not_misreported_as_missing_or_oversized_content()
    {
        using var input = new TrackedStream([1]);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => Provider(new Handler((_, _) =>
            throw new HttpRequestException("fixture transport failure"))).ConvertAsync(Input(input), "unused"));
        Assert.IsFalse(input.Disposed);
    }

    private static ExternalHttpDocumentOfficePreviewConversionProvider Provider(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        var options = Substitute.For<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
        options.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions
        {
            MaxInputBytes = 3, MaxOutputBytes = 3, TimeoutSeconds = 5,
            ExternalHttp = new() { BaseUrl = "https://preview.invalid/convert" },
        });
        return new(factory, options);
    }

    private static DocumentOfficePreviewConversionInput Input(Stream input, long length = 1) =>
        new("source.doc", "application/msword; charset=utf-8", input, length);

    private static HttpResponseMessage Response(Stream body, long? length = null)
    {
        var content = new StreamContent(body);
        if (length.HasValue) content.Headers.ContentLength = length.Value;
        return new(HttpStatusCode.OK) { Content = content };
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private class TrackedStream(byte[] bytes, bool seekable = true) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => seekable && base.CanSeek;
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }

    private sealed class WaitingStream() : TrackedStream([], false)
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
