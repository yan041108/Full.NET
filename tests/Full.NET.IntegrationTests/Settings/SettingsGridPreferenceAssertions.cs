using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.IntegrationTests.Settings;

/// <summary>当前用户 Grid 偏好双库纵向切片验收。</summary>
internal static class SettingsGridPreferenceAssertions
{
    private const string GridKey = "identity.users";
    private const string Path = "/api/v1/me/grid-preferences/identity.users";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var first = await factory.CreateHostIdentityAsync(
            $"grid-a-{Guid.NewGuid():N}",
            [],
            cancellationToken);
        var second = await factory.CreateHostIdentityAsync(
            $"grid-b-{Guid.NewGuid():N}",
            [],
            cancellationToken);

        await VerifyCatalogValidationAsync(client, first.AccessToken, cancellationToken);
        var created = await VerifyPerUserIsolationAsync(
            client,
            first.AccessToken,
            second.AccessToken,
            cancellationToken);
        await VerifyOptimisticConflictAndCacheInvalidationAsync(
            client,
            first.AccessToken,
            created,
            cancellationToken);
        await VerifyConcurrentCreateAsync(
            client,
            factory,
            cancellationToken);
        await VerifyIdempotentResetAsync(client, first.AccessToken, cancellationToken);
        await OpenApiSettingsGridPreferencesContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyCatalogValidationAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var unknownGrid = Authorized(
            HttpMethod.Get,
            "/api/v1/me/grid-preferences/identity.remote-script",
            token);
        using var unknownGridResponse = await client.SendAsync(
            unknownGrid,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, unknownGridResponse.StatusCode);

        using var unknownColumn = AuthorizedJson(
            HttpMethod.Put,
            Path,
            token,
            new UpdateGridPreferenceRequest(
                1,
                [new GridColumnPreference("remoteScript", 0, 120, true, null)],
                0));
        using var unknownColumnResponse = await client.SendAsync(
            unknownColumn,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownColumnResponse.StatusCode);
        Assert.AreEqual(
            SettingsErrorCodes.GridColumnUnknown,
            await ReadCodeAsync(unknownColumnResponse, cancellationToken));

        using var duplicateColumn = AuthorizedJson(
            HttpMethod.Put,
            Path,
            token,
            new UpdateGridPreferenceRequest(
                1,
                [
                    new GridColumnPreference("username", 0, 120, true, null),
                    new GridColumnPreference("username", 1, 180, false, "left"),
                ],
                0));
        using var duplicateResponse = await client.SendAsync(
            duplicateColumn,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        Assert.AreEqual(
            SettingsErrorCodes.GridColumnDuplicate,
            await ReadCodeAsync(duplicateResponse, cancellationToken));

        using var staleSchema = AuthorizedJson(
            HttpMethod.Put,
            Path,
            token,
            new UpdateGridPreferenceRequest(0, [], 0));
        using var staleSchemaResponse = await client.SendAsync(
            staleSchema,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleSchemaResponse.StatusCode);
        Assert.AreEqual(
            SettingsErrorCodes.GridSchemaVersionMismatch,
            await ReadCodeAsync(staleSchemaResponse, cancellationToken));
    }

    private static async Task<GridPreferenceResponse> VerifyPerUserIsolationAsync(
        HttpClient client,
        string firstToken,
        string secondToken,
        CancellationToken cancellationToken)
    {
        using var put = AuthorizedJson(
            HttpMethod.Put,
            Path,
            firstToken,
            new UpdateGridPreferenceRequest(
                1,
                [
                    new GridColumnPreference("status", 1, 140, false, "right"),
                    new GridColumnPreference("username", 0, 240, true, "left"),
                ],
                0));
        using var putResponse = await client.SendAsync(put, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, putResponse.StatusCode);
        var created = await putResponse.Content.ReadFromJsonAsync<GridPreferenceResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(1, created.Version);
        CollectionAssert.AreEqual(
            new[] { "username", "status" },
            created.Columns.Select(column => column.ColumnKey).ToArray());

        using var secondGet = Authorized(HttpMethod.Get, Path, secondToken);
        using var secondResponse = await client.SendAsync(secondGet, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondResponse.StatusCode);
        var isolated = await secondResponse.Content.ReadFromJsonAsync<GridPreferenceResponse>(
            cancellationToken);
        Assert.IsNotNull(isolated);
        Assert.AreEqual(GridKey, isolated.GridKey);
        Assert.AreEqual(0, isolated.Version);
        Assert.IsEmpty(isolated.Columns);
        return created;
    }

    private static async Task VerifyOptimisticConflictAndCacheInvalidationAsync(
        HttpClient client,
        string token,
        GridPreferenceResponse created,
        CancellationToken cancellationToken)
    {
        using var primeCache = Authorized(HttpMethod.Get, Path, token);
        using var primeResponse = await client.SendAsync(primeCache, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, primeResponse.StatusCode);

        var updateBody = new UpdateGridPreferenceRequest(
            1,
            [new GridColumnPreference("displayName", 0, 320, false, null)],
            created.Version);
        using var update = AuthorizedJson(HttpMethod.Put, Path, token, updateBody);
        using var updateResponse = await client.SendAsync(update, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<GridPreferenceResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual(created.Version + 1, updated.Version);

        using var refreshedGet = Authorized(HttpMethod.Get, Path, token);
        using var refreshedResponse = await client.SendAsync(
            refreshedGet,
            cancellationToken);
        var refreshed = await refreshedResponse.Content
            .ReadFromJsonAsync<GridPreferenceResponse>(cancellationToken);
        Assert.IsNotNull(refreshed);
        Assert.AreEqual(updated.Version, refreshed.Version);
        Assert.AreEqual(320, refreshed.Columns.Single().Width);

        using var staleUpdate = AuthorizedJson(
            HttpMethod.Put,
            Path,
            token,
            updateBody);
        using var staleResponse = await client.SendAsync(
            staleUpdate,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.AreEqual(
            SettingsErrorCodes.GridPreferenceVersionConflict,
            await ReadCodeAsync(staleResponse, cancellationToken));
    }

    private static async Task VerifyIdempotentResetAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var reset = Authorized(HttpMethod.Delete, Path, token);
            using var response = await client.SendAsync(reset, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var resetPreference = await response.Content
                .ReadFromJsonAsync<GridPreferenceResponse>(cancellationToken);
            Assert.IsNotNull(resetPreference);
            Assert.AreEqual(0, resetPreference.Version);
            Assert.IsEmpty(resetPreference.Columns);
        }
    }

    private static async Task VerifyConcurrentCreateAsync(
        HttpClient client,
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var identity = await factory.CreateHostIdentityAsync(
            $"grid-race-{Guid.NewGuid():N}",
            [],
            cancellationToken);
        var body = new UpdateGridPreferenceRequest(
            1,
            [new GridColumnPreference("username", 0, 180, true, null)],
            0);
        var requests = Enumerable.Range(0, 8)
            .Select(_ => AuthorizedJson(
                HttpMethod.Put,
                Path,
                identity.AccessToken,
                body))
            .ToArray();
        try
        {
            var responses = await Task.WhenAll(
                requests.Select(request => client.SendAsync(
                    request,
                    cancellationToken)));
            try
            {
                Assert.AreEqual(
                    1,
                    responses.Count(response =>
                        response.StatusCode == HttpStatusCode.OK));
                Assert.AreEqual(
                    responses.Length - 1,
                    responses.Count(response =>
                        response.StatusCode == HttpStatusCode.Conflict));
                foreach (var response in responses.Where(candidate =>
                             candidate.StatusCode == HttpStatusCode.Conflict))
                {
                    Assert.AreEqual(
                        SettingsErrorCodes.GridPreferenceVersionConflict,
                        await ReadCodeAsync(response, cancellationToken));
                }
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }
        finally
        {
            foreach (var request in requests)
            {
                request.Dispose();
            }
        }
    }

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
}
