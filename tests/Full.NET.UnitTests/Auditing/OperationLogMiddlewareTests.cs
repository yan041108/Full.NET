using System.Security.Claims;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Middleware;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class OperationLogMiddlewareTests
{
    [TestMethod]
    public async Task Permission_code_comes_from_endpoint_policy_not_first_user_claim()
    {
        var model = await CaptureAsync(
            ["unrelated.permission", "orders.update"],
            ["orders.update"]);

        Assert.AreEqual("orders.update", model.PermissionCode);
        CollectionAssert.AreEqual(
            new[] { "orders.update" },
            model.RequiredPermissions.ToArray());
    }

    [TestMethod]
    public async Task Multiple_required_permissions_are_not_reported_as_one_matched_claim()
    {
        var model = await CaptureAsync(
            ["unrelated.permission", "orders.update", "orders.approve"],
            ["orders.update", "orders.approve"]);

        Assert.IsNull(model.PermissionCode);
        CollectionAssert.AreEqual(
            new[] { "orders.update", "orders.approve" },
            model.RequiredPermissions.ToArray());
    }

    [TestMethod]
    public async Task Endpoint_without_permission_does_not_report_an_unrelated_claim()
    {
        var model = await CaptureAsync(["unrelated.permission"], []);

        Assert.IsNull(model.PermissionCode);
        Assert.IsEmpty(model.RequiredPermissions);
    }

    [TestMethod]
    public async Task Successful_mutation_keeps_success_status()
    {
        var model = await CaptureAsync(["orders.update"], ["orders.update"]);

        Assert.AreEqual(StatusCodes.Status200OK, model.StatusCode);
        Assert.IsTrue(model.Succeeded);
    }

    [TestMethod]
    public async Task Open_access_endpoint_permission_is_captured()
    {
        var model = await CaptureAsync(
            [],
            ["orders.create"],
            openAccess: true);

        Assert.AreEqual("orders.create", model.PermissionCode);
        CollectionAssert.AreEqual(
            new[] { "orders.create" },
            model.RequiredPermissions.ToArray());
    }

    private static async Task<OperationLogWriteModel> CaptureAsync(
        string[] grantedPermissions,
        string[] requiredPermissions,
        bool openAccess = false)
    {
        var buffer = new AuditWriteBuffer();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/orders";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            grantedPermissions.Select(code =>
                new Claim(FullNetIdentityClaimTypes.Permission, code)),
            authenticationType: "test"));
        var metadata = requiredPermissions
            .Select(code => (object)new AuthorizeAttribute
            {
                Policy = openAccess
                    ? $"FullNet.OpenAccess:{code}"
                    : FullNetPermissionPolicies.For(code),
            })
            .ToArray();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "orders"));

        var middleware = new OperationLogMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, new OperationLogWriter(buffer));

        return buffer.Snapshot().Operation
            ?? throw new AssertFailedException("Operation log was not captured.");
    }
}
