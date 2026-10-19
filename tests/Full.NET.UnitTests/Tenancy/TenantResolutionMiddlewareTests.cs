using System.Security.Claims;
using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>单元测试（多租户）：Tenant Resolution Middleware。</summary>
[TestClass]
public sealed class TenantResolutionMiddlewareTests
{
    private static readonly Guid TenantId =
        Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf294");

    [TestMethod]
    [DataRow("authenticated-id")]
    [DataRow("authenticated-domain")]
    [DataRow("anonymous-domain")]
    public async Task Authority_failure_does_not_enter_the_business_pipeline(string path)
    {
        var resolver = CreateResolver();
        var failure = new InvalidOperationException("authority unavailable");
        resolver.ById[TenantId] = CreateTenant(TenantId, "acme", "acme.localhost");
        resolver.ByDomain["acme.localhost"] = resolver.ById[TenantId];
        if (path == "authenticated-id") resolver.IdFailure = failure;
        else resolver.DomainFailure = failure;
        var fixture = CreateFixture(path == "authenticated-id" ? "admin.localhost" : "acme.localhost",
            path == "anonymous-domain" ? new ClaimsPrincipal(new ClaimsIdentity()) : CreateAuthenticatedPrincipal(TenantId), resolver);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.InvokeAsync());
        Assert.AreSame(failure, error);
        Assert.IsFalse(fixture.NextCalled);
        Assert.IsFalse(fixture.CurrentTenant.IsAvailable);
    }

    [TestMethod]
    [DataRow("authenticated-id")]
    [DataRow("authenticated-domain")]
    [DataRow("anonymous-domain")]
    public async Task Request_cancellation_is_forwarded_to_authority_and_does_not_enter_business(string path)
    {
        var resolver = CreateResolver();
        resolver.ById[TenantId] = CreateTenant(TenantId, "acme", "acme.localhost");
        resolver.ByDomain["acme.localhost"] = resolver.ById[TenantId];
        var fixture = CreateFixture(path == "authenticated-id" ? "admin.localhost" : "acme.localhost",
            path == "anonymous-domain" ? new ClaimsPrincipal(new ClaimsIdentity()) : CreateAuthenticatedPrincipal(TenantId), resolver);
        using var request = new CancellationTokenSource();
        fixture.HttpContext.RequestAborted = request.Token;
        // 已认证租户域名路径先完成 ID 读取，再取消并进入域名读取，确保两个边界分别被覆盖。
        if (path == "authenticated-domain") resolver.AfterIdLookup = request.Cancel;
        else request.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.InvokeAsync());
        Assert.AreEqual(request.Token, error.CancellationToken);
        Assert.AreEqual(path == "authenticated-domain" ? 2 : 1, resolver.ObservedTokens.Count);
        Assert.IsTrue(resolver.ObservedTokens.All(token => token == request.Token));
        if (path != "authenticated-id") Assert.AreEqual(1, resolver.DomainResolutionCount);
        Assert.IsFalse(fixture.NextCalled);
        Assert.IsFalse(fixture.CurrentTenant.IsAvailable);
    }

    [TestMethod]
    public async Task Authenticated_tenant_claim_resolves_tenant_on_host_domain()
    {
        var tenant = CreateTenant(TenantId, "acme", "acme.localhost");
        var resolver = CreateResolver();
        resolver.ById[TenantId] = tenant;
        TenantContext? observed = null;
        var fixture = CreateFixture(
            "admin.localhost",
            CreateAuthenticatedPrincipal(TenantId),
            resolver,
            accessor => observed = ReadTenant(accessor));

        await fixture.InvokeAsync();

        Assert.AreEqual(tenant.Id, observed?.Id);
        Assert.AreEqual(tenant.Identifier, observed?.Identifier);
        Assert.IsFalse(fixture.CurrentTenant.IsAvailable);
        Assert.AreEqual(0, resolver.DomainResolutionCount);
    }

    [TestMethod]
    public async Task Authenticated_tenant_claim_requires_matching_tenant_domain()
    {
        var tenant = CreateTenant(TenantId, "acme", "acme.localhost");
        var otherTenant = CreateTenant(Guid.CreateVersion7(), "beta", "beta.localhost");
        var resolver = CreateResolver();
        resolver.ById[TenantId] = tenant;
        resolver.ByDomain["beta.localhost"] = otherTenant;
        var fixture = CreateFixture(
            "beta.localhost",
            CreateAuthenticatedPrincipal(TenantId),
            resolver);

        await fixture.InvokeAsync();

        Assert.AreEqual(StatusCodes.Status403Forbidden, fixture.Mapper.StatusCode);
        Assert.AreEqual("tenancy.context_mismatch", fixture.Mapper.Error?.Code);
        Assert.IsFalse(fixture.NextCalled);
        Assert.IsFalse(fixture.CurrentTenant.IsAvailable);
    }

    [TestMethod]
    public async Task Authenticated_host_context_is_rejected_on_tenant_domain()
    {
        var resolver = CreateResolver();
        resolver.ByDomain["acme.localhost"] =
            CreateTenant(TenantId, "acme", "acme.localhost");
        var fixture = CreateFixture(
            "acme.localhost",
            CreateAuthenticatedPrincipal(),
            resolver);

        await fixture.InvokeAsync();

        Assert.AreEqual(StatusCodes.Status403Forbidden, fixture.Mapper.StatusCode);
        Assert.AreEqual("tenancy.context_mismatch", fixture.Mapper.Error?.Code);
        Assert.IsFalse(fixture.NextCalled);
    }

    [TestMethod]
    public async Task Missing_or_inactive_claimed_tenant_is_rejected()
    {
        var resolver = CreateResolver();
        resolver.ById[TenantId] =
            CreateTenant(TenantId, "acme", "acme.localhost", isActive: false);
        var fixture = CreateFixture(
            "admin.localhost",
            CreateAuthenticatedPrincipal(TenantId),
            resolver);

        await fixture.InvokeAsync();

        Assert.AreEqual(StatusCodes.Status403Forbidden, fixture.Mapper.StatusCode);
        Assert.AreEqual("tenancy.context_mismatch", fixture.Mapper.Error?.Code);
        Assert.IsFalse(fixture.NextCalled);
    }

    [TestMethod]
    public async Task Authenticated_host_context_ignores_untrusted_tenant_inputs()
    {
        var resolver = CreateResolver();
        var fixture = CreateFixture(
            "admin.localhost",
            CreateAuthenticatedPrincipal(),
            resolver,
            accessor => Assert.IsTrue(accessor.IsHost));
        fixture.HttpContext.Request.Headers["X-Tenant-Id"] = TenantId.ToString("D");
        fixture.HttpContext.Request.QueryString = new QueryString(
            $"?tenantId={TenantId:D}");
        fixture.HttpContext.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes($"{{\"tenantId\":\"{TenantId:D}\"}}"));

        await fixture.InvokeAsync();

        Assert.IsTrue(fixture.NextCalled);
        Assert.IsFalse(fixture.CurrentTenant.IsAvailable);
        Assert.AreEqual(0, resolver.IdResolutionCount);
        Assert.AreEqual(0, resolver.DomainResolutionCount);
    }

    [TestMethod]
    public async Task Anonymous_request_continues_to_resolve_tenant_by_domain()
    {
        var tenant = CreateTenant(TenantId, "acme", "acme.localhost");
        var resolver = CreateResolver();
        resolver.ByDomain["acme.localhost"] = tenant;
        TenantContext? observed = null;
        var fixture = CreateFixture(
            "acme.localhost",
            new ClaimsPrincipal(new ClaimsIdentity()),
            resolver,
            accessor => observed = ReadTenant(accessor));

        await fixture.InvokeAsync();

        Assert.AreEqual(tenant.Id, observed?.Id);
        Assert.IsTrue(fixture.NextCalled);
        Assert.IsFalse(fixture.CurrentTenant.IsAvailable);
    }

    private static MiddlewareFixture CreateFixture(
        string host,
        ClaimsPrincipal principal,
        StubTenantResolver resolver,
        Action<ICurrentTenant>? nextAssertion = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/tenancy/current";
        context.Request.Host = new HostString(host);
        context.User = principal;
        var accessor = new CurrentTenantAccessor();
        var mapper = new RecordingApiResultMapper();
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            nextAssertion?.Invoke(accessor);
            return Task.CompletedTask;
        });

        return new MiddlewareFixture(
            context,
            accessor,
            mapper,
            () => nextCalled,
            () => middleware.InvokeAsync(
                context,
                resolver,
                accessor,
                Options.Create(new TenancyOptions
                {
                    HostDomains = ["admin.localhost"]
                }),
                mapper));
    }

    private static StubTenantResolver CreateResolver() => new();

    private static TenantSummary CreateTenant(
        Guid id,
        string identifier,
        string domain,
        bool isActive = true) =>
        new(id, identifier, identifier.ToUpperInvariant(), domain, isActive, 1);

    private static ClaimsPrincipal CreateAuthenticatedPrincipal(Guid? tenantId = null)
    {
        var claims = tenantId.HasValue
            ? new[]
            {
                new Claim(
                    FullNetIdentityClaimTypes.TenantId,
                    tenantId.Value.ToString("D"))
            }
            : [];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static TenantContext? ReadTenant(ICurrentTenant currentTenant) =>
        currentTenant.Id.HasValue
            ? new TenantContext(
                currentTenant.Id.Value,
                currentTenant.Identifier ?? string.Empty,
                currentTenant.Name ?? string.Empty)
            : null;

    private sealed class MiddlewareFixture(
        DefaultHttpContext httpContext,
        CurrentTenantAccessor currentTenant,
        RecordingApiResultMapper mapper,
        Func<bool> nextCalled,
        Func<Task> invokeAsync)
    {
        public DefaultHttpContext HttpContext { get; } = httpContext;

        public CurrentTenantAccessor CurrentTenant { get; } = currentTenant;

        public RecordingApiResultMapper Mapper { get; } = mapper;

        public bool NextCalled => nextCalled();

        public Task InvokeAsync() => invokeAsync();
    }

    private sealed class RecordingApiResultMapper : IApiResultMapper
    {
        public Error? Error { get; private set; }

        public int? StatusCode { get; private set; }

        public IResult Map<T>(Result<T> result, HttpContext httpContext)
        {
            Error = result.Error;
            StatusCode = Error is null
                ? StatusCodes.Status200OK
                : StandardApiResultMapper.ToStatusCode(Error.Type);
            return new RecordedResult(StatusCode.Value);
        }

        public IResult MapException(Exception exception, HttpContext httpContext) =>
            new RecordedResult(StatusCodes.Status500InternalServerError);
    }

    private sealed class RecordedResult(int statusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
    }

    private sealed class StubTenantResolver : ITenantResolver
    {
        public Dictionary<Guid, TenantSummary> ById { get; } = [];

        public Dictionary<string, TenantSummary> ByDomain { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public int IdResolutionCount { get; private set; }

        public int DomainResolutionCount { get; private set; }

        public Exception? IdFailure { get; set; }
        public Exception? DomainFailure { get; set; }
        public Action? AfterIdLookup { get; set; }
        public List<CancellationToken> ObservedTokens { get; } = [];

        public Task<TenantSummary?> ResolveByDomainAsync(
            string domain,
            CancellationToken cancellationToken = default)
        {
            DomainResolutionCount++;
            ObservedTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (DomainFailure is not null) return Task.FromException<TenantSummary?>(DomainFailure);
            ByDomain.TryGetValue(domain, out var tenant);
            return Task.FromResult(tenant);
        }

        public Task<TenantSummary?> ResolveByIdAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            IdResolutionCount++;
            ObservedTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IdFailure is not null) return Task.FromException<TenantSummary?>(IdFailure);
            ById.TryGetValue(tenantId, out var tenant);
            AfterIdLookup?.Invoke();
            return Task.FromResult(tenant);
        }

        public Task<IReadOnlyList<TenantSummary>> GetAvailableAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantSummary>>([]);
    }
}
