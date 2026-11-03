using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using DotNet.Testcontainers.Containers;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.IntegrationTests.Messaging;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    /// <summary>两个存活Native API真实失去控制会话；有限迟到REST由原生Worker按当前持久目标纠正。</summary>
    private static async Task VerifyNativeMultiStreamReconciliationAsync(HttpClient source, HttpClient peer, DbConnection db,
        DatabaseProvider provider, KafkaConnectAdminClient connect, MessagingHttpFaultProxy proxy, IContainer worker,
        string formal, string[] events, string evidence)
    {
        var target = events[0];
        var baselines = new string[events.Length];
        for (var i = 0; i < events.Length; i++) baselines[i] = (await db.ExecuteScalarAsync<string>(
            "SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1 AND CurrentOwner=2", new { type = events[i] }))!;
        var gates = new List<MessagingHttpFaultProxy.Gate>();
        var change = new ChangeDeliveryOwnerRequest(target, 1, Full.NET.Messaging.Abstractions.EventDeliveryOwner.LegacyPolling, "Workflow multi-stream late REST");
        try
        {
            var pause = Arm("pause");
            var oldRollback = ControlAsync(source, "/api/v1/messaging/delivery/rollback", change);
            await ReachedAsync(pause, oldRollback);
            var generation = (await LatestAsync()).Generation;
            await LoseOnlyControlSessionAsync();
            await RecoverAsync(generation);
            Assert.AreEqual(Full.NET.Messaging.Abstractions.EventDeliveryRollbackPhase.Recovered, (await LatestAsync()).Phase);
            var beforePause = await CorrectionsAsync();
            pause.Release.TrySetResult(); await pause.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using (var response = await oldRollback) Assert.IsTrue((int)response.StatusCode >= 500, "原API失锁后不得完成或补偿。");
            await WaitKafkaAsync(async () => await CorrectionsAsync() > beforePause
                && await connect.WaitForConnectorHealthyAsync(formal + "-0", TimeSpan.FromSeconds(1)), "多流Recovered后实际纠正迟到pause");
            Assert.AreEqual(Full.NET.Messaging.Abstractions.EventDeliveryRollbackPhase.Recovered, (await LatestAsync()).Phase);
            await AssertOtherStreamsAsync();
            Assert.AreEqual(baselines[0], await BindingAsync(target));
            Assert.AreEqual(2, await OwnerAsync(target));
            Assert.AreEqual(0, await FenceAsync());
            using (var alive = await source.GetAsync("/health/live")) Assert.AreEqual(HttpStatusCode.OK, alive.StatusCode);

            // 原恢复执行者也仅丢失会话；其resume跨越后续Completed，不能解除新的Legacy终态。
            var pendingPause = Arm("pause");
            var oldPreparation = ControlAsync(source, "/api/v1/messaging/delivery/rollback", change);
            await ReachedAsync(pendingPause, oldPreparation);
            var recovering = (await LatestAsync()).Generation;
            await LoseOnlyControlSessionAsync();
            var resume = Arm("resume");
            var oldRecovery = ControlAsync(peer, $"/api/v1/messaging/delivery/rollback-operations/{recovering:D}/recover", new RecoverRollbackRequest("old live recovery"));
            await ReachedAsync(resume, oldRecovery);
            await LoseOnlyControlSessionAsync();
            pendingPause.Release.TrySetResult(); await pendingPause.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using (var response = await oldPreparation) Assert.IsTrue((int)response.StatusCode >= 500);
            await RecoverAsync(recovering);
            await PostAsync(source, "/api/v1/messaging/delivery/rollback", change);
            var completed = await LatestAsync();
            Assert.AreEqual(Full.NET.Messaging.Abstractions.EventDeliveryRollbackPhase.Completed, completed.Phase);
            var beforeResume = await CorrectionsAsync();
            resume.Release.TrySetResult(); await resume.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using (var response = await oldRecovery) Assert.IsTrue((int)response.StatusCode >= 500, "失锁的旧恢复不得清除后续代次。");
            await WaitKafkaAsync(async () => await CorrectionsAsync() > beforeResume && await connect.IsConnectorPausedAsync(formal + "-0"),
                "多流Completed后实际纠正迟到resume");
            Assert.AreEqual(completed, await LatestAsync(), "纠偏不能改写终态日志或控制代次。");
            Assert.AreEqual(0, await OwnerAsync(target)); Assert.AreEqual(0, await FenceAsync());
            Assert.AreEqual(baselines[0], await BindingAsync(target));
            await AssertOtherStreamsAsync();
            using (var alive = await peer.GetAsync("/health/live")) Assert.AreEqual(HttpStatusCode.OK, alive.StatusCode);
            await VerifyIndependentFailureAsync();
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new { provider = provider.ToString(),
                latePauseGeneration = generation, lateResumeGeneration = recovering, completedGeneration = completed.Generation,
                beforePause, beforeResume, finalCorrections = await CorrectionsAsync(), unchangedOtherCdcStreams = events.Length - 1,
                targetOwner = "LegacyPolling", targetPaused = true, fence = 0, unsafeStreamDidNotStarveAnother = true }));
        }
        finally { foreach (var gate in gates) gate.Release.TrySetResult(); }

        MessagingHttpFaultProxy.Gate Arm(string verb)
        {
            var gate = proxy.Arm("PUT", $"/connectors/{formal}-0/{verb}", afterExecution: false);
            gate.ContinueAfterClientDisconnect = true; gates.Add(gate); return gate;
        }
        async Task AssertOtherStreamsAsync()
        {
            for (var i = 1; i < events.Length; i++)
            {
                Assert.AreEqual(2, await OwnerAsync(events[i])); Assert.AreEqual(baselines[i], await BindingAsync(events[i]));
                Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(formal + "-" + i, TimeSpan.FromSeconds(10)), "纠偏不能暂停其它正式流。");
            }
        }
        Task<string?> BindingAsync(string type) => db.ExecuteScalarAsync<string>("SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type });
        Task<int> OwnerAsync(string type) => db.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type });
        Task<int> FenceAsync() => db.ExecuteScalarAsync<int>("SELECT RollbackState FROM fn_messaging_stream_ownership WHERE MessageType=@target AND SchemaVersion=1", new { target });
        async Task<RollbackOperationResponse> LatestAsync() => (await peer.GetFromJsonAsync<RollbackOperationResponse>(
            "/api/v1/messaging/delivery/rollback-operations?eventType=" + target + "&schemaVersion=1"))!;
        async Task RecoverAsync(Guid generation) => await PostAsync(peer, $"/api/v1/messaging/delivery/rollback-operations/{generation:D}/recover", new RecoverRollbackRequest("new live control session"));
        async Task<int> CorrectionsAsync()
        {
            var logs = await worker.GetLogsAsync();
            return (logs.Stdout + logs.Stderr).Split("Messaging control target was reconciled from durable terminal state.", StringSplitOptions.None).Length - 1;
        }
        async Task LoseOnlyControlSessionAsync()
        {
            // 会话来自本例独占数据库/服务的实际锁；禁止终止API进程或其它数据库会话来模拟通过。
            var id = provider == DatabaseProvider.SqlServer
                ? await db.QuerySingleAsync<int>("SELECT request_session_id FROM sys.dm_tran_locks WHERE resource_type='APPLICATION' AND request_owner_type='SESSION' AND resource_database_id=DB_ID() AND resource_description LIKE '%messaging.delivery_control%'")
                : await db.QuerySingleAsync<int>("SELECT IS_USED_LOCK('messaging.delivery_control')");
            Assert.IsGreaterThan(0, id);
            await db.ExecuteAsync("KILL " + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        async Task VerifyIndependentFailureAsync()
        {
            var original = await connect.TryReadConnectorConfigurationAsync(formal + "-0"); Assert.IsNotNull(original);
            var changed = new Dictionary<string, string>(original) { ["transforms.outbox.route.topic.replacement"] = "fullnet.invalid.$1.v1" };
            try
            {
                // 真实失配的Legacy发布者必须保留保护；不能让它在每轮首项失败后饿死另一个独占CDC流。
                await connect.RegisterConnectorAsync(formal + "-0", changed);
                await connect.ResumeConnectorAsync(formal + "-0");
                await WaitKafkaAsync(async () =>
                {
                    var logs = await worker.GetLogsAsync();
                    return (logs.Stdout + logs.Stderr).Contains("connector_configuration_changed", StringComparison.Ordinal);
                }, "实际不安全资源必须记录稳定失败原因");
                var delayed = proxy.Arm("PUT", $"/connectors/{formal}-1/pause", afterExecution: false);
                delayed.ContinueAfterClientDisconnect = true; gates.Add(delayed);
                using var client = new HttpClient { BaseAddress = proxy.Address, Timeout = TimeSpan.FromSeconds(30) };
                using var cancellation = new CancellationTokenSource();
                var request = client.PutAsync($"/connectors/{formal}-1/pause", null, cancellation.Token);
                await ReachedAsync(delayed, request); cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(async () => { using var response = await request; });
                var before = await CorrectionsAsync();
                delayed.Release.TrySetResult(); await delayed.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await WaitKafkaAsync(async () => await CorrectionsAsync() > before
                    && await connect.WaitForConnectorHealthyAsync(formal + "-1", TimeSpan.FromSeconds(1)), "不安全流不能阻挡另一流纠正已接受的迟到pause");
                Assert.IsFalse(await connect.IsConnectorPausedAsync(formal + "-0"), "未知资源不得被顺带操作。");
                Assert.AreEqual(0, await OwnerAsync(target)); Assert.AreEqual(0, await FenceAsync());
                await AssertOtherStreamsAsync();
            }
            finally
            {
                await connect.RegisterConnectorAsync(formal + "-0", original);
                // 配置恢复到原指纹后仍由同一真实Worker重申Legacy暂停目标。
                await WaitKafkaAsync(() => connect.IsConnectorPausedAsync(formal + "-0"), "恢复原配置后重新确认持久Legacy目标");
            }
        }
    }

    private static async Task<HttpResponseMessage> ControlAsync(HttpClient client, string path, object request)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await client.PostAsJsonAsync(path, request);
            if (response.StatusCode != HttpStatusCode.Conflict || attempt >= 19) return response;
            response.Dispose(); await Task.Delay(250);
        }
    }

    private static async Task ReachedAsync(MessagingHttpFaultProxy.Gate gate, Task<HttpResponseMessage> request)
    {
        await Task.WhenAny(gate.Reached.Task, request).WaitAsync(TimeSpan.FromMinutes(3));
        if (!gate.Reached.Task.IsCompleted)
        {
            using var response = await request;
            Assert.Fail("控制请求在真实故障注入点之前返回：" + response.StatusCode + " " + await response.Content.ReadAsStringAsync());
        }
    }
}
