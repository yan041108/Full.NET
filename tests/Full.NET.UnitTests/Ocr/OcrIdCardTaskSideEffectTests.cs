using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Ocr.Connectivity;
using Full.NET.Modules.Ocr.Contracts;
using Full.NET.Modules.Ocr.Domain;
using Full.NET.Modules.Ocr.Features.ManageIdCardTasks;
using Full.NET.Modules.Ocr.Features.ManageProviderConfigs;
using Full.NET.Modules.Ocr.Persistence;
using Full.NET.Modules.Ocr.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ocr;

/// <summary>身份证 OCR 必须先提交 pending 任务，再在事务外调用识别提供程序。</summary>
[TestClass]
public sealed class OcrIdCardTaskSideEffectTests
{
    /// <summary>识别请求只能发生在任务意图提交之后。</summary>
    [TestMethod]
    public async Task Recognize_runs_after_intent_commit_async()
    {
        var fixture = CreateFixture();
        fixture.Client.RecognizeAsync(
                Arg.Any<OcrProviderConfigRecord>(),
                Arg.Any<string?>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.IsFalse(fixture.Coordinator.HasTransaction);
                Assert.IsTrue(fixture.Coordinator.CommitCount >= 1);
                return (true, new OcrIdCardParsedResult("张三", "110101199001011234", "男", "汉", "北京", "1990-01-01"), "{}", "ok");
            });

        var result = await fixture.Service.CreateAsync(Guid.NewGuid(), new CreateOcrIdCardTaskRequest(fixture.FileId));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(OcrIdCardTaskStatusKeys.Recognized, fixture.Store.Tasks.Values.Single().StatusKey);
    }

    /// <summary>识别超时必须写成未知状态，不能把可能已计费的请求标记为失败。</summary>
    [TestMethod]
    public async Task Timeout_persists_provider_unknown_async()
    {
        var fixture = CreateFixture();
        fixture.Client.RecognizeAsync(
                Arg.Any<OcrProviderConfigRecord>(),
                Arg.Any<string?>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<(bool, OcrIdCardParsedResult?, string, string)>(_ => throw new TaskCanceledException("ocr timeout"));

        var result = await fixture.Service.CreateAsync(Guid.NewGuid(), new CreateOcrIdCardTaskRequest(fixture.FileId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(OcrErrorCodes.RemoteCallUnknown, result.Error!.Code);
        Assert.AreEqual(OcrIdCardTaskStatusKeys.ProviderUnknown, fixture.Store.Tasks.Values.Single().StatusKey);
    }

    /// <summary>源文件描述必须在本地事务外读取，避免把 Files 合同带进 OCR 短事务。</summary>
    [TestMethod]
    public async Task Source_file_descriptor_is_read_before_intent_transaction_async()
    {
        var fixture = CreateFixture();
        fixture.Descriptors.GetReadyDescriptorAsync(fixture.FileId, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.IsFalse(fixture.Coordinator.HasTransaction);
                Assert.AreEqual(0, fixture.Coordinator.BeginCount);
                return new HostFileDescriptor(
                    fixture.FileId,
                    "id.jpg",
                    "image/jpeg",
                    128,
                    null,
                    Guid.NewGuid());
            });
        fixture.Client.RecognizeAsync(
                Arg.Any<OcrProviderConfigRecord>(),
                Arg.Any<string?>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((true, new OcrIdCardParsedResult("张三", "110101199001011234", "男", "汉", "北京", "1990-01-01"), "{}", "ok"));

        var result = await fixture.Service.CreateAsync(Guid.NewGuid(), new CreateOcrIdCardTaskRequest(fixture.FileId));

        Assert.IsTrue(result.IsSuccess);
        await fixture.Descriptors.Received(1).GetReadyDescriptorAsync(fixture.FileId, Arg.Any<CancellationToken>());
    }

    /// <summary>非法源文件必须在开启事务前失败。</summary>
    [TestMethod]
    public async Task Invalid_source_file_does_not_begin_transaction_async()
    {
        var fixture = CreateFixture();
        fixture.Descriptors.GetReadyDescriptorAsync(fixture.FileId, Arg.Any<CancellationToken>())
            .Returns((HostFileDescriptor?)null);

        var result = await fixture.Service.CreateAsync(Guid.NewGuid(), new CreateOcrIdCardTaskRequest(fixture.FileId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(OcrErrorCodes.SourceFileInvalid, result.Error!.Code);
        Assert.AreEqual(0, fixture.Coordinator.BeginCount);
        Assert.AreEqual(0, fixture.Store.Tasks.Count);
    }

    /// <summary>识别已经成功时，本地回写失败仍必须保留已提交 pending 意图。</summary>
    [TestMethod]
    public async Task Keeps_committed_intent_when_result_persist_fails_async()
    {
        var fixture = CreateFixture();
        fixture.Client.RecognizeAsync(
                Arg.Any<OcrProviderConfigRecord>(),
                Arg.Any<string?>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((true, new OcrIdCardParsedResult("张三", "110101199001011234", null, null, null, null), "{}", "ok"));
        fixture.Store.ThrowOnUpdate = new InvalidOperationException("local persist failed");

        var result = await fixture.Service.CreateAsync(Guid.NewGuid(), new CreateOcrIdCardTaskRequest(fixture.FileId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(OcrErrorCodes.RemoteCallUnknown, result.Error!.Code);
        Assert.AreEqual(OcrIdCardTaskStatusKeys.Pending, fixture.Store.Tasks.Values.Single().StatusKey);
        await fixture.Client.Received(1).RecognizeAsync(
            Arg.Any<OcrProviderConfigRecord>(),
            Arg.Any<string?>(),
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>建立带真实事务提交语义的 OCR 任务服务。</summary>
    /// <returns>隔离测试夹具。</returns>
    private static OcrFixture CreateFixture()
    {
        var fileId = Guid.NewGuid();
        var provider = new OcrProviderConfigRecord
        {
            Id = Guid.NewGuid(),
            ProviderKey = OcrProviderKeys.PaddleOcrIdCard,
            Name = "paddle",
            BaseUrl = "http://localhost:8080",
            IsEnabled = true,
            Version = 1,
        };
        var store = new OcrStore { Provider = provider };
        var coordinator = new RecordingDbTransactionCoordinator();
        var transaction = new DapperCommandTransaction(coordinator);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 7, 2, 0, 0, TimeSpan.Zero));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.NewGuid());
        var client = Substitute.For<IPaddleOcrIdCardClient>();
        var descriptors = Substitute.For<IHostFileDescriptorReader>();
        descriptors.GetReadyDescriptorAsync(fileId, Arg.Any<CancellationToken>())
            .Returns(new HostFileDescriptor(fileId, "id.jpg", "image/jpeg", 128, null, Guid.NewGuid()));
        var contents = Substitute.For<IHostFileContentReader>();
        contents.OpenReadyContentAsync(fileId, Arg.Any<CancellationToken>())
            .Returns(call => Result<HostFileContent>.Success(
                new HostFileContent(new MemoryStream([1, 2, 3]), "image/jpeg", "id.jpg")));
        var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        return new OcrFixture(
            fileId,
            store,
            coordinator,
            client,
            descriptors,
            new OcrIdCardTaskService(
                store,
                transaction,
                new OcrIdCardTaskQueryService(store, options),
                new OcrProviderQueryService(store),
                new OcrProviderSecretResolver(new OcrApiKeyProtector(new EphemeralDataProtectionProvider())),
                descriptors,
                contents,
                client,
                clock,
                ids));
    }

    /// <summary>OCR 外部副作用测试夹具。</summary>
    /// <param name="FileId">源文件标识。</param>
    /// <param name="Store">内存持久化。</param>
    /// <param name="Coordinator">事务记录器。</param>
    /// <param name="Client">识别客户端替身。</param>
    /// <param name="Descriptors">Host 文件描述读取替身。</param>
    /// <param name="Service">任务服务。</param>
    private sealed record OcrFixture(
        Guid FileId,
        OcrStore Store,
        RecordingDbTransactionCoordinator Coordinator,
        IPaddleOcrIdCardClient Client,
        IHostFileDescriptorReader Descriptors,
        OcrIdCardTaskService Service);

    /// <summary>按语句名维护 OCR 任务内存状态。</summary>
    private sealed class OcrStore : IQueryExecutor, ICommandExecutor
    {
        /// <summary>当前测试 Provider。</summary>
        public OcrProviderConfigRecord Provider { get; init; } = null!;

        /// <summary>已提交任务。</summary>
        public Dictionary<Guid, OcrIdCardTaskRecord> Tasks { get; } = [];

        /// <summary>回写识别结果时抛出的异常。</summary>
        public Exception? ThrowOnUpdate { get; set; }

        /// <inheritdoc />
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var values = (IReadOnlyDictionary<string, object?>)parameters!;
            if (typeof(T) == typeof(OcrProviderConfigRecord))
            {
                return Task.FromResult((T?)(object?)Provider);
            }

            if (typeof(T) == typeof(OcrIdCardTaskRecord)
                && values.TryGetValue("TaskId", out var taskId)
                && taskId is Guid id
                && Tasks.TryGetValue(id, out var task))
            {
                return Task.FromResult((T?)(object?)task);
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
                case "ocr.id_card_task.insert":
                    var id = (Guid)values["Id"]!;
                    Tasks[id] = new OcrIdCardTaskRecord
                    {
                        Id = id,
                        SourceFileId = (Guid)values["SourceFileId"]!,
                        StatusKey = (string)values["StatusKey"]!,
                        CreatedAtUtc = (DateTimeOffset)values["CreatedAtUtc"]!,
                        UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?,
                        CreatedByUserId = (Guid)values["CreatedByUserId"]!,
                        Version = (int)values["Version"]!,
                    };
                    return Task.FromResult(1);
                case "ocr.id_card_task.update":
                    if (ThrowOnUpdate is not null)
                    {
                        throw ThrowOnUpdate;
                    }

                    var taskId = (Guid)values["Id"]!;
                    if (!Tasks.TryGetValue(taskId, out var current) || current.Version != (int)values["Version"]!)
                    {
                        return Task.FromResult(0);
                    }

                    current.StatusKey = (string)values["StatusKey"]!;
                    current.RecognizedName = values["RecognizedName"] as string;
                    current.RecognizedIdNumber = values["RecognizedIdNumber"] as string;
                    current.FailureMessage = values["FailureMessage"] as string;
                    current.RawResultJson = values["RawResultJson"] as string;
                    current.RecognizedAtUtc = values["RecognizedAtUtc"] as DateTimeOffset?;
                    current.UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?;
                    current.Version += 1;
                    return Task.FromResult(1);
                default:
                    return Task.FromResult(0);
            }
        }
    }
}
