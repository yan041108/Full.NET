using BenchmarkDotNet.Running;
using Full.NET.Benchmarks;
using Full.NET.Benchmarks.Auditing;
using Full.NET.Benchmarks.MixedLoad;
using Full.NET.Benchmarks.Outbox;

if (args.FirstOrDefault() is "outbox-capacity")
{
    var outboxArguments = args.Skip(1).ToArray();
    if (outboxArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(OutboxCapacityOptions.HelpText);
        return;
    }

    await OutboxCapacityRunner.RunAsync(
        OutboxCapacityOptions.Parse(outboxArguments));
}
else if (args.FirstOrDefault() is "mixed-load")
{
    var mixedLoadArguments = args.Skip(1).ToArray();
    if (mixedLoadArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(MixedLoadOptions.HelpText);
        return;
    }

    await MixedLoadRunner.RunAsync(MixedLoadOptions.Parse(mixedLoadArguments));
}
else if (args.FirstOrDefault() is "audit-query")
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
