using BenchmarkDotNet.Running;
using Full.NET.Benchmarks;
using Full.NET.Benchmarks.Auditing;

if (args.FirstOrDefault() is "audit-query")
{
    var auditArguments = args.Skip(1).ToArray();
    if (auditArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(AuditingQueryBenchmarkOptions.HelpText);
        return;
    }

    var auditOptions = AuditingQueryBenchmarkOptions.Parse(auditArguments);
    if (auditOptions.Mode == AuditingQueryBenchmarkMode.CursorAb)
    {
        await AuditingCursorAbBenchmarkRunner.RunAsync(auditOptions);
    }
    else if (auditOptions.Mode == AuditingQueryBenchmarkMode.SqlServerPlanAb)
    {
        await AuditingSqlServerAbBenchmarkRunner.RunAsync(auditOptions);
    }
    else if (auditOptions.Mode is AuditingQueryBenchmarkMode.MySqlIndexAb
        or AuditingQueryBenchmarkMode.MySqlLateMaterializationAb)
    {
        await AuditingMySqlIndexAbBenchmarkRunner.RunAsync(auditOptions);
    }
    else
    {
        await AuditingQueryBenchmarkRunner.RunAsync(auditOptions);
    }
}
else
{
    BenchmarkSwitcher
        .FromTypes([typeof(SerializationBenchmarks)])
        .Run(args);
}
