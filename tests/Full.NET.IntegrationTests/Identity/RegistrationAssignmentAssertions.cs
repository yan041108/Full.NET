using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Organization.Features.RegistrationAssignments;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>在同一真实受邀注册旅程中验证角色、Outbox、本地意图、组织恢复及人工关系保护。</summary>
internal static class RegistrationAssignmentAssertions
{
    internal static async Task PrepareAsync(FullNetApiFactory factory, Guid wayId, Guid tenantId, CancellationToken token)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var context = sp.GetRequiredService<CurrentTenantAccessor>(); context.SetHost();
        var queries = sp.GetRequiredService<IQueryExecutor>();
        var commands = sp.GetRequiredService<ICommandExecutor>();
        var way = await queries.QuerySingleOrDefaultAsync<RegistrationWayRecord>(RegistrationWaySql.FindById,
            IdentitySqlParameters.Create(("WayId", wayId)), token);
        await commands.ExecuteAsync(IdentitySql.EnsureRolePermission, IdentitySqlParameters.Create(("RoleId", way!.RoleId),
            ("PermissionCode", "organization.units.read")), token);
        var positionId = Guid.CreateVersion7();
        context.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        await InsertPositionAsync(commands, positionId, token);
        context.SetHost();
        await commands.ExecuteAsync(new SqlStatement("integration.registration_assignment.configure_position", """
            UPDATE fn_identity_user_registration_way SET PositionId=@PositionId,Version=Version+1 WHERE Id=@WayId
            """, SqlDataScope.HostOnly), IdentitySqlParameters.Create(("WayId", wayId), ("PositionId", positionId)), token);
    }

    internal static async Task VerifyAsync(FullNetApiFactory factory, Guid wayId, AcceptTenantInvitationResponse accepted, CancellationToken token)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var context = sp.GetRequiredService<CurrentTenantAccessor>(); context.SetHost();
        var queries = sp.GetRequiredService<IQueryExecutor>();
        var commands = sp.GetRequiredService<ICommandExecutor>();
        var way = await queries.QuerySingleOrDefaultAsync<RegistrationWayRecord>(RegistrationWaySql.FindById,
            IdentitySqlParameters.Create(("WayId", wayId)), token);
        var permissions = sp.GetRequiredService<IPermissionSnapshotReader>();
        var tenantPermissions = await permissions.ReadAsync(accepted.UserId, $"tenant:{accepted.TenantId:N}", accepted.TenantId, token);
        CollectionAssert.Contains(tenantPermissions.Permissions.ToArray(), "organization.units.read");
        var hostPermissions = await permissions.ReadAsync(accepted.UserId, "host", null, token);
        CollectionAssert.DoesNotContain(hostPermissions.Permissions.ToArray(), "organization.units.read");
        var messages = await queries.QueryAsync<ActivationEnvelope>(new SqlStatement("integration.registration_assignment.outbox", """
            SELECT Id,TenantId,Payload,OccurredAtUtc FROM fn_outbox_message WHERE TenantId=@TenantId AND MessageType=@MessageType
            """, SqlDataScope.HostOnly), IdentitySqlParameters.Create(("TenantId", accepted.TenantId),
                ("MessageType", RegistrationMemberIntegrationEventTypes.Activated)), token);
        Assert.AreEqual(1, messages.Count);
        var message = messages.Single();
        var serializer = sp.GetRequiredService<IIntegrationEventSerializer>();
        var value = serializer.Deserialize<RegistrationMemberActivatedIntegrationEvent>(message.Payload);
        Assert.AreEqual(accepted.UserId, value.UserId);
        Assert.AreEqual(accepted.MemberId, value.MemberId);
        Assert.AreEqual(way!.OrganizationUnitId, value.OrganizationUnitId);
        Assert.AreEqual(way.PositionId, value.PositionId);
        Assert.AreEqual(accepted.TenantId, message.TenantId);
        var envelope = new IntegrationEventContext(message.Id, RegistrationMemberIntegrationEventTypes.Activated, 1,
            message.TenantId, null, message.OccurredAtUtc);
        context.SetTenant(new TenantContext(accepted.TenantId, "acme", "Acme"));
        var handler = sp.GetRequiredService<RegistrationMemberActivatedHandler>();
        await handler.HandleAsync(envelope, message.Payload, token);
        await handler.HandleAsync(envelope, message.Payload, token);
        var intent = await FindAsync(queries, value.OperationId, token);
        Assert.AreEqual("Pending", intent.Status);
        var worker = new RegistrationAssignmentWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<IOptions<DatabaseOptions>>(), NullLogger<RegistrationAssignmentWorker>.Instance);
        await worker.RunBatchAsync(token);
        Assert.AreEqual("Completed", (await FindAsync(queries, value.OperationId, token)).Status);
        var unitParameters = OrganizationSqlParameters.Create(("UserId", value.UserId), ("UnitId", value.OrganizationUnitId));
        var unit = await queries.QuerySingleOrDefaultAsync<OrganizationUserUnitRecord>(OrganizationSql.FindUserUnitByTenantUserAndUnit, unitParameters, token);
        Assert.IsNotNull(unit); Assert.IsTrue(unit.IsActive); Assert.IsFalse(unit.IsPrimary);
        var position = await queries.QuerySingleOrDefaultAsync<OrganizationUserPositionRecord>(OrganizationSql.FindUserPositionByTenantUserAndPosition,
            OrganizationSqlParameters.Create(("UserId", value.UserId), ("PositionId", value.PositionId)), token);
        Assert.IsNotNull(position); Assert.IsTrue(position.IsActive); Assert.IsFalse(position.IsPrimary);

        await commands.ExecuteAsync(OrganizationSql.DisableUserUnit, OrganizationSqlParameters.Create(
            ("AssignmentId", unit.Id), ("UpdatedAtUtc", DateTimeOffset.UtcNow)), token);
        await handler.HandleAsync(envelope, message.Payload, token);
        await worker.RunBatchAsync(token);
        Assert.IsFalse((await queries.QuerySingleOrDefaultAsync<OrganizationUserUnitRecord>(OrganizationSql.FindUserUnitByTenantUserAndUnit, unitParameters, token))!.IsActive);

        var stale = value with { OperationId = Guid.CreateVersion7(), MemberVersion = value.MemberVersion + 1 };
        await handler.HandleAsync(envelope with { MessageId = Guid.CreateVersion7() }, serializer.Serialize(stale), token);
        await worker.RunBatchAsync(token);
        Assert.AreEqual("Skipped", (await FindAsync(queries, stale.OperationId, token)).Status);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.HandleAsync(envelope with { TenantId = Guid.CreateVersion7() }, message.Payload, token));

        var retry = value with { OperationId = Guid.CreateVersion7(), PositionId = Guid.CreateVersion7() };
        await handler.HandleAsync(envelope with { MessageId = Guid.CreateVersion7() }, serializer.Serialize(retry), token);
        await worker.RunBatchAsync(token);
        var waiting = await FindAsync(queries, retry.OperationId, token);
        Assert.AreEqual("Pending", waiting.Status);
        Assert.AreEqual("organization.registration_assignment.position_unavailable", waiting.ErrorCode);
        Assert.IsTrue(waiting.DueAtUtc > sp.GetRequiredService<IClock>().UtcNow);
        await InsertPositionAsync(commands, retry.PositionId!.Value, token);
        await commands.ExecuteAsync(new SqlStatement("integration.registration_assignment.retry_due", """
            UPDATE fn_organization_registration_assignment SET DueAtUtc=@Now WHERE TenantId=@TenantId AND Id=@Id
            """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId), OrganizationSqlParameters.Create(
                ("Id", retry.OperationId), ("Now", DateTimeOffset.UtcNow.AddSeconds(-1))), token);
        await worker.RunBatchAsync(token);
        Assert.AreEqual("Completed", (await FindAsync(queries, retry.OperationId, token)).Status);
        Assert.IsFalse((await queries.QuerySingleOrDefaultAsync<OrganizationUserUnitRecord>(OrganizationSql.FindUserUnitByTenantUserAndUnit, unitParameters, token))!.IsActive);
    }

    private static async Task<RegistrationAssignmentRecord> FindAsync(IQueryExecutor queries, Guid id, CancellationToken token) =>
        (await queries.QuerySingleOrDefaultAsync<RegistrationAssignmentRecord>(RegistrationAssignmentSql.Find,
            OrganizationSqlParameters.Create(("Id", id)), token))!;

    private static Task<int> InsertPositionAsync(ICommandExecutor commands, Guid id, CancellationToken token) =>
        commands.ExecuteAsync(PositionSql.Insert, OrganizationSqlParameters.Create(("Id", id), ("Code", $"register-{id:N}"),
            ("Name", "Registration position"), ("DisplayOrder", 0), ("IsActive", true),
            ("CreatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1)), token);

    private sealed class ActivationEnvelope
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public byte[] Payload { get; set; } = [];
        public DateTimeOffset OccurredAtUtc { get; set; }
    }
}
