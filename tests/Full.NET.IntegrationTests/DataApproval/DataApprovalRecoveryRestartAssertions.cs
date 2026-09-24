using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Api;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MemoryPack;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace Full.NET.IntegrationTests.DataApproval;

internal static class DataApprovalRecoveryRestartAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var permissions = new[]
        {
            DataApprovalPermissions.Create,
            DataApprovalPermissions.Read,
            DataApprovalPermissions.ScenariosManage,
            SerialNumberRulePermissions.Create,
            SerialNumberRulePermissions.Read,
            WorkflowPermissions.FormsCreate,
            WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate,
            WorkflowPermissions.DefinitionsPublish,
            WorkflowPermissions.InstancesRead,
        };
        DataApprovalRequestResponse failedRequest;

        using var apiHost = new FullNetApiFactory(
            provider,
            connectionString,
            CreateWorkerSettings(pollMilliseconds: 60_000, retryDelaySeconds: 3_600),
            configureTestServices: services =>
            {
                services.RemoveAll<IWorkflowInstanceStarter>();
                services.AddSingleton<IWorkflowInstanceStarter, FailingWorkflowInstanceStarter>();
            });
        await apiHost.InitializeAsync(cancellationToken).ConfigureAwait(false);
        using var client = apiHost.CreateClientForHost("localhost");
        var token = await apiHost.CreateHostAccessTokenAsync(permissions, cancellationToken)
            .ConfigureAwait(false);

        var definitionVersionId = await CreatePublishedDefinitionAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await BindScenarioAsync(client, token, definitionVersionId, cancellationToken).ConfigureAwait(false);
        var ruleId = await CreateSerialRuleAsync(client, token, cancellationToken).ConfigureAwait(false);
        failedRequest = await CreateRequestInterruptedDuringWorkflowStartAsync(
                client,
                token,
                ruleId,
                cancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(DataApprovalStatusKeys.Pending, failedRequest.StatusKey);
        Assert.AreEqual(DataApprovalRecoveryStatusKeys.PendingLink, failedRequest.RecoveryStatusKey);
        Assert.IsNull(failedRequest.WorkflowInstanceId);
        Assert.IsNull(failedRequest.LastFailureCode);

        using var workerHost = await BuildRecoveryWorkerHostAsync(
            provider,
            connectionString,
            CreateWorkerSettings(pollMilliseconds: 500, retryDelaySeconds: 5))
            .ConfigureAwait(false);
        await workerHost.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var recovered = await WaitForWorkflowLinkAsync(
                    client,
                    token,
                    failedRequest.Id,
                    cancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(DataApprovalStatusKeys.InReview, recovered.StatusKey);
            Assert.AreEqual(DataApprovalRecoveryStatusKeys.None, recovered.RecoveryStatusKey);
            Assert.IsNotNull(recovered.WorkflowInstanceId);
            Assert.IsNull(recovered.LastFailureCode);

            using var workflowRead = await SendAuthorizedAsync(
                    client,
                    HttpMethod.Get,
                    $"/api/v1/workflow/instances/{recovered.WorkflowInstanceId:D}",
                    token,
                    cancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, workflowRead.StatusCode, await workflowRead.Content.ReadAsStringAsync(cancellationToken));
        }
        finally
        {
            await workerHost.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string?> CreateWorkerSettings(
        int pollMilliseconds,
        int retryDelaySeconds) => new()
    {
        ["DataApproval:RequestRecoveryWorker:BatchSize"] = "10",
        ["DataApproval:RequestRecoveryWorker:PollMilliseconds"] = pollMilliseconds.ToString(),
        ["DataApproval:RequestRecoveryWorker:RetryDelaySeconds"] = retryDelaySeconds.ToString(),
        ["DataApproval:ApplicationRecoveryWorker:PollMilliseconds"] = "60000",
        ["DataApproval:ApplicationRecoveryWorker:RetryDelaySeconds"] = "3600",
    };

    private static async Task<IHost> BuildRecoveryWorkerHostAsync(
        DatabaseProvider provider,
        string connectionString,
        IReadOnlyDictionary<string, string?> workerSettings)
    {
        var redisConnectionString = await SharedDatabaseFixture.GetRedisConnectionStringAsync()
            .ConfigureAwait(false);
        var settings = new Dictionary<string, string?>(workerSettings)
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = provider.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
            [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] = "Binary16",
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            ["Cache:RedisConnectionString"] = redisConnectionString,
            ["Realtime:RedisBackplaneConnectionString"] = redisConnectionString,
            ["Realtime:AllowSharedRedisInDevelopment"] = "true",
            ["ConnectionStrings:redis"] = redisConnectionString,
            ["Files:Local:RootPath"] = Path.Combine(
                Path.GetTempPath(),
                "fullnet-files-integration",
                $"data-approval-worker-{Guid.NewGuid():N}"),
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var builder = global::Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Full.NET.IntegrationTests.DataApproval.Worker",
            EnvironmentName = "Testing",
        });
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.Services.AddScoped<CurrentTenantAccessor>();
        builder.Services.AddScoped<ICurrentTenant>(services =>
            services.GetRequiredService<CurrentTenantAccessor>());
        builder.Services.AddScoped<ICurrentTenantContextWriter>(services =>
            services.GetRequiredService<CurrentTenantAccessor>());
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        builder.Services.AddSingleton<IApiResultMapper, NonHttpApiResultMapper>();
        builder.Services.AddSingleton<
            ITenantOrganizationUnitDirectory,
            EmptyTenantOrganizationUnitDirectory>();
        builder.Services.AddSingleton<
            IIdentityOrganizationUnitDirectory,
            EmptyIdentityOrganizationUnitDirectory>();
        builder.Services.AddFullNetDapper(configuration, "Testing");
        builder.Services.AddFullNetMemoryPack();
        builder.Services.AddFullNetCaching(configuration, "Testing");
        builder.Services.AddFullNetRealtimePublisher(configuration, "Testing");
        builder.Services.AddFullNetApplicationModules(
            configuration,
            FullNetHostProfile.Worker);
        foreach (var descriptor in builder.Services
                     .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                         && descriptor.ImplementationType?.Name != "DataApprovalRequestRecoveryHostedProcessor")
                     .ToArray())
        {
            builder.Services.Remove(descriptor);
        }

        return builder.Build();
    }

    private static async Task<Guid> CreatePublishedDefinitionAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        var stamp = Guid.NewGuid().ToString("N");
        using var formResponse = await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/workflow/forms",
                token,
                new
                {
                    formKey = $"data-approval-recovery.{stamp}",
                    draft = new
                    {
                        schemaVersion = 1,
                        adapterVersion = 1,
                        sections = new[]
                        {
                            new
                            {
                                sectionKey = "main",
                                fields = new[]
                                {
                                    new { fieldKey = "reason", fieldTypeKey = "text", required = false, constraints = new { } },
                                    new { fieldKey = "decision", fieldTypeKey = "text", required = false, constraints = new { } },
                                },
                            },
                        },
                    },
                },
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, formResponse.StatusCode, await formResponse.Content.ReadAsStringAsync(cancellationToken));
        using var form = JsonDocument.Parse(await formResponse.Content.ReadAsStringAsync(cancellationToken));
        var formId = form.RootElement.GetProperty("id").GetGuid();
        using var formPublishResponse = await SendJsonAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/workflow/forms/{formId:D}/publish",
                token,
                new { expectedRevision = 1 },
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, formPublishResponse.StatusCode, await formPublishResponse.Content.ReadAsStringAsync(cancellationToken));
        using var formVersion = JsonDocument.Parse(await formPublishResponse.Content.ReadAsStringAsync(cancellationToken));
        var formVersionId = formVersion.RootElement.GetProperty("id").GetGuid();

        using var definitionResponse = await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/workflow/definitions",
                token,
                new
                {
                    definitionKey = $"data-approval-recovery.{stamp}",
                    draft = new
                    {
                        schemaVersion = 1,
                        nodes = new object[]
                        {
                            new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "approve" } } },
                            new
                            {
                                nodeKey = "approve",
                                nodeTypeKey = "human.approval",
                                nodeSchemaVersion = 1,
                                config = new
                                {
                                    nextNodeKeys = new[] { "end" },
                                    fieldPolicies = new { reason = "readOnly", decision = "required" },
                                },
                            },
                            new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { nextNodeKeys = Array.Empty<string>() } },
                        },
                    },
                },
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, definitionResponse.StatusCode, await definitionResponse.Content.ReadAsStringAsync(cancellationToken));
        using var definition = JsonDocument.Parse(await definitionResponse.Content.ReadAsStringAsync(cancellationToken));
        var definitionId = definition.RootElement.GetProperty("id").GetGuid();

        using var publishResponse = await SendJsonAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/workflow/definitions/{definitionId:D}/publish",
                token,
                new { expectedRevision = 1, formVersionId },
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, publishResponse.StatusCode, await publishResponse.Content.ReadAsStringAsync(cancellationToken));
        using var version = JsonDocument.Parse(await publishResponse.Content.ReadAsStringAsync(cancellationToken));
        return version.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task BindScenarioAsync(
        HttpClient client,
        string token,
        Guid definitionVersionId,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
                client,
                HttpMethod.Put,
                $"/api/v1/data-approvals/scenarios/{DataApprovalScenarioKeys.SerialRuleHostUpdate}",
                token,
                new { isEnabled = true, workflowDefinitionVersionId = definitionVersionId, version = (long?)null },
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task<Guid> CreateSerialRuleAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/serial-numbers/rules",
                token,
                new CreateSerialNumberRuleRequest(
                    $"recovery.{Guid.NewGuid():N}",
                    "Recovery integration rule",
                    null,
                    SerialNumberRuleScope.Host,
                    SerialNumberResetInterval.Never,
                    "REC-{sequence:4}",
                    1,
                    9999,
                    0,
                    true),
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<DataApprovalRequestResponse> CreateRequestInterruptedDuringWorkflowStartAsync(
        HttpClient client,
        string token,
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/data-approvals/requests",
                token,
                new CreateDataApprovalRequestBody(
                    DataApprovalScenarioKeys.SerialRuleHostUpdate,
                    ruleId,
                    JsonSerializer.Serialize(new { displayName = "Recovered rule" }),
                    $"recovery-{Guid.NewGuid():N}"),
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        var requestId = FailingWorkflowInstanceStarter.LastRequestId.GetValueOrDefault();
        Assert.AreNotEqual(Guid.Empty, requestId);
        using var readResponse = await SendAuthorizedAsync(
                client,
                HttpMethod.Get,
                $"/api/v1/data-approvals/requests/{requestId:D}",
                token,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, readResponse.StatusCode, await readResponse.Content.ReadAsStringAsync(cancellationToken));
        var request = await readResponse.Content.ReadFromJsonAsync<DataApprovalRequestResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(request);
        return request!;
    }

    private static async Task<DataApprovalRequestResponse> WaitForWorkflowLinkAsync(
        HttpClient client,
        string token,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await SendAuthorizedAsync(
                    client,
                    HttpMethod.Get,
                    $"/api/v1/data-approvals/requests/{requestId:D}",
                    token,
                    cancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            var request = await response.Content.ReadFromJsonAsync<DataApprovalRequestResponse>(cancellationToken)
                .ConfigureAwait(false);
            Assert.IsNotNull(request);
            if (request!.WorkflowInstanceId is not null)
            {
                return request;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        Assert.Fail($"DataApproval request {requestId:D} was not linked after the recovery worker started.");
        throw new InvalidOperationException("Unreachable after Assert.Fail.");
    }

    private static Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string token,
        object body,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request, cancellationToken);
    }

    private static Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string token,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request, cancellationToken);
    }

    private sealed class EmptyTenantOrganizationUnitDirectory
        : ITenantOrganizationUnitDirectory
    {
        public Task<TenantOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
            Guid tenantId,
            Guid unitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantOrganizationUnitDirectoryEntry?>(null);
    }

    private sealed class EmptyIdentityOrganizationUnitDirectory
        : IIdentityOrganizationUnitDirectory
    {
        public Task<IdentityOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
            Guid tenantId,
            Guid unitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentityOrganizationUnitDirectoryEntry?>(null);
    }

    private sealed class NonHttpApiResultMapper : IApiResultMapper
    {
        public IResult Map<T>(Result<T> result, HttpContext httpContext) =>
            throw new NotSupportedException("非 HTTP Worker 集成夹具不映射 API 结果。");

        public IResult MapException(Exception exception, HttpContext httpContext) =>
            throw new NotSupportedException("非 HTTP Worker 集成夹具不映射 API 异常。");
    }

    private sealed class FailingWorkflowInstanceStarter : IWorkflowInstanceStarter
    {
        public static Guid? LastRequestId { get; private set; }

        public Task<Result<WorkflowInstanceLifecycleResult>> StartAsync(
            Guid actorUserId,
            StartWorkflowInstanceCommand command,
            CancellationToken cancellationToken = default) =>
            InterruptProcess(command);

        private static Task<Result<WorkflowInstanceLifecycleResult>> InterruptProcess(
            StartWorkflowInstanceCommand command)
        {
            LastRequestId = Guid.Parse(command.BusinessId);
            throw new InvalidOperationException(
                "The integration test simulates a process interruption during workflow start.");
        }
    }
}
