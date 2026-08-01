using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Auditing;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 普通 HTTP Operation Log（B2）与 Access 表解耦的双库验收。
/// </summary>
internal static class HttpRequestLoggingAssertions
{
    public static async Task VerifyAccessTableNotWrittenByHttpAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        const string path = "/api/v1/settings/enum-catalogs";
        var before = await CountAccessRowsAsync(factory, path, cancellationToken);

        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var after = await CountAccessRowsAsync(factory, path, cancellationToken);
        Assert.AreEqual(
            before,
            after,
            "B2 HttpOperationCompleted must not write fn_auditing_access_log.");
    }

    private static async Task<long> CountAccessRowsAsync(
        FullNetApiFactory factory,
        string pathContains,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        return await scope.ServiceProvider
            .GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<long>(
                new SqlStatement(
                    "test.http_operation.count_access_by_path",
                    """
                    SELECT COUNT(*)
                    FROM fn_auditing_access_log
                    WHERE RequestPath LIKE @Pattern
                    """,
                    SqlDataScope.Global),
                new { Pattern = "%" + pathContains + "%" },
                cancellationToken);
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

[TestClass]
public sealed class HttpRequestLoggingApiSqlServerTests
{
    [TestMethod]
    public async Task Http_operation_log_does_not_write_access_table_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await HttpRequestLoggingAssertions.VerifyAccessTableNotWrittenByHttpAsync(factory);
    }
}

[TestClass]
public sealed class HttpRequestLoggingApiMySqlTests
{
    [TestMethod]
    public async Task Http_operation_log_does_not_write_access_table_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await HttpRequestLoggingAssertions.VerifyAccessTableNotWrittenByHttpAsync(factory);
    }
}
