using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Features.WriteAccessLogs;
using Full.NET.Modules.Auditing.Middleware;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditingAccessLogWritePathTests
{
    [TestMethod]
    public async Task Authenticated_get_is_persisted_without_query_string()
    {
        var executor = new RecordingCommandExecutor();
        await using var services = CreateServices(executor);
        var queue = ActivatorUtilities.CreateInstance<AccessLogWriteQueue>(services);
        await queue.StartAsync(CancellationToken.None);
        try
        {
            var userId = Guid.CreateVersion7();
            var context = new DefaultHttpContext
            {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(FullNetIdentityClaimTypes.Subject, userId.ToString("D"))],
                    "test")),
            };
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/v1/identity/users";
            context.Request.QueryString = new QueryString("?access_token=secret");
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/v1/identity/users"),
                0,
                null,
                null));

            var middleware = new AccessLogMiddleware(_ => Task.CompletedTask);
            await middleware.InvokeAsync(context, queue, new FixedClock());
            await executor.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual("auditing.insert_access_log", executor.Statement?.Name);
            Assert.IsNotNull(executor.Parameters);
            Assert.AreEqual("/api/v1/identity/users", executor.Parameters["a0_RequestPath"]);
            Assert.AreEqual(userId, executor.Parameters["a0_UserId"]);
            Assert.AreEqual(true, executor.Parameters["a0_IsAuthenticated"]);
            Assert.IsFalse(executor.Statement!.Text.Contains("secret", StringComparison.Ordinal));
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public void Full_queue_rejects_access_record_without_waiting()
    {
        var executor = new RecordingCommandExecutor();
        using var services = CreateServices(executor, capacity: 1);
        using var queue = ActivatorUtilities.CreateInstance<AccessLogWriteQueue>(services);
        var model = new AccessLogWriteModel(
            DateTimeOffset.UtcNow,
            "GET",
            "/api/v1/identity/users",
            200,
            1,
            null,
            null,
            null,
            null,
            false);

        Assert.IsTrue(queue.TryEnqueue(model));
        Assert.IsFalse(queue.TryEnqueue(model));
        Assert.IsNull(executor.Statement);
    }

    [TestMethod]
    public void Access_log_capture_requires_explicit_enable_and_bounded_batch()
    {
        var options = new AccessLogCaptureOptions();
        Assert.IsFalse(options.Enabled);

        options.Capacity = 1;
        options.MaxBatchRows = 101;
        var validation = new AccessLogCaptureOptionsValidator().Validate(null, options);
        Assert.IsFalse(validation.Succeeded);
    }

    [TestMethod]
    public async Task Two_requests_share_one_database_insert()
    {
        var executor = new RecordingCommandExecutor();
        await using var services = CreateServices(
            executor,
            maxBatchRows: 2,
            maxBatchDelay: TimeSpan.FromSeconds(1));
        var queue = ActivatorUtilities.CreateInstance<AccessLogWriteQueue>(services);
        await queue.StartAsync(CancellationToken.None);
        try
        {
            var middleware = new AccessLogMiddleware(_ => Task.CompletedTask);
            for (var index = 0; index < 2; index++)
            {
                var context = new DefaultHttpContext { RequestServices = services };
                context.Request.Method = HttpMethods.Get;
                context.Request.Path = "/api/v1/identity/users";
                await middleware.InvokeAsync(context, queue, new FixedClock());
            }

            await executor.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, executor.CallCount);
            Assert.AreEqual(22, executor.Parameters?.Count);
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task Anonymous_unauthorized_get_is_persisted()
    {
        var executor = new RecordingCommandExecutor();
        await using var services = CreateServices(executor);
        var queue = ActivatorUtilities.CreateInstance<AccessLogWriteQueue>(services);
        await queue.StartAsync(CancellationToken.None);
        try
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/v1/identity/users";
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/v1/identity/users"),
                0,
                null,
                null));
            var middleware = new AccessLogMiddleware(httpContext =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, queue, new FixedClock());
            await executor.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(401, executor.Parameters?["a0_StatusCode"]);
            Assert.AreEqual(false, executor.Parameters?["a0_IsAuthenticated"]);
            Assert.IsNull(executor.Parameters?["a0_UserId"]);
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task Unmatched_path_does_not_persist_untrusted_segments()
    {
        var executor = new RecordingCommandExecutor();
        await using var services = CreateServices(executor);
        var queue = ActivatorUtilities.CreateInstance<AccessLogWriteQueue>(services);
        await queue.StartAsync(CancellationToken.None);
        try
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/secret-token-in-path";
            var middleware = new AccessLogMiddleware(httpContext =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, queue, new FixedClock());
            await executor.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual("/api/{unmatched}", executor.Parameters?["a0_RequestPath"]);
            Assert.AreEqual(404, executor.Parameters?["a0_StatusCode"]);
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider CreateServices(
        ICommandExecutor executor,
        int capacity = 8,
        int maxBatchRows = 1,
        TimeSpan? maxBatchDelay = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(executor);
        services.AddSingleton<ICommandExecutor>(executor);
        services.AddSingleton<IIdGenerator, SequenceIdGenerator>();
        services.AddOptions<AccessLogCaptureOptions>().Configure(options =>
        {
            options.Enabled = true;
            options.Capacity = capacity;
            options.MaxBatchRows = maxBatchRows;
            options.MaxBatchDelay = maxBatchDelay ?? TimeSpan.FromMilliseconds(10);
        });
        return services.BuildServiceProvider();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }

    private sealed class RecordingCommandExecutor : ICommandExecutor
    {
        public TaskCompletionSource Written { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SqlStatement? Statement { get; private set; }

        public int CallCount { get; private set; }

        public Dictionary<string, object?>? Parameters { get; private set; }

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Statement = statement;
            Parameters = parameters as Dictionary<string, object?>;
            Written.TrySetResult();
            return Task.FromResult(Parameters?.Keys.Count(key =>
                key.EndsWith("_Id", StringComparison.Ordinal)) ?? 0);
        }
    }
}
