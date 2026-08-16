using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFiles;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Document;

/// <summary>Document version references are protected by Files claim state.</summary>
internal static class DocumentFilesReferenceClaimAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string documentWriterToken,
        HostDocumentItemResponse itemWithVersion,
        CancellationToken cancellationToken = default)
    {
        var fileId = itemWithVersion.CurrentVersion!.FileId;
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/files/host-files/{fileId:D}/delete")
        {
            Content = JsonContent.Create(new { }),
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var deleteResponse = await client.SendAsync(deleteRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await deleteResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            FilesErrorCodes.FileReferenced,
            problem.RootElement.GetProperty("code").GetString());

        using var downloadRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/document/host/items/{itemWithVersion.Id:D}/content");
        downloadRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            documentWriterToken);
        using var downloadResponse = await client.SendAsync(downloadRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, downloadResponse.StatusCode);
    }

    public static async Task VerifyClaimDeleteConcurrencyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var fileId = await SeedReadyHostFileAsync(factory, cancellationToken);
            await RunClaimDeleteRaceAsync(
                factory,
                fileId,
                claimStartsFirst: true,
                cancellationToken);
            fileId = await SeedReadyHostFileAsync(factory, cancellationToken);
            await RunClaimDeleteRaceAsync(
                factory,
                fileId,
                claimStartsFirst: false,
                cancellationToken);
        }
    }

    /// <summary>无启动门闩的并发矩阵，验证行锁顺序在真实竞争下仍保持终端不变量。</summary>
    public static async Task VerifyClaimDeleteUnsynchronizedConcurrencyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var fileId = await SeedReadyHostFileAsync(factory, cancellationToken);
            await RunClaimDeleteRaceUnsynchronizedAsync(factory, fileId, cancellationToken);
        }
    }

    /// <summary>HTTP 删除路径与服务内 Claim 并发，验证 Endpoint 与 Files 事务语义一致。</summary>
    public static async Task VerifyClaimDeleteHttpConcurrencyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClient();
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var fileId = await SeedReadyHostFileAsync(factory, cancellationToken);
            await RunClaimDeleteHttpRaceAsync(
                factory,
                client,
                adminToken,
                fileId,
                cancellationToken);
        }
    }

    private static async Task RunClaimDeleteRaceAsync(
        FullNetApiFactory factory,
        Guid fileId,
        bool claimStartsFirst,
        CancellationToken cancellationToken)
    {
        var consumerReferenceId = Guid.CreateVersion7();
        var idempotencyKey = $"integration-race:{consumerReferenceId:N}";
        var claimRequest = new HostFileReferenceClaimRequest(
            idempotencyKey,
            HostFileReferenceClaimConsumerModules.Document,
            consumerReferenceId,
            fileId);
        var ready = new CountdownEvent(2);
        using var startGate = new ManualResetEventSlim(false);

        var claimTask = Task.Run(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            ready.Signal();
            ready.Wait(cancellationToken);
            if (!claimStartsFirst)
            {
                startGate.Wait(cancellationToken);
            }

            var claimService = scope.ServiceProvider
                .GetRequiredService<IHostFileReferenceClaimService>();
            if (claimStartsFirst)
            {
                startGate.Set();
            }

            return await claimService.ClaimAsync(claimRequest, cancellationToken);
        }, cancellationToken);

        var deleteTask = Task.Run(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            ready.Signal();
            ready.Wait(cancellationToken);
            if (claimStartsFirst)
            {
                startGate.Wait(cancellationToken);
            }

            var deleteService = scope.ServiceProvider
                .GetRequiredService<HostFileManagementService>();
            if (!claimStartsFirst)
            {
                startGate.Set();
            }

            return await deleteService.DeleteAsync(fileId, cancellationToken);
        }, cancellationToken);

        await Task.WhenAll(claimTask, deleteTask).WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
        await AssertClaimDeleteTerminalStateAsync(
            factory,
            fileId,
            await claimTask,
            await deleteTask,
            cancellationToken);
    }

    private static async Task RunClaimDeleteRaceUnsynchronizedAsync(
        FullNetApiFactory factory,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var consumerReferenceId = Guid.CreateVersion7();
        var idempotencyKey = $"integration-unsync-race:{consumerReferenceId:N}";
        var claimRequest = new HostFileReferenceClaimRequest(
            idempotencyKey,
            HostFileReferenceClaimConsumerModules.Document,
            consumerReferenceId,
            fileId);

        var claimTask = Task.Run(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var claimService = scope.ServiceProvider
                .GetRequiredService<IHostFileReferenceClaimService>();
            return await claimService.ClaimAsync(claimRequest, cancellationToken);
        }, cancellationToken);

        var deleteTask = Task.Run(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var deleteService = scope.ServiceProvider
                .GetRequiredService<HostFileManagementService>();
            return await deleteService.DeleteAsync(fileId, cancellationToken);
        }, cancellationToken);

        await Task.WhenAll(claimTask, deleteTask).WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
        await AssertClaimDeleteTerminalStateAsync(
            factory,
            fileId,
            await claimTask,
            await deleteTask,
            cancellationToken);
    }

    private static async Task RunClaimDeleteHttpRaceAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string adminToken,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var consumerReferenceId = Guid.CreateVersion7();
        var idempotencyKey = $"integration-http-race:{consumerReferenceId:N}";
        var claimRequest = new HostFileReferenceClaimRequest(
            idempotencyKey,
            HostFileReferenceClaimConsumerModules.Document,
            consumerReferenceId,
            fileId);

        var claimTask = Task.Run(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var claimService = scope.ServiceProvider
                .GetRequiredService<IHostFileReferenceClaimService>();
            return await claimService.ClaimAsync(claimRequest, cancellationToken);
        }, cancellationToken);

        var deleteTask = Task.Run(async () =>
        {
            using var deleteRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/files/host-files/{fileId:D}/delete")
            {
                Content = JsonContent.Create(new { }),
            };
            deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            using var deleteResponse = await client.SendAsync(deleteRequest, cancellationToken);
            if (deleteResponse.IsSuccessStatusCode)
            {
                return Result<HostFileResponse>.Success(new HostFileResponse(
                    fileId,
                    "race.bin",
                    "application/octet-stream",
                    4,
                    null,
                    DateTimeOffset.UtcNow,
                    Guid.CreateVersion7()));
            }

            using var problem = JsonDocument.Parse(
                await deleteResponse.Content.ReadAsStringAsync(cancellationToken));
            var code = problem.RootElement.GetProperty("code").GetString()
                ?? FilesErrorCodes.FileReferenced;
            return Result<HostFileResponse>.Failure(
                new Error(code, code, ErrorType.Conflict));
        }, cancellationToken);

        await Task.WhenAll(claimTask, deleteTask).WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
        await AssertClaimDeleteTerminalStateAsync(
            factory,
            fileId,
            await claimTask,
            await deleteTask,
            cancellationToken);
    }

    private static async Task AssertClaimDeleteTerminalStateAsync(
        FullNetApiFactory factory,
        Guid fileId,
        Result<HostFileReferenceClaimResult> claimResult,
        Result<HostFileResponse> deleteResult,
        CancellationToken cancellationToken)
    {
        await using var verifyScope = factory.Services.CreateAsyncScope();
        verifyScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var query = verifyScope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var claimService = verifyScope.ServiceProvider
            .GetRequiredService<IHostFileReferenceClaimService>();
        var activeFile = await query.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
            HostFileSql.FindActiveById,
            new { FileId = fileId },
            cancellationToken);

        if (claimResult.IsSuccess)
        {
            Assert.IsFalse(deleteResult.IsSuccess);
            Assert.AreEqual(FilesErrorCodes.FileReferenced, deleteResult.Error!.Code);
            Assert.IsNotNull(activeFile);
            Assert.IsTrue(
                await claimService.HasOpenClaimsAsync(fileId, cancellationToken));
            return;
        }

        Assert.AreEqual(FilesErrorCodes.FileNotFound, claimResult.Error!.Code);
        Assert.IsTrue(deleteResult.IsSuccess);
        Assert.IsNull(activeFile);
        Assert.IsFalse(
            await claimService.HasOpenClaimsAsync(fileId, cancellationToken));
    }

    private static async Task<Guid> SeedReadyHostFileAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var providers = scope.ServiceProvider.GetRequiredService<FileStorageProviderRegistry>();
        var storage = providers.DefaultProvider;
        var fileId = Guid.CreateVersion7();
        var createdAtUtc = DateTimeOffset.UtcNow;
        var storageKey = $"host/race/{fileId:N}";
        Assert.AreEqual(
            1,
            await command.ExecuteAsync(
                HostFileSql.Insert,
                new
                {
                    Id = fileId,
                    OriginalFileName = "race.bin",
                    ContentType = "application/octet-stream",
                    SizeBytes = 4L,
                    storage.ProviderKey,
                    StorageKey = storageKey,
                    ContentHash = (string?)null,
                    CreatedAtUtc = createdAtUtc,
                    CreatedByUserId = Guid.CreateVersion7(),
                },
                cancellationToken));
        Assert.AreEqual(
            1,
            await command.ExecuteAsync(
                HostFileSql.ClaimPublication,
                new
                {
                    FileId = fileId,
                    storage.ProviderKey,
                    StorageKey = storageKey,
                },
                cancellationToken));
        await using (var content = new MemoryStream([1, 2, 3, 4], writable: false))
        {
            await storage.SaveAsync(storageKey, content, cancellationToken);
        }

        Assert.AreEqual(
            1,
            await command.ExecuteAsync(
                HostFileSql.MarkReady,
                new
                {
                    FileId = fileId,
                    storage.ProviderKey,
                    StorageKey = storageKey,
                },
                cancellationToken));
        return fileId;
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }
}
