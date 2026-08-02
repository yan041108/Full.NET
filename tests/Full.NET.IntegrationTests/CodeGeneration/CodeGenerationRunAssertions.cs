using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.CodeGeneration.Contracts;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>
/// 验证 Host 代码生成运行记录的独立授权、不可变摘要和安全失败契约。
/// </summary>
internal static class CodeGenerationRunAssertions
{
    private const string RunsPath = "/api/v1/code-generation/runs";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        string applyWorkspaceRoot,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var reader = await factory.CreateHostIdentityAsync(
            $"codegen-run-reader-{Guid.NewGuid():N}",
            [CodeGenerationRunPermissions.Read],
            cancellationToken);
        var executor = await factory.CreateHostIdentityAsync(
            $"codegen-run-executor-{Guid.NewGuid():N}",
            [CodeGenerationRunPermissions.Execute],
            cancellationToken);
        var applier = await factory.CreateHostIdentityAsync(
            $"codegen-run-applier-{Guid.NewGuid():N}",
            [CodeGenerationRunPermissions.Apply],
            cancellationToken);
        var roller = await factory.CreateHostIdentityAsync(
            $"codegen-run-roller-{Guid.NewGuid():N}",
            [CodeGenerationRunPermissions.Rollback],
            cancellationToken);
        var reviewer = await factory.CreateHostIdentityAsync(
            $"codegen-run-reviewer-{Guid.NewGuid():N}",
            [
                CodeGenerationTemplatePermissions.Write,
                CodeGenerationRunPermissions.Execute,
            ],
            cancellationToken);

        using (var readerCannotExecute = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/preview",
                       reader.AccessToken,
                       new CodeGenerationRunPreviewRequest(
                           null,
                           null,
                           CreateSchema())),
                   cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Forbidden,
                readerCannotExecute.StatusCode);
        }

        CodeGenerationRunPreviewResponse tracked;
        using (var execute = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/preview",
                       executor.AccessToken,
                       new CodeGenerationRunPreviewRequest(
                           null,
                           null,
                           CreateSchema())),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, execute.StatusCode);
            tracked = (await execute.Content.ReadFromJsonAsync<
                CodeGenerationRunPreviewResponse>(cancellationToken))!;
            Assert.IsNotNull(tracked);
            Assert.IsTrue(tracked.Preview.Artifacts.Count > 0);
        }

        await VerifyOrganizationOwnedTrackedPreviewAsync(
            client,
            executor.AccessToken,
            cancellationToken);
        await VerifyHostScopeOrganizationOwnershipRejectedOnRunsPreviewAsync(
            client,
            executor.AccessToken,
            cancellationToken);

        using (var executorCannotRead = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       RunsPath,
                       executor.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Forbidden,
                executorCannotRead.StatusCode);
        }

        await VerifyApplyAsync(
            client,
            reader,
            executor,
            applier,
            roller,
            reviewer,
            tracked.RunId,
            applyWorkspaceRoot,
            cancellationToken);

        using (var get = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       $"{RunsPath}/{tracked.RunId:D}",
                       reader.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
            var run = await get.Content.ReadFromJsonAsync<
                CodeGenerationRunResponse>(cancellationToken);
            Assert.IsNotNull(run);
            Assert.AreEqual(executor.UserId, run.RequestedByUserId);
            Assert.AreEqual(CodeGenerationRunStatuses.Succeeded, run.Status);
            Assert.AreEqual(tracked.Preview.Artifacts.Count, run.ArtifactCount);
        }

        using (var list = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       $"{RunsPath}?page=1&pageSize=100&status=succeeded",
                       reader.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
            var body = await list.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            var row = json.RootElement
                .GetProperty("items")
                .EnumerateArray()
                .Single(item =>
                    item.GetProperty("id").GetGuid() == tracked.RunId);
            Assert.IsFalse(row.TryGetProperty("schema", out _));
            Assert.IsFalse(row.TryGetProperty("preview", out _));
            Assert.IsFalse(row.TryGetProperty("content", out _));
            Assert.IsFalse(row.TryGetProperty("errorMessage", out _));
        }

        using (var invalid = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/preview",
                       executor.AccessToken,
                       new CodeGenerationRunPreviewRequest(null, null, null)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.AreEqual(
                CodeGenerationRunErrorCodes.InvalidSource,
                await ReadCodeAsync(invalid, cancellationToken));
        }

        using var failedList = await client.SendAsync(
            Authorized(
                HttpMethod.Get,
                $"{RunsPath}?status=failed",
                reader.AccessToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, failedList.StatusCode);
        var failedPage = await failedList.Content.ReadFromJsonAsync<
            PagedResult<CodeGenerationRunResponse>>(cancellationToken);
        Assert.IsNotNull(failedPage);
        Assert.IsTrue(failedPage.Items.Any(run =>
            run.Status == CodeGenerationRunStatuses.Failed
            && run.ErrorCode == CodeGenerationRunErrorCodes.InvalidSource));
        await OpenApiCodeGenerationRunsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyOrganizationOwnedTrackedPreviewAsync(
        HttpClient client,
        string executorAccessToken,
        CancellationToken cancellationToken)
    {
        using var execute = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"{RunsPath}/preview",
                executorAccessToken,
                new CodeGenerationRunPreviewRequest(
                    null,
                    null,
                    CodeGenerationOrganizationOwnedTestSupport.CreatePreviewRequest())),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, execute.StatusCode);
        var tracked = (await execute.Content.ReadFromJsonAsync<
            CodeGenerationRunPreviewResponse>(cancellationToken))!;
        Assert.IsNotNull(tracked);
        var feature = tracked.Preview.Artifacts
            .Single(artifact => artifact.Path == "backend/ProductFeature.g.cs")
            .Content;
        StringAssert.Contains(feature, "IOrganizationOwnedEntityWriteAuthorizer");
        StringAssert.Contains(feature, "BuildOrganizationUnitFilter");
    }

    private static async Task VerifyHostScopeOrganizationOwnershipRejectedOnRunsPreviewAsync(
        HttpClient client,
        string executorAccessToken,
        CancellationToken cancellationToken)
    {
        foreach (var dataScope in new[] { "host.only", "global" })
        {
            var organizationOwned =
                CodeGenerationOrganizationOwnedTestSupport.CreatePreviewRequest();
            var invalid = organizationOwned with
            {
                DataScope = dataScope,
                Columns = organizationOwned.Columns
                    .Where(column =>
                        !string.Equals(
                            column.ClrPropertyName,
                            "TenantId",
                            StringComparison.Ordinal))
                    .ToArray(),
            };
            using var response = await client.SendAsync(
                AuthorizedJson(
                    HttpMethod.Post,
                    $"{RunsPath}/preview",
                    executorAccessToken,
                    new CodeGenerationRunPreviewRequest(
                        null,
                        null,
                        invalid)),
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual(
                CodeGenerationErrorCodes.InvalidPreviewSchema,
                await ReadCodeAsync(response, cancellationToken));
        }
    }

    private static async Task VerifyApplyAsync(
        HttpClient client,
        HostTestIdentity reader,
        HostTestIdentity executor,
        HostTestIdentity applier,
        HostTestIdentity roller,
        HostTestIdentity reviewer,
        Guid inlinePreviewRunId,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        CodeGenerationTemplateResponse template;
        using (var createTemplate = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       "/api/v1/code-generation/templates/",
                       reviewer.AccessToken,
                       new CreateCodeGenerationTemplateRequest(
                           $"Apply contract {Guid.NewGuid():N}",
                           null,
                           CreateSchema())),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Created, createTemplate.StatusCode);
            template = (await createTemplate.Content.ReadFromJsonAsync<
                CodeGenerationTemplateResponse>(cancellationToken))!;
            Assert.IsNotNull(template);
        }

        CodeGenerationRunPreviewResponse reviewedPreview;
        using (var preview = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/preview",
                       reviewer.AccessToken,
                       new CodeGenerationRunPreviewRequest(
                           template.Id,
                           template.Version,
                           null)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
            reviewedPreview = (await preview.Content.ReadFromJsonAsync<
                CodeGenerationRunPreviewResponse>(cancellationToken))!;
            Assert.IsNotNull(reviewedPreview);
        }

        using (var executorCannotApply = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/apply",
                       executor.AccessToken,
                       new CodeGenerationRunApplyRequest(
                           reviewedPreview.RunId)),
                   cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Forbidden,
                executorCannotApply.StatusCode);
        }

        using (var applierCannotPreview = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/preview",
                       applier.AccessToken,
                       new CodeGenerationRunPreviewRequest(
                           template.Id,
                           template.Version,
                           null)),
                   cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Forbidden,
                applierCannotPreview.StatusCode);
        }

        CodeGenerationRunApplyResponse applied;
        string applyBody;
        using (var apply = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/apply",
                       applier.AccessToken,
                       new CodeGenerationRunApplyRequest(
                           reviewedPreview.RunId)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, apply.StatusCode);
            applyBody = await apply.Content.ReadAsStringAsync(cancellationToken);
            applied = JsonSerializer.Deserialize<CodeGenerationRunApplyResponse>(
                applyBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.IsNotNull(applied);
            Assert.AreEqual(reviewedPreview.RunId, applied.PreviewRunId);
            Assert.AreEqual(
                reviewedPreview.Preview.Artifacts.Count,
                applied.ArtifactCount);
            Assert.IsTrue(applied.ChangedArtifactCount > 0);
            Assert.AreEqual(64, applied.ManifestSha256.Length);
        }

        Assert.IsFalse(
            applyBody.Contains(workspaceRoot, StringComparison.OrdinalIgnoreCase),
            "Apply 响应不得暴露服务器工作区路径。");
        var manifestPath = Path.Combine(
            workspaceRoot,
            GenerationWorkspaceStore.ManifestRelativePath);
        Assert.IsTrue(File.Exists(manifestPath));
        var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(
            workspaceRoot,
            applied.RunId,
            cancellationToken);
        Assert.AreEqual(applied.RunId, checkpoint.ApplyRunId);
        Assert.IsNull(checkpoint.PreviousManifest);
        Assert.HasCount(0, checkpoint.PreviousContents);
        Assert.AreEqual(
            await File.ReadAllTextAsync(manifestPath, cancellationToken),
            checkpoint.AppliedManifest.ToJson());
        Assert.AreEqual(
            applied.ArtifactCount + 2,
            Directory.EnumerateFiles(
                    workspaceRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Count());

        using (var getApply = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       $"{RunsPath}/{applied.RunId:D}",
                       reader.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, getApply.StatusCode);
            var run = await getApply.Content.ReadFromJsonAsync<
                CodeGenerationRunResponse>(cancellationToken);
            Assert.IsNotNull(run);
            Assert.AreEqual(
                CodeGenerationRunOperationKinds.Apply,
                run.OperationKind);
            Assert.AreEqual(CodeGenerationRunStatuses.Succeeded, run.Status);
            Assert.AreEqual(template.Id, run.TemplateId);
            Assert.AreEqual(template.Version, run.TemplateVersion);
            Assert.AreEqual(applier.UserId, run.RequestedByUserId);
            Assert.AreEqual(applied.ArtifactCount, run.ArtifactCount);
            Assert.AreEqual(applied.ManifestSha256, run.ManifestSha256);
            Assert.IsNull(run.SourceApplyRunId);
            Assert.IsNull(run.ErrorCode);
        }

        using var invalid = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"{RunsPath}/apply",
                applier.AccessToken,
                new CodeGenerationRunApplyRequest(inlinePreviewRunId)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadAsStringAsync(
            cancellationToken);
        using var invalidJson = JsonDocument.Parse(invalidBody);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.InvalidApplyPreview,
            invalidJson.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(
            invalidBody.Contains(workspaceRoot, StringComparison.OrdinalIgnoreCase),
            "Apply 错误不得暴露服务器工作区路径。");

        await VerifyRollbackChainAsync(
            client,
            reader,
            applier,
            roller,
            reviewer,
            template.Id,
            template.Version,
            applied,
            inlinePreviewRunId,
            workspaceRoot,
            cancellationToken);
    }

    private static async Task VerifyRollbackChainAsync(
        HttpClient client,
        HostTestIdentity reader,
        HostTestIdentity applier,
        HostTestIdentity roller,
        HostTestIdentity reviewer,
        Guid templateId,
        long templateVersion,
        CodeGenerationRunApplyResponse firstApplied,
        Guid invalidApplyRunId,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        using (var updateTemplate = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Put,
                       $"/api/v1/code-generation/templates/{templateId:D}",
                       reviewer.AccessToken,
                       new UpdateCodeGenerationTemplateRequest(
                           $"Apply contract second {Guid.NewGuid():N}",
                           null,
                           CreateUpdatedSchema(),
                           templateVersion)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, updateTemplate.StatusCode);
            var updated = await updateTemplate.Content.ReadFromJsonAsync<
                CodeGenerationTemplateResponse>(cancellationToken);
            Assert.IsNotNull(updated);
            templateVersion = updated.Version;
        }

        CodeGenerationRunPreviewResponse secondPreview;
        using (var preview = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/preview",
                       reviewer.AccessToken,
                       new CodeGenerationRunPreviewRequest(
                           templateId,
                           templateVersion,
                           null)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
            secondPreview = (await preview.Content.ReadFromJsonAsync<
                CodeGenerationRunPreviewResponse>(cancellationToken))!;
            Assert.IsNotNull(secondPreview);
        }

        CodeGenerationRunApplyResponse secondApplied;
        using (var apply = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/apply",
                       applier.AccessToken,
                       new CodeGenerationRunApplyRequest(
                           secondPreview.RunId)),
                   cancellationToken))
        {
            var applyBody = await apply.Content.ReadAsStringAsync(cancellationToken);
            Assert.AreEqual(
                HttpStatusCode.OK,
                apply.StatusCode,
                applyBody);
            secondApplied = JsonSerializer.Deserialize<
                CodeGenerationRunApplyResponse>(
                applyBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.IsNotNull(secondApplied);
        }

        using (var applierCannotRollbackChain = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/rollback-chain",
                       applier.AccessToken,
                       new CodeGenerationRunRollbackChainRequest(
                           [secondApplied.RunId, firstApplied.RunId])),
                   cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Forbidden,
                applierCannotRollbackChain.StatusCode);
        }

        using (var invalidOrder = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/rollback-chain",
                       roller.AccessToken,
                       new CodeGenerationRunRollbackChainRequest(
                           [firstApplied.RunId, secondApplied.RunId])),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalidOrder.StatusCode);
            Assert.AreEqual(
                CodeGenerationRunErrorCodes.InvalidRollbackChain,
                await ReadCodeAsync(invalidOrder, cancellationToken));
        }

        CodeGenerationRunRollbackChainResponse rolledBackChain;
        string rollbackChainBody;
        using (var rollbackChain = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/rollback-chain",
                       roller.AccessToken,
                       new CodeGenerationRunRollbackChainRequest(
                           [secondApplied.RunId, firstApplied.RunId])),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, rollbackChain.StatusCode);
            rollbackChainBody = await rollbackChain.Content.ReadAsStringAsync(
                cancellationToken);
            rolledBackChain = JsonSerializer.Deserialize<
                CodeGenerationRunRollbackChainResponse>(
                rollbackChainBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.IsNotNull(rolledBackChain);
            Assert.HasCount(2, rolledBackChain.Rollbacks);
            Assert.AreEqual(secondApplied.RunId, rolledBackChain.Rollbacks[0].ApplyRunId);
            Assert.AreEqual(firstApplied.RunId, rolledBackChain.Rollbacks[1].ApplyRunId);
        }

        Assert.IsFalse(
            rollbackChainBody.Contains(
                workspaceRoot,
                StringComparison.OrdinalIgnoreCase),
            "Rollback chain 响应不得暴露服务器工作区路径。");
        var manifestPath = Path.Combine(
            workspaceRoot,
            GenerationWorkspaceStore.ManifestRelativePath);
        Assert.AreEqual(
            GenerationManifest.Create([]).ToJson(),
            await File.ReadAllTextAsync(manifestPath, cancellationToken));

        using (var invalid = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/rollback",
                       roller.AccessToken,
                       new CodeGenerationRunRollbackRequest(invalidApplyRunId)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.AreEqual(
                CodeGenerationRunErrorCodes.InvalidRollbackApply,
                await ReadCodeAsync(invalid, cancellationToken));
        }

        using (var duplicateFirst = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/rollback",
                       roller.AccessToken,
                       new CodeGenerationRunRollbackRequest(firstApplied.RunId)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, duplicateFirst.StatusCode);
            var replay = JsonSerializer.Deserialize<
                CodeGenerationRunRollbackResponse>(
                await duplicateFirst.Content.ReadAsStringAsync(cancellationToken),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.IsNotNull(replay);
            Assert.AreEqual(firstApplied.RunId, replay.ApplyRunId);
            Assert.AreEqual(0, replay.ChangedArtifactCount);
        }

        using (var duplicateSecond = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{RunsPath}/rollback",
                       roller.AccessToken,
                       new CodeGenerationRunRollbackRequest(secondApplied.RunId)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, duplicateSecond.StatusCode);
            Assert.AreEqual(
                CodeGenerationRunErrorCodes.RollbackConflict,
                await ReadCodeAsync(duplicateSecond, cancellationToken));
        }
    }

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson<T>(
        HttpMethod method,
        string path,
        string token,
        T body)
    {
        var request = Authorized(method, path, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<string?> ReadCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("code").GetString();
    }

    private static CodeGenerationPreviewRequest CreateSchema() =>
        new(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "HostOnly",
            true,
            [
                new("Id", "Id", "id", "Uuid", false, null, null, null),
                new(
                    "Name",
                    "Name",
                    "displayName",
                    "String",
                    false,
                    200,
                    null,
                    null),
                new(
                    "IsActive",
                    "IsActive",
                    "isActive",
                    "Boolean",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Version",
                    "Version",
                    "version",
                    "Int64",
                    false,
                    null,
                    null,
                    null),
            ]);

    private static CodeGenerationPreviewRequest CreateUpdatedSchema() =>
        new(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "HostOnly",
            true,
            [
                new("Id", "Id", "id", "Uuid", false, null, null, null),
                new(
                    "Name",
                    "Name",
                    "displayName",
                    "String",
                    false,
                    200,
                    null,
                    null),
                new(
                    "IsActive",
                    "IsActive",
                    "isActive",
                    "Boolean",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Version",
                    "Version",
                    "version",
                    "Int64",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Remark",
                    "Remark",
                    "remark",
                    "String",
                    true,
                    500,
                    null,
                    null),
            ]);
}

/// <summary>
/// 为真实 Apply API 创建测试独占工作区，避免任何集成场景写入仓库根目录。
/// </summary>
internal sealed class CodeGenerationApplyTestWorkspace : IDisposable
{
    private CodeGenerationApplyTestWorkspace(string rootPath)
    {
        RootPath = rootPath;
        Settings = new Dictionary<string, string?>
        {
            ["CodeGeneration:Apply:Enabled"] = "true",
            ["CodeGeneration:Apply:WorkspaceRoot"] = rootPath,
        };
    }

    public string RootPath { get; }

    public IReadOnlyDictionary<string, string?> Settings { get; }

    public static CodeGenerationApplyTestWorkspace Create() =>
        new(Directory.CreateTempSubdirectory(
            "fullnet-codegeneration-api-apply-").FullName);

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
