using System.Net;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Notifications;

public sealed partial class NotificationWorkerAdmissionTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Crashed_smtp_worker_leaves_accepted_mail_for_reconciliation_without_resending(DatabaseProvider provider, bool startTls)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = budget.Token;
        var database = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var keyRing = new NotificationRecoveryKeyRing();
        using var sourceKeys = keyRing.OpenSource();
        await using var inbox = new ControlledSmtpInbox(startTls);
        using var api = new FullNetApiFactory(provider, database,
            new Dictionary<string, string?> { ["Notifications:Providers:Smtp:Enabled"] = "true" },
            configureTestServices: services =>
            {
                services.AddSingleton(sourceKeys.GetRequiredService<IDataProtectionProvider>());
                ConfigureWorker(services);
            });
        await api.InitializeAsync(token, useSchemaTemplate: false);
        using var client = api.CreateClientForHost("localhost");
        var access = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, token);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, access, token);
        const string secret = "env://FULLNET_RECOVERY_TEST_SMTP_PASSWORD";
        var route = await CreateSmtpRouteAsync(api.Services, client, access, user.Id, inbox, token, secret);
        var intent = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, access, route, user.Id, "accepted-before-crash", token);
        var queued = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, access, intent.Id, token);

        var processSettings = keyRing.SourceSettings;
        processSettings["RecoveryFixture:NotificationSmtpCertificateSha256"] = inbox.CertificateFingerprint;
        processSettings["Notifications:Providers:Smtp:Enabled"] = "true";
        processSettings["Notifications:SecretReferences:email.smtp:0"] = secret;
        processSettings["FULLNET_RECOVERY_TEST_SMTP_PASSWORD"] = "protocol-password";
        processSettings["Notifications:Worker:LeaseSeconds"] = "30";

        var processDirectory = Directory.CreateTempSubdirectory("fullnet-notification-process-");
        try
        {
            var quitGate = new TaskCompletionSource<ControlledSmtpInbox.Observation>(TaskCreationOptions.RunContinuationsAsynchronously);
            var receiving = inbox.ReceiveAsync("accepted", token, quitGate);
            await using var source = await StartWorkerAsync(1);
            await VerifyMailAsync(quitGate.Task, "accepted-before-crash", token);
            var accepted = await quitGate.Task;
            var beforeCrash = await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(client, access, queued.Id, token);
            Assert.AreEqual("accepted", beforeCrash.StatusKey);
            Assert.HasCount(0, beforeCrash.Attempts);
            await source.CrashAsync();
            await receiving.WaitAsync(TimeSpan.FromSeconds(10), token);

            await using var recovery = await StartWorkerAsync(2);
            Assert.AreNotEqual(source.Id, recovery.Id);
            var unknown = await WaitUntilAsync(queued.Id, "unknown", TimeSpan.FromSeconds(60));
            Assert.AreEqual(intent.Id, unknown.IntentId);
            Assert.IsNull(unknown.NextAttemptAtUtc);
            Assert.HasCount(1, unknown.Attempts);
            Assert.AreEqual("unknown", unknown.Attempts[0].ResultCategoryKey);
            Assert.IsNull(unknown.Attempts[0].ProviderMessageId);
            Assert.IsFalse(inbox.HasPendingConnection);

            var evidence = $"smtp-receipt:{accepted.Message!.MessageId}";
            using var reconcile = NotificationProfileBindingAssertions.CreateBearerJsonRequest(HttpMethod.Post,
                $"/api/v1/notifications/deliveries/{queued.Id:D}/reconcile", access,
                new ReconcileNotificationDeliveryRequest(unknown.Revision, "confirmed_accepted", evidence));
            using var response = await client.SendAsync(reconcile, token);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(token));
            var confirmed = await WaitUntilAsync(queued.Id, "sent", TimeSpan.FromSeconds(10));
            Assert.AreEqual(unknown.Attempts.Single().Id, confirmed.Attempts.Single().Id);
            Assert.IsNull(confirmed.Attempts[0].ProviderMessageId);

            var next = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, access, route, user.Id, "accepted-after-restart", token);
            var nextDelivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, access, next.Id, token);
            await VerifyMailAsync(inbox.ReceiveAsync("accepted", token), "accepted-after-restart", token);
            await WaitUntilAsync(nextDelivery.Id, "sent", TimeSpan.FromSeconds(10));
            await recovery.CrashAsync();
            Assert.IsFalse(inbox.HasPendingConnection);
        }
        finally
        {
            processDirectory.Delete(recursive: true);
        }

        Task<EnterpriseRequestWorkerProcess> StartWorkerAsync(int sequence) =>
            EnterpriseRequestWorkerProcess.StartAsync(api, processDirectory.FullName, "none",
                Path.Combine(processDirectory.FullName, "unused-gate"), sequence,
                additionalSettings: processSettings);

        async Task<NotificationDeliveryResponse> WaitUntilAsync(Guid id, string status, TimeSpan timeout)
        {
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            window.CancelAfter(timeout);
            while (true)
            {
                var delivery = await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(client, access, id, window.Token);
                if (delivery.StatusKey == status) return delivery;
                Assert.AreEqual("accepted", delivery.StatusKey, $"投递没有接续到 {status}。");
                await Task.Delay(100, window.Token);
            }
        }
    }
}
