using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Http;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityAuthSessionScopeFilterTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Non_host_or_inconsistent_context_is_forbidden_without_dispatch(bool host, bool tenantId)
    {
        var current = Substitute.For<ICurrentTenant>();
        current.IsHost.Returns(host);
        current.Id.Returns(tenantId ? Guid.CreateVersion7() : null);
        var mapper = new CaptureMapper();
        var filter = new IdentityAuthSessionScopeFilter(current, mapper);
        var called = false;
        var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(new DefaultHttpContext(), []), _ =>
        {
            called = true;
            return ValueTask.FromResult<object?>("dispatched");
        });
        Assert.IsFalse(called);
        Assert.AreEqual(StatusCodes.Status403Forbidden, ((IStatusCodeHttpResult)result!).StatusCode);
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, mapper.Error?.Code);
        Assert.AreEqual(ErrorType.Forbidden, mapper.Error?.Type);
    }

    [TestMethod]
    public async Task Host_context_preserves_cookie_csrf_and_request_cancellation()
    {
        var current = Substitute.For<ICurrentTenant>();
        current.IsHost.Returns(true);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "fullnet-refresh=secret; fullnet-csrf=csrf";
        context.Request.Headers["X-CSRF-Token"] = "csrf";
        using var cancellation = new CancellationTokenSource();
        context.RequestAborted = cancellation.Token;
        var expected = new object();
        var result = await new IdentityAuthSessionScopeFilter(current, new CaptureMapper()).InvokeAsync(
            new DefaultEndpointFilterInvocationContext(context, []), invocation =>
            {
                Assert.AreSame(context, invocation.HttpContext);
                Assert.AreEqual(cancellation.Token, invocation.HttpContext.RequestAborted);
                Assert.AreEqual("csrf", invocation.HttpContext.Request.Headers["X-CSRF-Token"].ToString());
                StringAssert.Contains(invocation.HttpContext.Request.Headers.Cookie.ToString(), "fullnet-refresh=secret");
                return ValueTask.FromResult<object?>(expected);
            });
        Assert.AreSame(expected, result);
    }

    [TestMethod]
    public async Task Rejection_does_not_clear_cookies_or_change_tenant_context()
    {
        var id = Guid.CreateVersion7();
        var current = Substitute.For<ICurrentTenant>();
        current.Id.Returns(id);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "fullnet-refresh=secret";
        await new IdentityAuthSessionScopeFilter(current, new CaptureMapper()).InvokeAsync(
            new DefaultEndpointFilterInvocationContext(context, []), _ => throw new AssertFailedException("must not dispatch"));
        Assert.AreEqual(id, current.Id);
        Assert.AreEqual("fullnet-refresh=secret", context.Request.Headers.Cookie.ToString());
        Assert.IsFalse(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    private sealed class CaptureMapper : IApiResultMapper
    {
        internal Error? Error { get; private set; }
        public IResult Map<T>(Result<T> result, HttpContext httpContext)
        {
            Error = result.Error;
            return Microsoft.AspNetCore.Http.Results.StatusCode(StandardApiResultMapper.ToStatusCode(result.Error!.Type));
        }
        public IResult MapException(Exception exception, HttpContext httpContext) => throw new NotSupportedException();
    }
}
