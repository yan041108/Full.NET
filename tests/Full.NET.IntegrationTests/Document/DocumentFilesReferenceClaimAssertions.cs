using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;

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