using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

/// <summary>外部聚合接口及模块内部明细服务均不能绕过审批状态。</summary>
internal static class EnterpriseRequestDraftMutationAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        foreach (var (operation, permission) in new[]
        {
            ("enterpriseRequestGetEnterpriseRequestAggregate", EnterpriseRequestPermissions.Read),
            ("enterpriseRequestUpdateEnterpriseRequestAggregate", EnterpriseRequestPermissions.Update),
        })
        {
            var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.Single(item =>
                item.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == operation);
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(item => item.Policy).ToArray();
            CollectionAssert.Contains(policies, FullNetPermissionPolicies.For(permission));
            CollectionAssert.Contains(policies, FullNetPermissionPolicies.For(EnterpriseRequestDetailPermissions.Read));
        }
        var token = await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(client, token, default);
        var unit = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(client, token, default);
        var actor = await EnterpriseRequestAssertions.GetCurrentUserIdAsync(client, token, default);
        const string heads = "/api/v1/enterprise_request/enterprise-requests";
        CreateEnterpriseRequestRequest Create(string status) => new($"REQ-{Guid.NewGuid():N}"[..24], "Draft guard", status, 1m, actor);

        async Task<JsonElement> Send(HttpMethod method, string path, object? body, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unit.ToString("D"));
            if (body is not null) request.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(expected, response.StatusCode, text);
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        async Task Reject(HttpMethod method, string path, object body, string code = "enterprise_request.enterprise_requests.invalid_status")
        {
            var result = await Send(method, path, body, HttpStatusCode.Conflict);
            Assert.AreEqual(code, result.GetProperty("code").GetString());
        }

        await Reject(HttpMethod.Post, heads, Create("Approved"));
        await Reject(HttpMethod.Post, heads + "/aggregate", new { master = Create("Approved"), details = Array.Empty<object>() });
        var aggregate = await Send(HttpMethod.Post, heads + "/aggregate", new CreateEnterpriseRequestAggregateRequest(
            Create("Draft"), [new(1, "Item", 1m, 1m, 1m)]), HttpStatusCode.OK);
        var draft = aggregate.GetProperty("master");
        var id = draft.GetProperty("id").GetGuid();
        var row = aggregate.GetProperty("details")[0];
        var rowId = row.GetProperty("id").GetGuid();
        var version = long.Parse(draft.GetProperty("version").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var rowVersion = long.Parse(row.GetProperty("version").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        await Reject(HttpMethod.Put, $"{heads}/{id}", new UpdateEnterpriseRequestRequest("REQ", "Title", "Approved", 1m, actor, version));
        await using var scope = factory.Services.CreateAsyncScope();
        var tenantWriter = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var resolver = scope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>();
        tenantWriter.SetTenant((await resolver.ResolveActiveByIdAsync(draft.GetProperty("tenantId").GetGuid()))!);
        try
        {
        var details = scope.ServiceProvider.GetRequiredService<EnterpriseRequestDetailManagementService>();
        var moved = await details.UpdateAsync(rowId, new(Guid.CreateVersion7(), 1, "Moved", 1m, 1m, 1m, rowVersion), actor);
        Assert.AreEqual("enterprise_request.enterprise_request_lines.parent_immutable", moved.Error!.Code);
        var submitted = await Send(HttpMethod.Post, $"{heads}/{id}/submit-for-approval", null, HttpStatusCode.OK);
        Assert.AreEqual("Submitted", submitted.GetProperty("status").GetString());
        version = long.Parse(submitted.GetProperty("version").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var update = new UpdateEnterpriseRequestRequest("REQ", "Changed", "Draft", 1m, actor, version);
        await Reject(HttpMethod.Put, $"{heads}/{id}", update);
        await Reject(HttpMethod.Post, $"{heads}/{id}/delete", new DeleteEnterpriseRequestRequest(version));
        var createdLate = await details.CreateAsync(new(id, 2, "Late", 1m, 1m, 1m), actor, unit);
        var updatedLate = await details.UpdateAsync(rowId, new(id, 1, "Late", 1m, 1m, 1m, rowVersion), actor);
        var deletedLate = await details.DeleteAsync(rowId, new(rowVersion), actor);
        foreach (var rejected in new[] { createdLate, updatedLate, deletedLate })
            Assert.AreEqual("enterprise_request.enterprise_requests.invalid_status", rejected.Error!.Code);
        await Reject(HttpMethod.Put, $"{heads}/{id}/aggregate", new { master = update, added = Array.Empty<object>(), updated = Array.Empty<object>(), removed = Array.Empty<object>() });
        var unchanged = await Send(HttpMethod.Get, $"{heads}/{id}/aggregate", null, HttpStatusCode.OK);
        Assert.AreEqual("Item", unchanged.GetProperty("details")[0].GetProperty("itemDescription").GetString());
        }
        finally { tenantWriter.Clear(); }
    }
}
