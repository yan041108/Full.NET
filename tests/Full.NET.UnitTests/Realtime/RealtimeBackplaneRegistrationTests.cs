using Full.NET.Realtime;
using Full.NET.Realtime.SignalR;
using Full.NET.Realtime.SignalR.Health;
using Full.NET.Realtime.SignalR.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;

namespace Full.NET.UnitTests.Realtime;

[TestClass]
public sealed class RealtimeBackplaneRegistrationTests
{
    [TestMethod]
    public void Registration_adds_realtime_context_to_http_json_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetRealtimeSignalR(
            new ConfigurationBuilder().Build(),
            "Testing");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>()
            .Value
            .SerializerOptions;

        Assert.IsTrue(options.TypeInfoResolverChain.Contains(
            RealtimeJsonSerializerContext.Default));
    }

    [TestMethod]
    public void Redis_configuration_keeps_reconnect_enabled_and_scopes_channels()
    {
        var configuration = RealtimeRedisConfiguration.Create(
            "127.0.0.1:6379,abortConnect=true",
            "Production");

        Assert.IsFalse(configuration.AbortOnConnectFail);
        Assert.AreEqual(
            "fullnet:production:signalr:",
            configuration.ChannelPrefix.ToString());
    }

    [TestMethod]
    [DataRow(" Production")]
    [DataRow("Production Blue")]
    [DataRow("Production:Blue")]
    [DataRow("生产")]
    [DataRow("-Production")]
    [DataRow("Production-")]
    public void Redis_configuration_rejects_noncanonical_environment_names(
        string environmentName)
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            RealtimeRedisConfiguration.Create(
                "127.0.0.1:6379",
                environmentName));

        StringAssert.Contains(
            exception.Message,
            "ASCII letters, digits, or hyphens");
    }

    [TestMethod]
    public void Dedicated_backplane_registers_a_ready_health_check()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetRealtimeSignalR(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{RealtimeOptions.SectionName}:HubPath"] =
                        "/custom/notifications",
                    [$"{RealtimeOptions.SectionName}:RedisBackplaneConnectionString"] =
                        "127.0.0.1:6379",
                })
                .Build(),
            "Testing");

        using var provider = services.BuildServiceProvider();
        Assert.AreEqual(
            "/custom/notifications",
            provider.GetRequiredService<IOptions<RealtimeOptions>>()
                .Value
                .HubPath);
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        Assert.IsTrue(registrations.Any(registration =>
            registration.Name == "realtime-backplane"
            && registration.Tags.Contains("ready")));
    }

    [TestMethod]
    public void Enabled_single_node_registration_exports_publish_meter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetRealtimeSignalR(
            new ConfigurationBuilder().Build(),
            "Testing");

        using var provider = services.BuildServiceProvider();

        Assert.IsNotNull(provider.GetRequiredService<MeterProvider>());
        Assert.IsEmpty(
            provider.GetServices<IRealtimeBackplaneProbe>());
        Assert.IsFalse(provider
            .GetServices<IOptions<HealthCheckServiceOptions>>()
            .SelectMany(options => options.Value.Registrations)
            .Any(registration =>
                registration.Name == "realtime-backplane"));
    }

    [TestMethod]
    public void Publisher_only_registration_excludes_api_authentication_adapters()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetRealtimePublisher(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{RealtimeOptions.SectionName}:RedisBackplaneConnectionString"] =
                        "127.0.0.1:6379",
                })
                .Build(),
            "Testing");

        using var provider = services.BuildServiceProvider();

        Assert.IsNotNull(provider.GetRequiredService<IRealtimePublisher>());
        Assert.IsEmpty(
            provider.GetServices<IPostConfigureOptions<JwtBearerOptions>>());
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;
        Assert.IsTrue(registrations.Any(registration =>
            registration.Name == "realtime-backplane"
            && registration.Tags.Contains("ready")));
        Assert.IsNotNull(
            provider.GetRequiredService<IRealtimeBackplaneProbe>());
        Assert.IsNotNull(provider.GetRequiredService<MeterProvider>());
    }

    [TestMethod]
    public async Task Hub_query_access_token_preserves_typed_jwt_events()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RecordingJwtBearerEvents>();
        services.AddAuthentication()
            .AddJwtBearer(options =>
                options.EventsType = typeof(RecordingJwtBearerEvents));
        services.AddFullNetRealtimeSignalR(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{RealtimeOptions.SectionName}:HubPath"] =
                        "/hubs/notifications",
                })
                .Build(),
            "Testing");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        httpContext.Request.Path = "/hubs/notifications";
        httpContext.Request.QueryString =
            new QueryString("?access_token=signalr-token");
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(JwtBearerHandler));
        var messageContext = new MessageReceivedContext(
            httpContext,
            scheme,
            options);

        Assert.IsNull(options.EventsType);
        await options.Events.MessageReceived(messageContext);
        Assert.AreEqual("signalr-token", messageContext.Token);
        Assert.AreEqual(
            1,
            scope.ServiceProvider
                .GetRequiredService<RecordingJwtBearerEvents>()
                .MessageReceivedCount);

        var validatedContext = new TokenValidatedContext(
            httpContext,
            scheme,
            options);
        await options.Events.TokenValidated(validatedContext);
        Assert.AreEqual(
            1,
            scope.ServiceProvider
                .GetRequiredService<RecordingJwtBearerEvents>()
                .ValidatedCount);
    }

    [TestMethod]
    public async Task Hub_query_access_token_is_limited_to_transport_paths()
    {
        var events = new SignalRAccessTokenJwtBearerEvents(
            "/hubs/notifications",
            new JwtBearerEvents(),
            configuredEventsType: null);

        foreach (var path in new[]
                 {
                     "/hubs/notifications",
                     "/hubs/notifications/negotiate",
                 })
        {
            var context = CreateMessageReceivedContext(path);

            await events.MessageReceived(context);

            Assert.AreEqual(
                "signalr-token",
                context.Token,
                $"SignalR transport path '{path}' 必须接受查询令牌。");
        }

        var descendantContext = CreateMessageReceivedContext(
            "/hubs/notifications/internal");

        await events.MessageReceived(descendantContext);

        Assert.IsNull(
            descendantContext.Token,
            "Hub 前缀下的普通后代路径不得把查询令牌提升为 Bearer 身份。");
    }

    [TestMethod]
    [DataRow("?access_token=first-token&access_token=second-token")]
    [DataRow("?access_token=same-token&access_token=same-token")]
    public async Task Hub_query_access_token_requires_a_single_value(
        string queryString)
    {
        var events = new SignalRAccessTokenJwtBearerEvents(
            "/hubs/notifications",
            new JwtBearerEvents(),
            configuredEventsType: null);
        var context = CreateMessageReceivedContext(
            "/hubs/notifications",
            queryString);

        await events.MessageReceived(context);

        Assert.IsNull(
            context.Token,
            "重复查询令牌必须在 JWT 解析前失败关闭，不能隐式合并为单个 Bearer 值。");
    }

    [TestMethod]
    public void Disabled_publisher_only_registration_uses_null_publisher()
    {
        var services = new ServiceCollection();
        services.AddFullNetRealtimePublisher(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{RealtimeOptions.SectionName}:Enabled"] = "false",
                })
                .Build(),
            "Testing");

        using var provider = services.BuildServiceProvider();

        Assert.AreSame(
            NullRealtimePublisher.Instance,
            provider.GetRequiredService<IRealtimePublisher>());
        Assert.IsFalse(provider
            .GetServices<IOptions<HealthCheckServiceOptions>>()
            .SelectMany(options => options.Value.Registrations)
            .Any(registration =>
                registration.Name == "realtime-backplane"));
    }

    [TestMethod]
    public void Enabled_publisher_only_registration_requires_a_backplane()
    {
        var services = new ServiceCollection();

        var exception = Assert.ThrowsExactly<OptionsValidationException>(() =>
            services.AddFullNetRealtimePublisher(
                new ConfigurationBuilder().Build(),
                "Testing"));

        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Realtime publishing outside the API host requires Realtime:RedisBackplaneConnectionString "
            + "(or ConnectionStrings:redis when Realtime:AllowSharedRedisInDevelopment=true).");
    }

    [TestMethod]
    public void Production_rejects_shared_cache_and_realtime_redis_connection_strings()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisConnectionString"] = "127.0.0.1:6379",
                [$"{RealtimeOptions.SectionName}:RedisBackplaneConnectionString"] =
                    "127.0.0.1:6379",
            })
            .Build();

        var exception = Assert.ThrowsExactly<OptionsValidationException>(() =>
            services.AddFullNetRealtimeSignalR(configuration, "Production"));

        StringAssert.Contains(
            exception.Failures.First(),
            "must differ");
    }

    [TestMethod]
    public void Development_allows_shared_redis_when_explicitly_opted_in()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetRealtimeSignalR(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cache:RedisConnectionString"] = "127.0.0.1:6379",
                    [$"{RealtimeOptions.SectionName}:RedisBackplaneConnectionString"] =
                        "127.0.0.1:6379",
                    [$"{RealtimeOptions.SectionName}:AllowSharedRedisInDevelopment"] =
                        "true",
                })
                .Build(),
            "Development");

        using var provider = services.BuildServiceProvider();
        Assert.AreEqual(
            "127.0.0.1:6379",
            provider.GetRequiredService<IOptions<RealtimeOptions>>()
                .Value
                .RedisBackplaneConnectionString);
    }

    [TestMethod]
    public void Production_does_not_fall_back_to_shared_connection_strings_redis()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetRealtimeSignalR(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:redis"] = "127.0.0.1:6379",
                })
                .Build(),
            "Production");

        using var provider = services.BuildServiceProvider();
        Assert.IsNull(
            provider.GetRequiredService<IOptions<RealtimeOptions>>()
                .Value
                .RedisBackplaneConnectionString);
        Assert.IsFalse(provider
            .GetServices<IOptions<HealthCheckServiceOptions>>()
            .SelectMany(options => options.Value.Registrations)
            .Any(registration => registration.Name == "realtime-backplane"));
    }

    [TestMethod]
    public void WebSocketsOnly_skip_negotiation_allows_disabling_session_affinity()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetRealtimeSignalR(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{RealtimeOptions.SectionName}:TransportMode"] = "WebSocketsOnly",
                    [$"{RealtimeOptions.SectionName}:SkipNegotiation"] = "true",
                    [$"{RealtimeOptions.SectionName}:RequireSessionAffinity"] = "false",
                })
                .Build(),
            "Testing");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RealtimeOptions>>().Value;
        Assert.AreEqual(RealtimeTransportMode.WebSocketsOnly, options.TransportMode);
        Assert.IsTrue(options.SkipNegotiation);
        Assert.IsFalse(options.RequireSessionAffinity);
    }

    [TestMethod]
    public void Default_transport_rejects_disabled_session_affinity()
    {
        var exception = Assert.ThrowsExactly<OptionsValidationException>(() =>
            new ServiceCollection().AddFullNetRealtimeSignalR(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [$"{RealtimeOptions.SectionName}:RequireSessionAffinity"] = "false",
                    })
                    .Build(),
                "Testing"));

        StringAssert.Contains(
            exception.Failures.First(),
            "RequireSessionAffinity");
    }

    [TestMethod]
    public void Invalid_hub_paths_fail_during_realtime_registration()
    {
        string[] invalidHubPaths =
        [
            "",
            "hubs/notifications",
            "/hubs/notifications?tenant=host",
            "/hubs/notifications#fragment",
            "/hubs/notifi cations",
        ];

        foreach (var hubPath in invalidHubPaths)
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{RealtimeOptions.SectionName}:HubPath"] = hubPath,
                })
                .Build();

            var exception = Assert.ThrowsExactly<OptionsValidationException>(
                () => services.AddFullNetRealtimeSignalR(
                    configuration,
                    "Testing"),
                $"HubPath '{hubPath}' 必须在服务注册期间被拒绝。");

            CollectionAssert.Contains(
                exception.Failures.ToArray(),
                "Realtime:HubPath must be an absolute path without whitespace, a query string, or a fragment.");
        }
    }

    [TestMethod]
    public void Noncanonical_hub_paths_fail_during_realtime_registration()
    {
        string[] invalidHubPaths =
        [
            "/",
            "/hubs/notifications/",
            "/hubs//notifications",
        ];

        foreach (var hubPath in invalidHubPaths)
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{RealtimeOptions.SectionName}:HubPath"] = hubPath,
                })
                .Build();

            _ = Assert.ThrowsExactly<OptionsValidationException>(
                () => services.AddFullNetRealtimeSignalR(
                    configuration,
                    "Testing"),
                $"HubPath '{hubPath}' 必须在服务注册期间被拒绝。");
        }
    }

    private static MessageReceivedContext CreateMessageReceivedContext(
        string path,
        string queryString = "?access_token=signalr-token")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.QueryString =
            new QueryString(queryString);
        var options = new JwtBearerOptions();
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(JwtBearerHandler));
        return new MessageReceivedContext(
            httpContext,
            scheme,
            options);
    }

    private sealed class RecordingJwtBearerEvents : JwtBearerEvents
    {
        public int MessageReceivedCount { get; private set; }

        public int ValidatedCount { get; private set; }

        public override Task MessageReceived(MessageReceivedContext context)
        {
            _ = context;
            MessageReceivedCount++;
            return Task.CompletedTask;
        }

        public override Task TokenValidated(TokenValidatedContext context)
        {
            _ = context;
            ValidatedCount++;
            return Task.CompletedTask;
        }
    }
}
