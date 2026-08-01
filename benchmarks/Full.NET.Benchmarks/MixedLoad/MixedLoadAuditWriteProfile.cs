namespace Full.NET.Benchmarks.MixedLoad;

/// <summary>
/// 指定混合负载使用生产等价默认流量，或使用 Audit 写入归因流量。
/// </summary>
public enum MixedLoadWorkload
{
    Default = 0,
    AuditWrite = 1,
}

/// <summary>
/// 指定 Benchmark Host 在单次请求中实际执行的 Audit INSERT 种类。
/// </summary>
[Flags]
public enum MixedLoadAuditWriteProfile
{
    None = 0,
    Access = 1,
    Operation = 2,
    Exception = 4,
    All = Access | Operation | Exception,
}

/// <summary>
/// 将稳定 Audit Statement 名称映射到 Benchmark 专用写入组合。
/// </summary>
public static class MixedLoadAuditWritePolicy
{
    /// <summary>
    /// Benchmark Host 内部用于逐请求选择写入组合的 Header；生产 Host 不读取该值。
    /// </summary>
    public const string HeaderName = "X-FullNet-Benchmark-Audit-Writes";
    private const string AccessStatement = "auditing.insert_access_log";
    private const string OperationStatement = "auditing.insert_operation_log";
    private const string ExceptionStatement = "auditing.insert_exception_log";
    private const string BatchStatementPrefix =
        "auditing.insert_request_audit_batch.";
    private const string MicrobatchOperationStatement =
        "auditing.microbatch.insert_operation_log";
    private const string MicrobatchExceptionStatement =
        "auditing.microbatch.insert_exception_log";

    /// <summary>
    /// 判断当前 profile 是否应实际执行给定 Statement；非 Audit Statement 始终执行。
    /// </summary>
    public static bool ShouldExecute(
        MixedLoadAuditWriteProfile profile,
        string statementName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statementName);
        return statementName switch
        {
            AccessStatement => profile.HasFlag(MixedLoadAuditWriteProfile.Access),
            OperationStatement => profile.HasFlag(MixedLoadAuditWriteProfile.Operation),
            ExceptionStatement => profile.HasFlag(MixedLoadAuditWriteProfile.Exception),
            _ => true,
        };
    }

    /// <summary>
    /// 判断给定稳定 Statement 名称是否属于本轮归因的三类 Audit INSERT。
    /// </summary>
    public static bool IsAuditInsert(string statementName) =>
        GetObservedStatements(statementName).Count > 0;

    /// <summary>
    /// 将逐条或请求批量 Statement 展开为三类稳定观测名称，使批处理前后的归因口径保持一致。
    /// </summary>
    public static IReadOnlyList<string> GetObservedStatements(string statementName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statementName);
        return statementName switch
        {
            AccessStatement => [AccessStatement],
            OperationStatement => [OperationStatement],
            ExceptionStatement => [ExceptionStatement],
            MicrobatchOperationStatement => [OperationStatement],
            MicrobatchExceptionStatement => [ExceptionStatement],
            $"{BatchStatementPrefix}access" => [AccessStatement],
            $"{BatchStatementPrefix}operation" => [OperationStatement],
            $"{BatchStatementPrefix}exception" => [ExceptionStatement],
            $"{BatchStatementPrefix}access_operation" =>
                [AccessStatement, OperationStatement],
            $"{BatchStatementPrefix}access_exception" =>
                [AccessStatement, ExceptionStatement],
            $"{BatchStatementPrefix}operation_exception" =>
                [OperationStatement, ExceptionStatement],
            $"{BatchStatementPrefix}access_operation_exception" =>
                [AccessStatement, OperationStatement, ExceptionStatement],
            _ => [],
        };
    }

    /// <summary>
    /// 将 profile 转换为只在 Benchmark Host 内使用的低基数 Header 值。
    /// </summary>
    public static string GetToken(MixedLoadAuditWriteProfile profile) =>
        profile switch
        {
            MixedLoadAuditWriteProfile.None => "none",
            MixedLoadAuditWriteProfile.Access => "access",
            MixedLoadAuditWriteProfile.Operation => "operation",
            MixedLoadAuditWriteProfile.Exception => "exception",
            MixedLoadAuditWriteProfile.All => "all",
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
        };

    /// <summary>
    /// 解析 Benchmark Host 注入的 profile Header；未知值立即失败以防产生错误归因。
    /// </summary>
    public static MixedLoadAuditWriteProfile ParseToken(string value) =>
        value switch
        {
            "none" => MixedLoadAuditWriteProfile.None,
            "access" => MixedLoadAuditWriteProfile.Access,
            "operation" => MixedLoadAuditWriteProfile.Operation,
            "exception" => MixedLoadAuditWriteProfile.Exception,
            "all" => MixedLoadAuditWriteProfile.All,
            _ => throw new ArgumentException(
                $"未知的 Audit 写入 profile：{value}",
                nameof(value)),
        };
}
