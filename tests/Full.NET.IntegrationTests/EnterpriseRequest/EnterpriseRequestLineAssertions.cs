using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    internal static void ConfigureLineInsertFailure(IServiceCollection services)
    {
        var factory = services.Last(item => item.ServiceType == typeof(ICommandExecutor)).ImplementationFactory!;
        services.AddSingleton(new LineInsertFailureProbe());
        services.AddScoped<ICommandExecutor>(provider => new LineInsertFailureExecutor((ICommandExecutor)factory(provider), provider.GetRequiredService<LineInsertFailureProbe>()));
    }

    private static async Task VerifyLinesAsync(FullNetApiFactory factory, HttpClient client, string token, EnterpriseRequestResponse parent, CancellationToken ct)
    {
        var path = $"{BasePath}/{parent.Id:D}/lines";
        using var anonymous = await client.GetAsync(path, ct);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var initial = await Send(HttpMethod.Get, null);
        Assert.AreEqual(HttpStatusCode.OK, initial.StatusCode, await initial.Content.ReadAsStringAsync(ct));
        using var initialJson = JsonDocument.Parse(await initial.Content.ReadAsStringAsync(ct));
        Assert.AreEqual(0, initialJson.RootElement.GetProperty("items").GetArrayLength());

        var body = new { version = parent.Version.ToString(), items = new[] {
            new { itemDescription = "First", quantity = "1.0001", unitPrice = "50.00" },
            new { itemDescription = "Second", quantity = "2.5", unitPrice = "3.21" } } };
        using var replaced = await Send(HttpMethod.Put, body);
        Assert.AreEqual(HttpStatusCode.OK, replaced.StatusCode, await replaced.Content.ReadAsStringAsync(ct));
        using var json = JsonDocument.Parse(await replaced.Content.ReadAsStringAsync(ct));
        Assert.AreEqual(parent.Version + 1, long.Parse(json.RootElement.GetProperty("requestVersion").GetString()!));
        Assert.AreEqual("58.04", json.RootElement.GetProperty("totalAmount").GetString());
        Assert.AreEqual("50.01", json.RootElement.GetProperty("items")[0].GetProperty("lineAmount").GetString());
        Assert.AreEqual(2, json.RootElement.GetProperty("items")[1].GetProperty("lineNumber").GetInt32());
        factory.Services.GetRequiredService<LineInsertFailureProbe>().Enabled = true;
        using var failedInsert = await Send(HttpMethod.Put, new { version = (parent.Version + 1).ToString(), items = body.items });
        Assert.AreEqual(HttpStatusCode.InternalServerError, failedInsert.StatusCode);
        using var stale = await Send(HttpMethod.Put, new { version = parent.Version.ToString(), items = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode, await stale.Content.ReadAsStringAsync(ct));
        using var invalid = await Send(HttpMethod.Put, new { version = (parent.Version + 1).ToString(), items = new[] {
            new { itemDescription = "Invalid precision", quantity = "0.00001", unitPrice = "1" } } });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var header = new HttpRequestMessage(HttpMethod.Put, $"{BasePath}/{parent.Id:D}") {
            Content = JsonContent.Create(new UpdateEnterpriseRequestRequest(parent.RequestNumber, parent.Title, "Draft", 1m, parent.ApplicantUserId, parent.Version + 1)) };
        header.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var changed = await client.SendAsync(header, ct);
        Assert.AreEqual(HttpStatusCode.BadRequest, changed.StatusCode, "有明细时不得通过主表编辑破坏合计。");
        using var after = await Send(HttpMethod.Get, null);
        Assert.AreEqual(HttpStatusCode.OK, after.StatusCode);
        using var finalJson = JsonDocument.Parse(await after.Content.ReadAsStringAsync(ct));
        Assert.AreEqual(2, finalJson.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual("58.04", finalJson.RootElement.GetProperty("totalAmount").GetString());
        Assert.AreEqual(json.RootElement.GetProperty("requestVersion").GetString(), finalJson.RootElement.GetProperty("requestVersion").GetString());
        Assert.AreEqual(json.RootElement.GetProperty("items")[0].GetProperty("id").GetString(), finalJson.RootElement.GetProperty("items")[0].GetProperty("id").GetString(),
            "插入失败必须恢复原明细身份，不只是恢复行数与金额。");
        using var cleared = await Send(HttpMethod.Put, new { version = (parent.Version + 1).ToString(), items = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.OK, cleared.StatusCode);
        using var clearedJson = JsonDocument.Parse(await cleared.Content.ReadAsStringAsync(ct));
        Assert.AreEqual("0", clearedJson.RootElement.GetProperty("totalAmount").GetString());
        Assert.AreEqual(0, clearedJson.RootElement.GetProperty("items").GetArrayLength());

        async Task<HttpResponseMessage> Send(HttpMethod method, object? body)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request, ct);
        }
    }

    private sealed class LineInsertFailureProbe { internal bool Enabled; }
    private sealed class LineInsertFailureExecutor(ICommandExecutor inner, LineInsertFailureProbe probe) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var result = await inner.ExecuteAsync(statement, parameters, cancellationToken);
            // 真实 INSERT 完成后才抛错，覆盖父表更新、清空旧行与已写入新行的整组回滚。
            if (statement.Name == "enterprise_request.replace_lines_insert" && probe.Enabled)
            { probe.Enabled = false; throw new InvalidOperationException("Injected request line failure."); }
            return result;
        }
    }
}
