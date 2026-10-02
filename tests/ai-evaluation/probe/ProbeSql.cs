using System.Buffers.Binary;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.AiRetrieval.Probe;

/// <summary>仅在控制器新建的实验容器中直接验证驱动；生产必须经 Full.NET 自有 SQL 边界。</summary>
internal static class ProbeSql
{
    internal static async Task<(string Version, double Recall)> RunAsync(string provider,
        ProbeChunk[] chunks, ProbeCase[] cases, List<ProbeObservation> observations)
    {
        var connectionString = Environment.GetEnvironmentVariable("PROBE_CONNECTION")
            ?? throw new InvalidOperationException("缺少实验连接。");
        DbConnection Create(string value) => provider == "mysql" ? new MySqlConnection(value) : new SqlConnection(value);
        await using var admin = Create(connectionString);
        await admin.OpenAsync();
        var version = Convert.ToString(await ScalarAsync(admin, provider == "mysql" ? "SELECT VERSION();" : "SELECT @@VERSION;"))!;
        var database = "AiR03_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(admin, $"CREATE DATABASE {database};");
        var builder = provider == "mysql"
            ? (DbConnectionStringBuilder)new MySqlConnectionStringBuilder(connectionString) { Database = database }
            : new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database };
        await using var connection = Create(builder.ConnectionString);
        await connection.OpenAsync();
        // 固定实验表与二进制向量；SQL Server/MySQL 均采用精确大小写过滤和 UUID v7 标识。
        await ExecuteAsync(connection, provider == "mysql" ? """
            CREATE TABLE fn_ai_retrieval_probe_chunk (
                Id BINARY(16) NOT NULL PRIMARY KEY, ChunkId VARCHAR(64) NOT NULL,
                ScopeKey VARCHAR(64) NOT NULL, SourceVersion VARCHAR(64) NOT NULL,
                ModelKey VARCHAR(64) NOT NULL, Generation INT NOT NULL, VectorPayload VARBINARY(512) NOT NULL,
                INDEX IX_Probe_Scope (ScopeKey, SourceVersion, ModelKey, Generation)
            ) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
            """ : """
            CREATE TABLE fn_ai_retrieval_probe_chunk (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY CLUSTERED, ChunkId VARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                ScopeKey VARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                SourceVersion VARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                ModelKey VARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                Generation INT NOT NULL, VectorPayload VARBINARY(512) NOT NULL
            );
            CREATE INDEX IX_Probe_Scope ON fn_ai_retrieval_probe_chunk(ScopeKey, SourceVersion, ModelKey, Generation);
            """);
        async Task InsertAsync(ProbeChunk chunk, int generation)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO fn_ai_retrieval_probe_chunk (Id, ChunkId, ScopeKey, SourceVersion, ModelKey, Generation, VectorPayload) VALUES (@id, @chunk, @scope, @source, @model, @generation, @vector);";
            var id = Guid.CreateVersion7();
            Add(command, "@id", provider == "mysql" ? id.ToByteArray(bigEndian: true) : id);
            Add(command, "@chunk", chunk.ChunkId); Add(command, "@scope", chunk.TenantScope);
            Add(command, "@source", chunk.SourceVersion); Add(command, "@model", ProbeCorpus.Model);
            Add(command, "@generation", generation); Add(command, "@vector", Encode(ProbeVectorSearch.Embed(chunk.Text)));
            await command.ExecuteNonQueryAsync();
        }
        foreach (var chunk in chunks) await InsertAsync(chunk, 1);
        observations.Add(new("write", true, "synthetic UUID v7 rows and vectors persisted"));

        async Task<string[]> QueryAsync(ProbeCase testCase, int generation = 1)
        {
            await using var command = connection.CreateCommand();
            var sourceParameters = testCase.Allowed.Select((_, i) => "@source" + i).ToArray();
            // 参数数量取自受控允许集；最多读取上限加一，超限失败而非静默截取。
            command.CommandText = (provider == "mysql" ? "SELECT " : "SELECT TOP (4097) ")
                + "ChunkId, ModelKey, Generation, VectorPayload FROM fn_ai_retrieval_probe_chunk WHERE ScopeKey=@scope AND ModelKey=@model AND Generation=@generation AND SourceVersion IN ("
                + string.Join(",", sourceParameters) + ") ORDER BY ChunkId"
                + (provider == "mysql" ? " LIMIT 4097;" : ";");
            Add(command, "@scope", testCase.TenantScope); Add(command, "@model", ProbeCorpus.Model); Add(command, "@generation", generation);
            for (var i = 0; i < testCase.Allowed.Length; i++) Add(command, sourceParameters[i], testCase.Allowed[i]);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var reader = await command.ExecuteReaderAsync(timeout.Token);
            var candidates = new List<ProbeVector>();
            while (await reader.ReadAsync(timeout.Token))
                candidates.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), Decode((byte[])reader.GetValue(3))));
            return ProbeVectorSearch.Rank(ProbeVectorSearch.Embed(testCase.Question), candidates, ProbeCorpus.Model, generation, 4096, 5, timeout.Token);
        }
        var recall = await ProbeCorpus.CheckRetrievalAsync(chunks, cases, c => QueryAsync(c), observations);
        await ProbeCorpus.CheckBroadRankingAsync(chunks, cases, c => QueryAsync(c), observations);
        var number = cases.Single(x => x.CaseId == "exact-number");
        observations.Add(new("identifier", (await QueryAsync(number)).Contains("ticket-1", StringComparer.Ordinal), "exact synthetic identifier retrieved"));
        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM fn_ai_retrieval_probe_chunk WHERE ScopeKey=@scope AND ChunkId=@chunk;";
            Add(delete, "@scope", "tenant-a"); Add(delete, "@chunk", "ticket-1");
            await delete.ExecuteNonQueryAsync();
            observations.Add(new("delete", (await QueryAsync(number)).Length == 0, "deleted row cannot be retrieved"));
            observations.Add(new("delete-idempotent", await delete.ExecuteNonQueryAsync() == 0, "repeat deletion changes no rows"));
        }
        await InsertAsync(chunks.Single(x => x.ChunkId == "ticket-1"), 2);
        observations.Add(new("rebuild", (await QueryAsync(number, 2)).Contains("ticket-1", StringComparer.Ordinal)
            && (await QueryAsync(number, 1)).Length == 0, "new generation rebuilt; old generation stays absent"));

        // MySQL 单独 SLEEP（包括常量投影）被中断可能正常返回；真实行谓词检验驱动的异常映射。
        var delay = provider == "mysql" ? "SELECT 1 FROM fn_ai_retrieval_probe_chunk WHERE SLEEP(10);" : "WAITFOR DELAY '00:00:10'; SELECT 1;";
        Console.WriteLine("验证数据库命令超时。");
        await using (var timeoutCommand = connection.CreateCommand())
        {
            timeoutCommand.CommandText = delay; timeoutCommand.CommandTimeout = 1;
            try { await timeoutCommand.ExecuteScalarAsync(); observations.Add(new("timeout", false, "slow command unexpectedly completed")); }
            catch (SqlException ex) when (ex.Number == -2) { observations.Add(new("timeout", true, "driver command timeout")); }
            catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.CommandTimeoutExpired) { observations.Add(new("timeout", true, "driver command timeout")); }
        }
        await using (var cancelCommand = connection.CreateCommand())
        {
            Console.WriteLine("验证数据库在途取消。");
            cancelCommand.CommandText = delay;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var elapsed = Stopwatch.StartNew();
            try { await cancelCommand.ExecuteScalarAsync(cancellation.Token); observations.Add(new("cancel", false, "cancelled command unexpectedly completed")); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { observations.Add(new("cancel", elapsed.Elapsed < TimeSpan.FromSeconds(3), "in-flight command cancelled before delayed completion")); }
            // 此处仅为受控 WAITFOR 实验接纳已观察的驱动取消形态；不作为生产写入完成判断。
            catch (SqlException ex) when (cancellation.IsCancellationRequested && ex.Number == 0 && ex.State == 0 && ex.Class == 11)
            { observations.Add(new("cancel", elapsed.Elapsed < TimeSpan.FromSeconds(3), "in-flight WAITFOR cancelled; SqlException number=0 state=0 class=11")); }
        }
        observations.Add(new("connection-recovery", Convert.ToInt32(await ScalarAsync(connection, "SELECT 1;")) == 1, "connection usable after timeout/cancel"));
        if (provider == "sqlserver")
            observations.Add(new("fulltext-observation", true, "IsFullTextInstalled=" + Convert.ToString(await ScalarAsync(connection, "SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled');"))));
        return (version, recall);
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 30;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 30;
        return await command.ExecuteScalarAsync();
    }

    private static byte[] Encode(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        for (var i = 0; i < vector.Length; i++) BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(i * sizeof(float)), vector[i]);
        return bytes;
    }

    private static float[] Decode(byte[] bytes)
    {
        if (bytes.Length != ProbeCorpus.Dimension * sizeof(float)) throw new InvalidDataException("向量载荷长度错误。");
        return Enumerable.Range(0, ProbeCorpus.Dimension).Select(i => BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(i * sizeof(float)))).ToArray();
    }
}
