using System.Text.Json;

namespace Full.NET.ArchitectureTests;

/// <summary>覆盖查询许可的实际源范围与失败关闭条件，不将登记当作写入白名单。</summary>
[TestClass]
public sealed class ControlledModuleReadQueryTests
{
    private const string FilePath = "src/Modules/Full.NET.Modules.Alpha/Features/ListOrders/Queries/OrderQuerySql.cs";
    private const string ConsumerPath = "src/Modules/Full.NET.Modules.Alpha/Features/ListOrders/Queries/OrderQuery.cs";
    private const string Sql = "SELECT o.Id, u.UserName FROM fn_alpha_order o JOIN fn_beta_user u ON u.TenantId = o.TenantId AND u.Id = o.UserId WHERE o.TenantId = @TenantId AND u.UserName LIKE @Name ORDER BY o.Id";

    private static ControlledModuleReadQueryScanner.Entry Registration(string sql = Sql) => new(
        "alpha", FilePath, "OrderQuerySql.Page", "alpha.orders.page",
        ControlledModuleReadQueryScanner.Hash(sql), "admin-list",
        [new("beta", "fn_beta_user", ["TenantId", "Id", "UserName"])],
        "租户等值关联，订单数据范围由 Query 加入；只显示允许的用户名，不返回凭据。当前用户名不改写订单历史快照。");

    private static string Source(string sql = Sql) =>
        "using Full.NET.Data.Abstractions; namespace Full.NET.Modules.Alpha; internal static class OrderQuerySql { public static readonly SqlStatement Page = new(\"alpha.orders.page\", "
        + JsonSerializer.Serialize(sql) + ", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId); }";

    private static Dictionary<string, string> Sources(string sql = Sql) => new()
    {
        [FilePath] = Source(sql),
        [ConsumerPath] = "internal class OrderQuery { object Statement => OrderQuerySql.Page; }",
    };

    private static Dictionary<string, string[]> Dependencies() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha"] = ["beta"],
        ["beta"] = [],
    };

    [TestMethod]
    public void Registered_query_allows_only_its_SQL_literal_and_supports_transitive_dependencies()
    {
        var sources = Sources();
        var result = ControlledModuleReadQueryScanner.Analyze(new(1, [Registration()]), sources, Dependencies());
        Assert.HasCount(0, result.Violations, string.Join("\n", result.Violations));
        Assert.HasCount(1, result.Allowed);
        var span = result.Allowed[0].Span;
        Assert.IsTrue(span.Contains(sources[FilePath].IndexOf("fn_beta_user", StringComparison.Ordinal)));
        Assert.IsFalse(span.Contains(sources[FilePath].IndexOf("SqlDataScope", StringComparison.Ordinal)));

        var dependencies = Dependencies();
        dependencies["alpha"] = ["bridge"];
        dependencies["bridge"] = ["beta"];
        Assert.HasCount(0, ControlledModuleReadQueryScanner.Analyze(new(1, [Registration()]), sources, dependencies).Violations);
    }

    [TestMethod]
    public void Catalog_rejects_missing_duplicate_stale_wildcard_drifted_and_unscoped_entries()
    {
        var entry = Registration();
        var invalid = new[]
        {
            entry with { File = "**/OrderQuerySql.cs" },
            entry with { File = FilePath.Replace("Queries/", "Persistence/", StringComparison.Ordinal) },
            entry with { SourceModule = "beta" },
            entry with { Member = "OrderQuerySql.Missing" },
            entry with { StatementName = "alpha.other" },
            entry with { SqlSha256 = new string('0', 64) },
            entry with { Purpose = "command-validation" },
            entry with { SecurityNotes = "" },
            entry with { Dependencies = [] },
            entry with { Dependencies = [new("beta", "fn_beta_missing", ["Id"])] },
            entry with { Dependencies = [new("beta", "fn_beta_user", ["Id"])] },
            entry with { Dependencies = [new("beta", "fn_beta_user", ["Id", "TenantId", "UserName", "PasswordHash"])] },
            entry with { Dependencies = [new("gamma", "fn_beta_user", ["Id", "TenantId", "UserName"])] },
        };
        foreach (var candidate in invalid)
        {
            var result = ControlledModuleReadQueryScanner.Analyze(new(1, [candidate]), Sources(), Dependencies());
            Assert.IsNotEmpty(result.Violations, JsonSerializer.Serialize(candidate));
            Assert.HasCount(0, result.Allowed);
        }

        Assert.IsNotEmpty(ControlledModuleReadQueryScanner.Analyze(new(1, [entry, entry]), Sources(), Dependencies()).Violations);
        Assert.IsNotEmpty(ControlledModuleReadQueryScanner.Analyze(new(1, [entry]), new Dictionary<string, string>(), Dependencies()).Violations);
        Assert.IsNotEmpty(ControlledModuleReadQueryScanner.Analyze(new(2, []), Sources(), Dependencies()).Violations);
        Assert.HasCount(0, ControlledModuleReadQueryScanner.Analyze(new(1, []), Sources(), Dependencies()).Allowed);
        Assert.IsNotEmpty(ControlledModuleReadQueryScanner.Analyze(new(1, [entry]), Sources(), new Dictionary<string, string[]>()).Violations);
    }

    [TestMethod]
    public void Query_registration_does_not_allow_other_members_dynamic_SQL_or_transaction_consumers()
    {
        var entry = Registration();
        var sources = Sources();
        sources[FilePath] = sources[FilePath].Replace("; }", "; public static readonly SqlStatement Other = new(\"alpha.other\", \"DELETE FROM fn_beta_user\", SqlDataScope.Global); }", StringComparison.Ordinal);
        var result = ControlledModuleReadQueryScanner.Analyze(new(1, [entry]), sources, Dependencies());
        Assert.IsFalse(result.Allowed.Any(read => read.Span.Contains(sources[FilePath].IndexOf("DELETE", StringComparison.Ordinal))));
        Assert.IsNotEmpty(ControlledModuleReadQueryScanner.FindForbiddenSql("alpha", FilePath, sources[FilePath]));

        sources = Sources();
        sources[FilePath] = sources[FilePath].Replace(JsonSerializer.Serialize(Sql), "BuildSql()", StringComparison.Ordinal);
        Assert.IsNotEmpty(ControlledModuleReadQueryScanner.Analyze(new(1, [entry]), sources, Dependencies()).Violations);
        sources = Sources();
        sources[ConsumerPath] = "class OrderQuery { ICommandTransaction transaction; object Statement => OrderQuerySql.Page; }";
        Assert.IsNotEmpty(ControlledModuleReadQueryScanner.Analyze(new(1, [entry]), sources, Dependencies()).Violations);
        sources = Sources();
        sources["src/Modules/Full.NET.Modules.Alpha/Features/CreateOrder/Command.cs"] = sources[ConsumerPath];
        Assert.IsNotEmpty(ControlledModuleReadQueryScanner.Analyze(new(1, [entry]), sources, Dependencies()).Violations);
        sources = Sources();
        sources[FilePath] = sources[FilePath].Replace("; }", "; static void Execute() {} }", StringComparison.Ordinal);
        Assert.IsNotEmpty(ControlledModuleReadQueryScanner.Analyze(new(1, [entry]), sources, Dependencies()).Violations);
    }

    [TestMethod]
    [DataRow("SELECT u.Id FROM fn_beta_user u; DELETE FROM fn_beta_user")]
    [DataRow("UPDATE fn_beta_user SET UserName = @Name WHERE Id = @Id")]
    [DataRow("DELETE u FROM fn_beta_user u WHERE u.Id = @Id")]
    [DataRow("INSERT INTO fn_beta_user (Id) VALUES (@Id)")]
    [DataRow("INSERT INTO fn_alpha_order (Id) SELECT u.Id FROM fn_beta_user u")]
    [DataRow("SELECT u.Id INTO fn_alpha_copy FROM fn_beta_user u")]
    [DataRow("SELECT u.Id FROM fn_beta_user u INTO OUTFILE '/tmp/users'")]
    [DataRow("WITH data AS (SELECT Id FROM fn_beta_user) DELETE FROM data")]
    [DataRow("MERGE fn_beta_user AS t USING fn_alpha_order AS s ON t.Id = s.Id WHEN MATCHED THEN DELETE;")]
    [DataRow("SELECT u.Id FROM fn_beta_user u FOR UPDATE")]
    [DataRow("SELECT u.Id FROM fn_beta_user u WITH (UPDLOCK)")]
    [DataRow("BEGIN TRANSACTION; SELECT u.Id FROM fn_beta_user u; COMMIT;")]
    [DataRow("SELECT u.Id FROM fn_beta_user u /*!50000 FOR UPDATE */")]
    [DataRow("SELECT u.Id FROM fn_beta_user u /*M! FOR UPDATE */")]
    [DataRow("EXEC('SELECT Id FROM fn_beta_user')")]
    [DataRow("SELECT @Id = u.Id FROM fn_beta_user u")]
    [DataRow("SELECT u.Id, @x := 1 FROM fn_beta_user u")]
    [DataRow("SELECT u.Id FROM other.fn_beta_user u")]
    [DataRow("SELECT dbo.Mutate(u.Id) FROM fn_beta_user u")]
    [DataRow("SELECT GET_LOCK('lock', 10), u.Id FROM fn_beta_user u")]
    [DataRow("SELECT u.Id FROM fn_beta_user u --no-space\n FOR UPDATE")]
    [DataRow("SELECT NEXT VALUE FOR fn_beta_sequence FROM fn_beta_user u")]
    [DataRow("SELECT u.Id FROM fn_beta_user u REPLACE fn_beta_user VALUES (1)")]
    [DataRow("SELECT u.Id FROM fn_beta_user u WHERE EXISTS (SELECT 1 FROM OPENJSON(dbo.ReadFile()) ids)")]
    [DataRow("SELECT u.Id FROM fn_beta_user u WHERE EXISTS (SELECT 1 FROM OPENJSON(@Rows, '$') ids)")]
    [DataRow("SELECT u.Id FROM fn_beta_user u WHERE EXISTS (SELECT 1 FROM JSON_TABLE(LOAD_FILE('/tmp/users'), '$[*]' COLUMNS (Id INT PATH '$')) ids)")]
    [DataRow("SELECT u.Id FROM fn_beta_user u WHERE EXISTS (SELECT 1 FROM JSON_TABLE(@Rows, @Path COLUMNS (Id INT PATH '$')) ids)")]
    public void Read_only_gate_rejects_mutations_locks_transactions_and_hidden_side_effects(string sql)
    {
        Assert.IsNull(ControlledModuleReadQueryScanner.ReadOnlySql(sql), sql);
        Assert.IsNotEmpty(ControlledModuleReadQueryScanner.FindForbiddenSql("alpha", FilePath, Source(sql)));
    }

    [TestMethod]
    [DataRow("SELECT u.Id, 'DELETE FROM ignored' AS Label FROM fn_beta_user u;")]
    [DataRow("/* 查询说明 */ SELECT u.Id FROM fn_beta_user u -- 注释\n WHERE u.TenantId = @TenantId")]
    [DataRow("SELECT u.[Id] FROM [fn_beta_user] u WHERE u.[TenantId] = @TenantId")]
    [DataRow("SELECT u.`Id` FROM `fn_beta_user` u WHERE u.`TenantId` = @TenantId")]
    [DataRow("SELECT u.Id FROM fn_beta_user u ORDER BY u.Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY")]
    [DataRow("SELECT u.Id FROM fn_beta_user u WHERE EXISTS (SELECT 1 FROM OPENJSON(@Ids) WITH (Id uniqueidentifier '$') ids WHERE ids.Id = u.Id)")]
    [DataRow("SELECT u.Id FROM fn_beta_user u WHERE EXISTS (SELECT 1 FROM JSON_TABLE(@Ids, '$[*]' COLUMNS (Id VARCHAR(36) PATH '$')) ids WHERE UNHEX(REPLACE(ids.Id, '-', '')) = u.Id)")]
    public void Read_only_gate_accepts_parameterized_selects_and_ordinary_literals(string sql)
    {
        Assert.IsNotNull(ControlledModuleReadQueryScanner.ReadOnlySql(sql));
    }
}
