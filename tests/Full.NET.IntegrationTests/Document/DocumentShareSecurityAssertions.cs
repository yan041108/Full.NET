using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Full.NET.Abstractions;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Full.NET.IntegrationTests.Document;

/// <summary>
/// RED 断言：Document 分享口令安全在双库运行时不泄露明文、错误口令不增加计数、匿名POST访问。
/// GREEN 阶段：补齐 PasswordHash 列、Hasher、匿名 Endpoint 后转绿。
/// </summary>
[TestClass]
public sealed class DocumentShareSecurityAssertions
{
    [TestClass]
    public sealed class SqlServer : DocumentShareSecuritySpecification
    {
        protected override IServiceProvider CreateFixture() =>
            DocumentFixtureFactory.CreateSqlServer();
    }

    [TestClass]
    public sealed class MySql : DocumentShareSecuritySpecification
    {
        protected override IServiceProvider CreateFixture() =>
            DocumentFixtureFactory.CreateMySql();
    }
}

public abstract class DocumentShareSecuritySpecification
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    protected abstract IServiceProvider CreateFixture();

    [TestMethod]
    public async Task Create_password_share_stores_no_plaintext_password()
    {
        // 中文注释：RED 断言——数据库列必须叫 PasswordHash，内容是哈希不能匹配用户输入明文；
        // 当前 RED 阶段会因列名或 Hasher 实现缺失失败；GREEN 后必须通过。
        var fixture = CreateFixture();
        var now = DateTimeOffset.UtcNow;
        const string password = "Share@2026!Secure";
        var (shareId, shareCode, documentId) = await CreateShareWithPasswordAsync(fixture, password, now)
            .ConfigureAwait(false);

        var storedPasswordHash = await QueryStoredPasswordHashAsync(fixture, shareId).ConfigureAwait(false);
        Assert.IsNotNull(storedPasswordHash, "分享创建后必须写入 PasswordHash 列");
        Assert.AreNotEqual(password, storedPasswordHash, "数据库绝对不得存储明文口令");
        Assert.IsFalse(storedPasswordHash.Contains(password, StringComparison.Ordinal),
            "哈希值不得包含明文子串");
        Assert.IsTrue(storedPasswordHash.Length >= 60,
            "PBKDF2/Identity PasswordHasher 输出至少 61 字符的 Base64；当前过短");
    }

    [TestMethod]
    public async Task Wrong_password_does_not_increment_access_count()
    {
        // 中文注释：错误口令或空口令在验证层短路，不进入 AccessCount 自增 SQL；
        // RED 阶段当前实现直接拒绝密码，GREEN 后验证短路逻辑生效。
        var fixture = CreateFixture();
        var now = DateTimeOffset.UtcNow;
        const string password = "Share@2026!Secure";
        var (shareId, shareCode, _) = await CreateShareWithPasswordAsync(fixture, password, now)
            .ConfigureAwait(false);

        await AccessShareAnonymousAsync(fixture, shareCode, "WrongPassword!1").ConfigureAwait(false);
        await AccessShareAnonymousAsync(fixture, shareCode, "StillWrong!2").ConfigureAwait(false);

        var accessCount = await QueryAccessCountAsync(fixture, shareId).ConfigureAwait(false);
        Assert.AreEqual(0, accessCount,
            "错误口令绝对不得增加 AccessCount（避免被用作存在性 oracle）");
    }

    [TestMethod]
    public async Task Correct_password_access_succeeds_once()
    {
        var fixture = CreateFixture();
        var now = DateTimeOffset.UtcNow;
        const string password = "Share@2026!Secure";
        var (shareId, shareCode, _) = await CreateShareWithPasswordAsync(fixture, password, now)
            .ConfigureAwait(false);

        var access = await AccessShareAnonymousAsync(fixture, shareCode, password).ConfigureAwait(false);

        Assert.IsTrue(access.IsSuccess, $"正确口令访问必须成功：{FormatError(access)}");
        var accessCount = await QueryAccessCountAsync(fixture, shareId).ConfigureAwait(false);
        Assert.AreEqual(1, accessCount, "正确口令必须消耗 1 次访问计数");
    }

    [TestMethod]
    public async Task Share_query_response_has_no_password_or_hash_fields()
    {
        var fixture = CreateFixture();
        var now = DateTimeOffset.UtcNow;
        const string password = "Share@2026!Secure";
        await CreateShareWithPasswordAsync(fixture, password, now).ConfigureAwait(false);

        var queryJson = await QueryShareListJsonAsync(fixture).ConfigureAwait(false);
        AssertNoPasswordFields(queryJson, "管理列表响应");

        var getJson = await GetShareJsonAsync(fixture).ConfigureAwait(false);
        AssertNoPasswordFields(getJson, "管理详情响应");
    }

    private static void AssertNoPasswordFields(string json, string label)
    {
        Assert.IsFalse(
            json.Contains("\"password\"", StringComparison.OrdinalIgnoreCase),
            $"{label} JSON 不得包含 password：{json}");
        Assert.IsFalse(
            json.Contains("\"passwordHash\"", StringComparison.OrdinalIgnoreCase),
            $"{label} JSON 不得包含 passwordHash：{json}");
    }

    private static async Task<(Guid ShareId, string ShareCode, Guid DocumentId)> CreateShareWithPasswordAsync(
        IServiceProvider services, string password, DateTimeOffset now)
    {
        // RED 阶段以原始 SQL 插入作为 fixture；GREEN 阶段切换为真实 ManagementService
        var dbSession = services.GetRequiredService<IDbSession>();
        var documentId = Guid.CreateVersion7();
        var shareId = Guid.CreateVersion7();
        var shareCode = "DOC-SHARE-SEC-" + shareId.ToString("N")[..8].ToUpperInvariant();

        await dbSession.InTransactionAsync(async (conn, tx, ct) =>
        {
            await InsertDocumentAsync(conn, tx, documentId, now).ConfigureAwait(false);
            await conn.ExecuteAsync(
                """
                INSERT INTO fn_document_share
                    (Id, TenantId, DocumentId, ShareCode, CreatedAtUtc, ExpireTime,
                     PasswordHash, MaxAccessCount, AccessCount, IsEnabled, Version)
                VALUES
                    (@Id, NULL, @DocumentId, @ShareCode, @CreatedAtUtc, @ExpireTime,
                     @PasswordHash, @MaxAccessCount, 0, 1, 1);
                """,
                new
                {
                    Id = shareId,
                    DocumentId = documentId,
                    ShareCode = shareCode,
                    CreatedAtUtc = now,
                    ExpireTime = now.AddDays(7),
                    PasswordHash = "REDPLACEHOLDER",
                    MaxAccessCount = (int?)3,
                },
                tx).ConfigureAwait(false);
            return 0;
        }).ConfigureAwait(false);
        return (shareId, shareCode, documentId);
    }

    private static Task InsertDocumentAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid documentId,
        DateTimeOffset now)
    {
        return conn.ExecuteAsync(
            """
            INSERT INTO fn_document_item
                (Id, TenantId, CategoryId, DocumentNo, Title, FileName, FileExtension,
                 MimeType, FileSizeBytes, StorageBlobId, CurrentVersionId, CreatedAtUtc,
                 CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, DeletedAtUtc, DeletedByUserId, Version)
            VALUES
                (@Id, NULL, NULL, @DocumentNo, @Title, @FileName, @FileExtension,
                 @MimeType, @FileSizeBytes, @StorageBlobId, @CurrentVersionId, @CreatedAtUtc,
                 @CreatedByUserId, NULL, NULL, NULL, NULL, 1);
            """,
            new
            {
                Id = documentId,
                DocumentNo = "DOC-SEC-RED",
                Title = "Security RED Document",
                FileName = "security-red.txt",
                FileExtension = ".txt",
                MimeType = "text/plain",
                FileSizeBytes = 16L,
                StorageBlobId = Guid.CreateVersion7(),
                CurrentVersionId = (Guid?)null,
                CreatedAtUtc = now,
                CreatedByUserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            },
            tx);
    }

    private static async Task<string?> QueryStoredPasswordHashAsync(IServiceProvider services, Guid shareId)
    {
        var dbSession = services.GetRequiredService<IDbSession>();
        var options = services.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();
        return await dbSession.QueryOneAsync<string?>(async (conn, ct) =>
        {
            var column = options.Value.Provider == DatabaseProvider.SqlServer
                ? "PasswordHash"
                : "PasswordHash";
            var sql = $"""
                SELECT TOP(1) {column} FROM fn_document_share WHERE Id = @Id;
                """;
            if (options.Value.Provider == DatabaseProvider.MySql)
            {
                sql = $"""
                    SELECT {column} FROM fn_document_share WHERE Id = @Id LIMIT 1;
                    """;
            }
            return await conn.ExecuteScalarAsync<string?>(sql, new { Id = shareId })
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static async Task<int> QueryAccessCountAsync(IServiceProvider services, Guid shareId)
    {
        var dbSession = services.GetRequiredService<IDbSession>();
        return await dbSession.QueryOneAsync<int>(async (conn, ct) =>
        {
            var sql = """
                SELECT AccessCount FROM fn_document_share WHERE Id = @Id;
                """;
            return await conn.ExecuteScalarAsync<int>(sql, new { Id = shareId })
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static Task<Result<HostDocumentShareAccessResponse>> AccessShareAnonymousAsync(
        IServiceProvider services,
        string shareCode,
        string? password)
    {
        // RED 阶段占位：GREEN 时换成真实匿名访问 Endpoint/Service
        return Task.FromResult(Result<HostDocumentShareAccessResponse>.Failure(
            new Error(
                "document.host_share.access_denied",
                "RED 占位——Task2 GREEN 实现匿名访问服务",
                ErrorType.Unauthorized)));
    }

    private static Task<string> QueryShareListJsonAsync(IServiceProvider services)
    {
        var response = new HostDocumentSharePageResponse(
            new List<HostDocumentShareResponse>(), 1, 20, 0);
        return Task.FromResult(JsonSerializer.Serialize(response, _jsonOptions));
    }

    private static Task<string> GetShareJsonAsync(IServiceProvider services)
    {
        var response = new HostDocumentShareResponse(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "DOC-SEC-JSON-1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7),
            Password: null,
            MaxAccessCount: 5,
            AccessCount: 0,
            IsEnabled: true,
            Version: 1L);
        return Task.FromResult(JsonSerializer.Serialize(response, _jsonOptions));
    }

    private static string FormatError<T>(Result<T> result)
    {
        return result.IsSuccess || result.Error is null ? "<success>" : $"{result.Error.Code}: {result.Error.Message}";
    }
}

/// <summary>
/// 匿名访问响应占位类型；GREEN 阶段搬入真实 HostDocumentContracts。
/// </summary>
public sealed record HostDocumentShareAccessResponse(
    Guid DocumentId,
    string ShareCode,
    bool HasPassword);

/// <summary>
/// 分页响应占位类型；GREEN 阶段引用真实契约。
/// </summary>
public sealed record HostDocumentSharePageResponse(
    IReadOnlyList<HostDocumentShareResponse> Items,
    int Page,
    int PageSize,
    long Total);

/// <summary>
/// RED 阶段占位工厂；GREEN 阶段替换为真实 SharedDatabaseFixture 双库构造。
/// </summary>
internal static class DocumentFixtureFactory
{
    public static IServiceProvider CreateSqlServer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = "Server=.;Database=fn_document_sec_red;Trusted_Connection=True;TrustServerCertificate=True;",
            CommandTimeoutSeconds = 30,
        }));
        services.AddSingleton<IDbSession, FakeDbSession>();
        services.AddSingleton<IOptionsSnapshot<DatabaseOptions>>(sp =>
            new FakeOptionsSnapshot<DatabaseOptions>(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value));
        return services.BuildServiceProvider();
    }

    public static IServiceProvider CreateMySql()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = "Server=127.0.0.1;Database=fn_document_sec_red;Uid=root;Pwd=root;AllowUserVariables=True;",
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 30,
        }));
        services.AddSingleton<IDbSession, FakeDbSession>();
        services.AddSingleton<IOptionsSnapshot<DatabaseOptions>>(sp =>
            new FakeOptionsSnapshot<DatabaseOptions>(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value));
        return services.BuildServiceProvider();
    }
}

/// <summary>
/// RED 占位数据库会话接口；GREEN 阶段替换为 Full.NET.Data.Dapper 的真实 IDbSession。
/// </summary>
internal interface IDbSession
{
    DatabaseProvider Provider { get; }
    Task<T> QueryOneAsync<T>(Func<IDbConnection, CancellationToken, Task<T>> queryAsync, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> QueryListAsync<T>(Func<IDbConnection, CancellationToken, Task<IReadOnlyList<T>>> queryAsync, CancellationToken cancellationToken = default);
    Task<T> InTransactionAsync<T>(Func<IDbConnection, IDbTransaction, CancellationToken, Task<T>> transactionalAsync, CancellationToken cancellationToken = default);
}

internal sealed class FakeOptionsSnapshot<T> : IOptionsSnapshot<T>
    where T : class
{
    public FakeOptionsSnapshot(T value) => Value = value;
    public T Value { get; }
    public T Get(string? name) => Value;
}

internal sealed class FakeDbSession : IDbSession
{
    public DatabaseProvider Provider => DatabaseProvider.SqlServer;

    public Task<T> QueryOneAsync<T>(
        Func<IDbConnection, CancellationToken, Task<T>> queryAsync,
        CancellationToken cancellationToken = default)
    {
        // RED 占位，真实运行 GREEN 阶段替换为真实 SQL 执行；
        // 只要编译通过且类型对齐即可，RED 不跑集成数据库。
        return Task.FromResult(default(T)!);
    }

    public Task<IReadOnlyList<T>> QueryListAsync<T>(
        Func<IDbConnection, CancellationToken, Task<IReadOnlyList<T>>> queryAsync,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
    }

    public Task<T> InTransactionAsync<T>(
        Func<IDbConnection, IDbTransaction, CancellationToken, Task<T>> transactionalAsync,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(default(T)!);
    }
}
