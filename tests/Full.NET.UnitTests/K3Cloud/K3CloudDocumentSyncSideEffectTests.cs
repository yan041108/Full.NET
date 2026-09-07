using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.K3Cloud.Connectivity;
using Full.NET.Modules.K3Cloud.Contracts;
using Full.NET.Modules.K3Cloud.Features.ManageConnectionConfigs;
using Full.NET.Modules.K3Cloud.Features.ManageDocumentSyncs;
using Full.NET.Modules.K3Cloud.Persistence;
using Full.NET.Modules.K3Cloud.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.K3Cloud;

/// <summary>金蝶单据同步必须先提交本地意图，再在事务外调用 Save/Submit。</summary>
[TestClass]
public sealed class K3CloudDocumentSyncSideEffectTests
{
    /// <summary>远程 Save/Submit 只能发生在 pending 意图提交之后。</summary>
    [TestMethod]
    public async Task Save_and_submit_runs_after_intent_commit_async()
    {
        var fixture = CreateFixture();
        fixture.Client.SaveDocumentAsync(
                Arg.Any<K3CloudConnectionConfigRecord>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsFalse(fixture.Coordinator.HasTransaction);
                Assert.IsTrue(fixture.Coordinator.CommitCount >= 1);
                return ((bool Succeeded, string? BillId, string? BillNo, string Message))(true, "bill-1", "SO-1", "ok");
            });

        var result = await fixture.Service.CreateAsync(
            Guid.NewGuid(),
            new CreateK3CloudDocumentSyncRequest(
                fixture.Connection.Id,
                K3CloudDocumentTypeKeys.SalSaleOrder,
                "biz-1",
                """{"FBillNo":"SO-1"}"""));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(K3CloudDocumentSyncStatusKeys.Submitted, fixture.Store.Records.Values.Single().StatusKey);
    }

    /// <summary>远程超时必须写成未知状态，不能把可能已保存的单据标记为失败。</summary>
    [TestMethod]
    public async Task Timeout_persists_provider_unknown_async()
    {
        var fixture = CreateFixture();
        fixture.Client.When(client => client.SaveDocumentAsync(
                Arg.Any<K3CloudConnectionConfigRecord>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new HttpRequestException("timeout"));

        var result = await fixture.Service.CreateAsync(
            Guid.NewGuid(),
            new CreateK3CloudDocumentSyncRequest(
                fixture.Connection.Id,
                K3CloudDocumentTypeKeys.SalSaleOrder,
                "biz-2",
                """{"FBillNo":"SO-2"}"""));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(K3CloudErrorCodes.RemoteCallUnknown, result.Error!.Code);
        Assert.AreEqual(K3CloudDocumentSyncStatusKeys.ProviderUnknown, fixture.Store.Records.Values.Single().StatusKey);
    }

    /// <summary>pending 租约未过期时，并发重试不得再次调用金蝶。</summary>
    [TestMethod]
    public async Task Overlapping_retry_does_not_invoke_remote_async()
    {
        var fixture = CreateFixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.SaveDocumentAsync(
                Arg.Any<K3CloudConnectionConfigRecord>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return ((bool Succeeded, string? BillId, string? BillNo, string Message))(true, "bill-3", "SO-3", "ok");
            });

        var first = fixture.Service.CreateAsync(
            Guid.NewGuid(),
            new CreateK3CloudDocumentSyncRequest(
                fixture.Connection.Id,
                K3CloudDocumentTypeKeys.SalSaleOrder,
                "biz-3",
                """{"FBillNo":"SO-3"}"""));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var syncId = fixture.Store.Records.Keys.Single();
        var second = await fixture.Service.RetryAsync(syncId);
        release.TrySetResult();
        var firstResult = await first;

        Assert.IsTrue(firstResult.IsSuccess);
        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(K3CloudErrorCodes.DocumentSyncInProgress, second.Error!.Code);
        await fixture.Client.Received(1).SaveDocumentAsync(
            Arg.Any<K3CloudConnectionConfigRecord>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("provider_unknown")]
    [DataRow("pending")]
    public async Task Ambiguous_previous_invocation_is_not_replayed_async(string status)
    {
        var fixture = CreateFixture();
        var id = Guid.NewGuid();
        fixture.Store.Records[id] = new K3CloudDocumentSyncRecord
        {
            Id = id, ConnectionConfigId = fixture.Connection.Id,
            DocumentTypeKey = K3CloudDocumentTypeKeys.SalSaleOrder,
            BusinessKey = "ambiguous", PayloadJson = "{}", StatusKey = status,
            CreatedAtUtc = DateTimeOffset.UnixEpoch, Version = 1,
        };
        var result = await fixture.Service.RetryAsync(id);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(K3CloudErrorCodes.RemoteCallUnknown, result.Error!.Code);
        Assert.AreEqual(0, fixture.Client.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Definitive_submit_failure_retries_only_saved_document()
    {
        var fixture = CreateFixture();
        var id = Guid.NewGuid();
        fixture.Store.Records[id] = new K3CloudDocumentSyncRecord
        {
            Id = id, ConnectionConfigId = fixture.Connection.Id,
            DocumentTypeKey = K3CloudDocumentTypeKeys.SalSaleOrder,
            BusinessKey = "saved", PayloadJson = "{}", StatusKey = K3CloudDocumentSyncStatusKeys.SubmitFailed,
            ExternalBillId = "42", ExternalBillNo = "SO-42", CreatedAtUtc = DateTimeOffset.UnixEpoch, Version = 1,
        };
        fixture.Client.SubmitDocumentAsync(fixture.Connection, Arg.Any<string>(), "SAL_SaleOrder", "42", "SO-42", Arg.Any<CancellationToken>())
            .Returns((true, "ok"));
        var result = await fixture.Service.RetryAsync(id);
        Assert.IsTrue(result.IsSuccess);
        await fixture.Client.DidNotReceiveWithAnyArgs().SaveDocumentAsync(default!, default!, default!, default!);
        await fixture.Client.Received(1).SubmitDocumentAsync(fixture.Connection, Arg.Any<string>(), "SAL_SaleOrder", "42", "SO-42", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Submit_timeout_keeps_saved_identity_and_blocks_blind_retry()
    {
        var fixture = CreateFixture();
        fixture.Client.SaveDocumentAsync(Arg.Any<K3CloudConnectionConfigRecord>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((true, "55", "SO-55", "ok"));
        fixture.Client.SubmitDocumentAsync(Arg.Any<K3CloudConnectionConfigRecord>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<(bool, string)>(new HttpRequestException("timeout")));
        var result = await fixture.Service.CreateAsync(Guid.NewGuid(), new CreateK3CloudDocumentSyncRequest(
            fixture.Connection.Id, K3CloudDocumentTypeKeys.SalSaleOrder, "saved-timeout", "{}"));
        Assert.IsFalse(result.IsSuccess);
        var saved = fixture.Store.Records.Values.Single();
        Assert.AreEqual("55", saved.ExternalBillId);
        Assert.AreEqual(K3CloudDocumentSyncStatusKeys.ProviderUnknown, saved.StatusKey);
        var retry = await fixture.Service.RetryAsync(saved.Id);
        Assert.AreEqual(K3CloudErrorCodes.RemoteCallUnknown, retry.Error!.Code);
        await fixture.Client.Received(1).SaveDocumentAsync(Arg.Any<K3CloudConnectionConfigRecord>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("null")]
    [DataRow("{\"Result\":{\"ResponseStatus\":{}}}")]
    public void Missing_provider_result_is_unknown_not_retryable_failure(string response)
    {
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => K3CloudWebApiClient.ReadExplicitSuccess(response));
    }

    [TestMethod]
    public async Task Number_only_saved_document_reports_submit_failure()
    {
        var fixture = CreateFixture();
        fixture.Client.SaveDocumentAsync(Arg.Any<K3CloudConnectionConfigRecord>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((true, (string?)null, "SO-55", "ok"));
        fixture.Client.SubmitDocumentAsync(Arg.Any<K3CloudConnectionConfigRecord>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((false, "rejected"));
        await fixture.Service.CreateAsync(Guid.NewGuid(), new CreateK3CloudDocumentSyncRequest(
            fixture.Connection.Id, K3CloudDocumentTypeKeys.SalSaleOrder, "number-only", "{}"));
        var saved = fixture.Store.Records.Values.Single();
        Assert.AreEqual(K3CloudDocumentSyncStatusKeys.SubmitFailed, saved.StatusKey);
        Assert.AreEqual(K3CloudDocumentSyncStepKeys.Submit, saved.LastStepKey);
    }

    /// <summary>建立带真实事务提交语义的单据同步服务。</summary>
    /// <returns>隔离测试夹具。</returns>
    private static SyncFixture CreateFixture()
    {
        var protector = new K3CloudPasswordProtector(new EphemeralDataProtectionProvider());
        var connection = new K3CloudConnectionConfigRecord
        {
            Id = Guid.NewGuid(),
            Name = "review",
            BaseUrl = "https://k3.example.test",
            AcctId = "acct",
            Username = "user",
            PasswordProtected = protector.Protect("secret"),
            Lcid = 2052,
            IsEnabled = true,
            Version = 1,
        };
        var store = new SyncStore { Connection = connection };
        var coordinator = new RecordingDbTransactionCoordinator();
        var transaction = new DapperCommandTransaction(coordinator);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 7, 2, 0, 0, TimeSpan.Zero));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.NewGuid());
        var client = Substitute.For<IK3CloudWebApiClient>();
        client.SubmitDocumentAsync(Arg.Any<K3CloudConnectionConfigRecord>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsFalse(coordinator.HasTransaction);
                var saved = store.Records.Values.Single();
                Assert.IsFalse(string.IsNullOrWhiteSpace(saved.ExternalBillId));
                Assert.IsTrue(coordinator.CommitCount >= 2);
                return (true, "ok");
            });
        var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        return new SyncFixture(
            connection,
            store,
            coordinator,
            client,
            new K3CloudDocumentSyncService(
                store,
                store,
                transaction,
                new K3CloudDocumentSyncQueryService(store, options),
                new K3CloudConnectionQueryService(store),
                protector,
                client,
                clock,
                ids));
    }

    /// <summary>金蝶同步外部副作用测试夹具。</summary>
    /// <param name="Connection">连接配置。</param>
    /// <param name="Store">内存持久化。</param>
    /// <param name="Coordinator">事务记录器。</param>
    /// <param name="Client">远程客户端替身。</param>
    /// <param name="Service">同步服务。</param>
    private sealed record SyncFixture(
        K3CloudConnectionConfigRecord Connection,
        SyncStore Store,
        RecordingDbTransactionCoordinator Coordinator,
        IK3CloudWebApiClient Client,
        K3CloudDocumentSyncService Service);

    /// <summary>按语句名维护单据同步内存状态。</summary>
    private sealed class SyncStore : IQueryExecutor, ICommandExecutor
    {
        /// <summary>当前测试连接。</summary>
        public K3CloudConnectionConfigRecord Connection { get; init; } = null!;

        /// <summary>已提交同步记录。</summary>
        public Dictionary<Guid, K3CloudDocumentSyncRecord> Records { get; } = [];

        /// <inheritdoc />
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var values = (IReadOnlyDictionary<string, object?>)parameters!;
            if (typeof(T) == typeof(K3CloudConnectionConfigRecord))
            {
                return Task.FromResult((T?)(object?)Connection);
            }

            if (typeof(T) == typeof(K3CloudDocumentSyncRecord))
            {
                if (values.TryGetValue("SyncId", out var syncId)
                    && syncId is Guid id
                    && Records.TryGetValue(id, out var byId))
                {
                    return Task.FromResult((T?)(object?)byId);
                }

                if (values.TryGetValue("BusinessKey", out var businessKey)
                    && businessKey is string key)
                {
                    var match = Records.Values.FirstOrDefault(row =>
                        row.ConnectionConfigId == (Guid)values["ConnectionConfigId"]!
                        && row.DocumentTypeKey == (string)values["DocumentTypeKey"]!
                        && row.BusinessKey == key);
                    return Task.FromResult((T?)(object?)match);
                }
            }

            return Task.FromResult(default(T?));
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>([]);

        /// <inheritdoc />
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var values = (IReadOnlyDictionary<string, object?>)parameters!;
            switch (statement.Name)
            {
                case "k3cloud.document_sync.insert":
                    var id = (Guid)values["Id"]!;
                    Records[id] = new K3CloudDocumentSyncRecord
                    {
                        Id = id,
                        ConnectionConfigId = (Guid)values["ConnectionConfigId"]!,
                        DocumentTypeKey = (string)values["DocumentTypeKey"]!,
                        BusinessKey = (string)values["BusinessKey"]!,
                        PayloadJson = (string)values["PayloadJson"]!,
                        StatusKey = (string)values["StatusKey"]!,
                        CreatedAtUtc = (DateTimeOffset)values["CreatedAtUtc"]!,
                        UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?,
                        CreatedByUserId = (Guid)values["CreatedByUserId"]!,
                        Version = (int)values["Version"]!,
                    };
                    return Task.FromResult(1);
                case "k3cloud.document_sync.update":
                    return Task.FromResult(Update(
                        (Guid)values["Id"]!,
                        (int)values["Version"]!,
                        current =>
                        {
                            current.StatusKey = (string)values["StatusKey"]!;
                            current.LastStepKey = values["LastStepKey"] as string;
                            current.ExternalBillId = values["ExternalBillId"] as string;
                            current.ExternalBillNo = values["ExternalBillNo"] as string;
                            current.LastErrorCode = values["LastErrorCode"] as string;
                            current.LastErrorMessage = values["LastErrorMessage"] as string;
                            current.SubmittedAtUtc = values["SubmittedAtUtc"] as DateTimeOffset?;
                            current.UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?;
                            current.Version += 1;
                            return current;
                        }));
                case "k3cloud.document_sync.claim_retry":
                    return Task.FromResult(Update(
                        (Guid)values["Id"]!,
                        (int)values["Version"]!,
                        current =>
                        {
                            current.StatusKey = K3CloudDocumentSyncStatusKeys.Pending;
                            current.UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?;
                            current.Version += 1;
                            return current;
                        }));
                default:
                    return Task.FromResult(0);
            }
        }

        /// <summary>按版本更新同步记录。</summary>
        /// <param name="id">记录标识。</param>
        /// <param name="version">期望版本。</param>
        /// <param name="update">就地更新。</param>
        /// <returns>受影响行数。</returns>
        private int Update(Guid id, int version, Func<K3CloudDocumentSyncRecord, K3CloudDocumentSyncRecord> update)
        {
            if (!Records.TryGetValue(id, out var current) || current.Version != version)
            {
                return 0;
            }

            Records[id] = update(current);
            return 1;
        }
    }
}
