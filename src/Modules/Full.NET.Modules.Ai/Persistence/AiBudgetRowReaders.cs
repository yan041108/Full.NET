#if FULLNET_AOT_COMPILE || FULLNET_AI_ROW_READER_TESTS
using System.Data.Common;
using System.Globalization;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>仅进入原生编译闭包；测试链接同一源文件，普通业务模块不引入 ADO.NET 依赖。</summary>
internal static class AiBudgetRowReaders
{
#if FULLNET_AOT_COMPILE
    internal static void Register()
    {
        var registrar = new Full.NET.Data.Dapper.DapperAotMaterializerRegistrar();
        registrar.Register<AiOperationBudgetRecord>(ReadOperation);
        registrar.Register<AiOperationBudgetTotals>(ReadTotals);
        registrar.Register<AiModelPriceRecord>(ReadPrice);
        registrar.Register<AiTenantQuotaRecord>(ReadQuota);
    }
#endif
    internal static AiOperationBudgetTotals ReadTotals(DbDataReader reader) => new()
    {
        MonthlyRequests = Integer(reader, "MonthlyRequests"), MonthlyTokens = Integer(reader, "MonthlyTokens"),
        RunRequests = Integer(reader, "RunRequests"), RunTokens = Integer(reader, "RunTokens")
    };

    internal static AiModelPriceRecord ReadPrice(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), Currency = Text(reader, "Currency"),
        InputPerMillion = Amount(reader, "InputPerMillion"), OutputPerMillion = Amount(reader, "OutputPerMillion"),
        CachedInputPerMillion = Amount(reader, "CachedInputPerMillion")
    };

    internal static AiOperationBudgetRecord ReadOperation(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), ModelConfigId = reader.GetGuid(reader.GetOrdinal("ModelConfigId")),
        RunId = Null(reader, "RunId") ? null : reader.GetGuid(reader.GetOrdinal("RunId")),
        RequestHash = Text(reader, "RequestHash"), QuotaMonthKey = Text(reader, "QuotaMonthKey"),
        ReservedTokens = Integer(reader, "ReservedTokens"), ReservedCost = Null(reader, "ReservedCost") ? null : Amount(reader, "ReservedCost"),
        Currency = Null(reader, "Currency") ? null : Text(reader, "Currency"),
        PriceVersionId = Null(reader, "PriceVersionId") ? null : reader.GetGuid(reader.GetOrdinal("PriceVersionId")),
        InputPerMillion = Null(reader, "InputPerMillion") ? null : Amount(reader, "InputPerMillion"),
        OutputPerMillion = Null(reader, "OutputPerMillion") ? null : Amount(reader, "OutputPerMillion"),
        CachedInputPerMillion = Null(reader, "CachedInputPerMillion") ? null : Amount(reader, "CachedInputPerMillion"),
        InputTokens = Null(reader, "InputTokens") ? null : Integer(reader, "InputTokens"),
        OutputTokens = Null(reader, "OutputTokens") ? null : Integer(reader, "OutputTokens"),
        CachedInputTokens = Null(reader, "CachedInputTokens") ? null : Integer(reader, "CachedInputTokens"),
        UsageStatus = Text(reader, "UsageStatus"), Outcome = Text(reader, "Outcome"),
        LegacyTracked = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("LegacyTracked")), CultureInfo.InvariantCulture)
    };

    private static AiTenantQuotaRecord ReadQuota(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), TenantId = reader.GetGuid(reader.GetOrdinal("TenantId")),
        MonthlyTokenLimit = Null(reader, "MonthlyTokenLimit") ? null : Integer(reader, "MonthlyTokenLimit"),
        MonthlyRequestLimit = Null(reader, "MonthlyRequestLimit") ? null : Integer(reader, "MonthlyRequestLimit"),
        UsedTokensThisMonth = Integer(reader, "UsedTokensThisMonth"), UsedRequestsThisMonth = Integer(reader, "UsedRequestsThisMonth"),
        QuotaMonthKey = Text(reader, "QuotaMonthKey"), IsEnabled = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsEnabled")), CultureInfo.InvariantCulture),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"), UpdatedAtUtc = Null(reader, "UpdatedAtUtc") ? null : Timestamp(reader, "UpdatedAtUtc"),
        Version = checked((int)Integer(reader, "Version"))
    };

    private static bool Null(DbDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name));
    private static string Text(DbDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));
    private static long Integer(DbDataReader reader, string name) => Convert.ToInt64(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);
    private static decimal Amount(DbDataReader reader, string name) => reader.GetDecimal(reader.GetOrdinal(name));
    private static DateTimeOffset Timestamp(DbDataReader reader, string name) => reader.GetValue(reader.GetOrdinal(name)) switch
    {
        DateTimeOffset value => value,
        DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
        _ => throw new InvalidOperationException("Invalid AI budget timestamp type.")
    };
}
#endif
