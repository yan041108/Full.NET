using BenchmarkDotNet.Running;
using Full.NET.Benchmarks;
using Full.NET.Benchmarks.Auditing;
using Full.NET.Benchmarks.Caching;
using Full.NET.Benchmarks.Data;
using Full.NET.Benchmarks.Jobs;
using Full.NET.Benchmarks.Kafka;
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
else if (args.FirstOrDefault() is "jobs-backlog-query")
{
    var jobsArguments = args.Skip(1).ToArray();
    if (jobsArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(JobsBacklogQueryBenchmarkOptions.HelpText);
        return;
    }

    var jobsOptions = JobsBacklogQueryBenchmarkOptions.Parse(
        jobsArguments);
    if (jobsOptions.Mode == JobsBacklogQueryBenchmarkMode.IndexAb)
    {
        await JobsBacklogIndexAbBenchmarkRunner.RunAsync(jobsOptions);
    }
    else
    {
        await JobsBacklogQueryBenchmarkRunner.RunAsync(jobsOptions);
    }
}
else if (args.FirstOrDefault() is "jobs-capacity")
{
    var capacityArguments = args.Skip(1).ToArray();
    if (capacityArguments.Contains(
            "--help",
            StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(JobsCapacityOptions.HelpText);
        return;
    }

    await JobsCapacityRunner.RunAsync(
        JobsCapacityOptions.Parse(capacityArguments));
}
else if (args.FirstOrDefault() is "outbox-write-profile")
{
    var profileArguments = args.Skip(1).ToArray();
    if (profileArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(OutboxWriteProfileOptions.HelpText);
        return;
    }

    await OutboxWriteProfileRunner.RunAsync(
        OutboxWriteProfileOptions.Parse(profileArguments));
}
else if (args.FirstOrDefault() is "kafka-capacity")
{
    var capacityArguments = args.Skip(1).ToArray();
    if (capacityArguments.Contains(
            "--help",
            StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(KafkaCapacityOptions.HelpText);
        return;
    }

    Environment.ExitCode = (int)await KafkaCapacityRunner.RunCommandAsync(
        capacityArguments);
}
else
{
    BenchmarkSwitcher
        .FromTypes([
            typeof(SerializationBenchmarks),
            typeof(CacheAccessBoundaryBenchmarks),
            typeof(DapperAotCommandReuseBenchmarks),
        ])
        .Run(args);
}
