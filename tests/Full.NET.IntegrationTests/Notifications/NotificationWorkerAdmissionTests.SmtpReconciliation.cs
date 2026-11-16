using System.Data.Common;
using System.Net;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Data;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Notifications;

public sealed partial class NotificationWorkerAdmissionTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Restored_smtp_backlog_reconciles_original_external_acceptance_without_resending(DatabaseProvider provider, bool startTls)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = budget.Token;
        var database = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using var inbox = new ControlledSmtpInbox(startTls);
        using var keyRing = new NotificationRecoveryKeyRing();
        using var sourceKeys = keyRing.OpenSource();
        var sourceProtection = sourceKeys.GetRequiredService<IDataProtectionProvider>();
        var settings = new Dictionary<string, string?> { ["Notifications:Providers:Smtp:Enabled"] = "true" };
        using var api = new FullNetApiFactory(provider, database, settings, configureTestServices: services => Configure(services, sourceProtection));
        await api.InitializeAsync(token, useSchemaTemplate: false);
        using var client = api.CreateClientForHost("localhost");
        var access = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, token);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, access, token);
        var route = await CreateSmtpRouteAsync(api.Services, client, access, user.Id, inbox, token);
        var identity = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, access, route, user.Id, "accepted-after-backup", token);
        var original = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, access, identity.Id, token);
        Assert.AreEqual("accepted", original.StatusKey);

        string? messageId = null;
        var restored = await DatabaseRecoveryBackup.RestoreAsync(provider, database, async () =>
        {
            using var sender = await BuildWorkerAsync(database, "Notifications.SourceAfterBackup", sourceProtection);
            var received = inbox.ReceiveAsync("accepted", token);
            await sender.StartAsync(token);
            try
            {
                await VerifyMailAsync(received, "accepted-after-backup", token);
                messageId = (await received).Message!.MessageId;
                await WaitSentAsync(client, access, original.Id);
            }
            finally { await sender.StopAsync(CancellationToken.None); }
        }, token);
        Assert.IsNotNull(messageId);

        using var restoredKeys = keyRing.Restore(sourceProtection);
        var restoredProtection = restoredKeys.GetRequiredService<IDataProtectionProvider>();
        using var recoveryApi = new FullNetApiFactory(provider, restored, settings, configureTestServices: services => Configure(services, restoredProtection));
        using var recovery = recoveryApi.CreateClientForHost("localhost");
        var recoveryAccess = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(recovery, token);
        var pending = await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(recovery, recoveryAccess, original.Id, token);
        Assert.AreEqual(original.Revision, pending.Revision);
        Assert.AreEqual("accepted", pending.StatusKey);
        Assert.HasCount(0, pending.Attempts);

        const string restoreOutcome = "confirmed_smtp_accepted";
        var readOnly = await recoveryApi.CreateHostAccessTokenAsync([NotificationPlatformPermissions.DeliveriesRead], token);
        await ReconcileAsync(readOnly, pending.Revision, restoreOutcome, messageId, HttpStatusCode.Forbidden);
        await ReconcileAsync(recoveryAccess, pending.Revision, "confirmed_accepted", messageId, HttpStatusCode.Conflict);
        await ReconcileAsync(recoveryAccess, pending.Revision + 1, restoreOutcome, messageId, HttpStatusCode.Conflict);
        await ReconcileAsync(recoveryAccess, pending.Revision, restoreOutcome, "incorrect@local.invalid", HttpStatusCode.Conflict);

        await using DbConnection observer = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(restored)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(restored, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        await observer.OpenAsync(token);
        await SetLeaseAsync("recovery-test-lease", DateTime.UtcNow.AddMinutes(1));
        await ReconcileAsync(recoveryAccess, pending.Revision, restoreOutcome, messageId, HttpStatusCode.Conflict);
        await SetLeaseAsync("recovery-test-lease", DateTime.UtcNow.AddMinutes(-1));
        await ReconcileAsync(recoveryAccess, pending.Revision, restoreOutcome, messageId, HttpStatusCode.OK);
        await ReconcileAsync(recoveryAccess, pending.Revision, restoreOutcome, messageId, HttpStatusCode.Conflict);
        var confirmed = await WaitSentAsync(recovery, recoveryAccess, original.Id);
        Assert.AreEqual(identity.Id, confirmed.IntentId);
        Assert.AreEqual(pending.Revision + 1, confirmed.Revision);
        Assert.HasCount(0, confirmed.Attempts);
        Assert.IsNull(confirmed.NextAttemptAtUtc);
        var audit = await observer.QuerySingleAsync<string>(new CommandDefinition("""
            SELECT DetailJson FROM fn_notifications_domain_audit
            WHERE ResourceId = @Id AND OperationKey = 'delivery.reconcile' AND OutcomeKey = @OutcomeKey AND ScopeKey = 'host'
            """, new { original.Id, OutcomeKey = restoreOutcome }, cancellationToken: token));
        using var detail = System.Text.Json.JsonDocument.Parse(audit);
        Assert.AreEqual(messageId, detail.RootElement.GetProperty("evidenceReference").GetString());

        var next = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(recovery, recoveryAccess, route, user.Id, "queued-after-reconciliation", token);
        var nextDelivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(recovery, recoveryAccess, next.Id, token);
        using var worker = await BuildWorkerAsync(restored, "Notifications.RestoredAfterReconciliation", restoredProtection);
        var nextMail = inbox.ReceiveAsync("accepted", token);
        await worker.StartAsync(token);
        try
        {
            await VerifyMailAsync(nextMail, "queued-after-reconciliation", token);
            await WaitSentAsync(recovery, recoveryAccess, nextDelivery.Id);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
        Assert.IsFalse(inbox.HasPendingConnection);

        async Task SetLeaseAsync(string owner, DateTime expires)
        {
            Assert.AreEqual(1, await observer.ExecuteAsync(new CommandDefinition("""
                UPDATE fn_notifications_delivery SET LeaseOwnerKey = @Owner, LeaseExpiresAtUtc = @Expires WHERE Id = @Id
                """, new { original.Id, Owner = owner, Expires = expires }, cancellationToken: token)));
        }

        async Task ReconcileAsync(string authorization, long revision, string outcome, string evidence, HttpStatusCode expected)
        {
            using var request = NotificationProfileBindingAssertions.CreateBearerJsonRequest(HttpMethod.Post,
                $"/api/v1/notifications/deliveries/{original.Id:D}/reconcile", authorization,
                new ReconcileNotificationDeliveryRequest(revision, outcome, evidence));
            using var response = await recovery.SendAsync(request, token);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync(token));
        }

        Task<IHost> BuildWorkerAsync(string connection, string name, IDataProtectionProvider protection) =>
            IntegrationWorkerHostFactory.BuildAsync(provider, connection, settings, name, nameof(NotificationDeliveryHostedProcessor),
                configureTargetServices: services => Configure(services, protection));

        void Configure(IServiceCollection services, IDataProtectionProvider protection)
        {
            ConfigureWorker(services);
            services.AddSingleton(protection);
            services.RemoveAll<ISmtpMailTransport>();
            services.AddSingleton<ISmtpMailTransport>(inbox.CreateTransport());
            services.RemoveAll<INotificationSecretResolver>();
            services.AddSingleton<INotificationSecretResolver>(new ControlledSmtpInbox.ProtocolSecretResolver());
        }

        async Task<NotificationDeliveryResponse> WaitSentAsync(HttpClient caller, string authorization, Guid id)
        {
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            window.CancelAfter(TimeSpan.FromSeconds(20));
            while (true)
            {
                var delivery = await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(caller, authorization, id, window.Token);
                if (delivery.StatusKey == "sent") return delivery;
                Assert.AreEqual("accepted", delivery.StatusKey);
                await Task.Delay(100, window.Token);
            }
        }
    }
}
