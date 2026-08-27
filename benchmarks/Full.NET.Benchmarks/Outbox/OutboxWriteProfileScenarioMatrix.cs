namespace Full.NET.Benchmarks.Outbox;

internal sealed record OutboxWriteProfileScenario(
    OutboxWriteProfileTarget Target,
    OutboxWriteProfileCommandPath CommandPath,
    int Concurrency,
    int Repetition);

/// <summary>
/// 为 A/B 重复轮次交替命令路径顺序，降低数据库热身与时间漂移对因果比较的偏差。
/// </summary>
internal static class OutboxWriteProfileScenarioMatrix
{
    public static IReadOnlyList<OutboxWriteProfileScenario> Create(
        OutboxWriteProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var scenarios = new List<OutboxWriteProfileScenario>(
            options.Targets.Count
            * options.ConcurrencyLevels.Count
            * options.Repetitions
            * options.CommandPaths.Count);
        foreach (var target in options.Targets)
        {
            foreach (var concurrency in options.ConcurrencyLevels)
            {
                for (var repetition = 1;
                     repetition <= options.Repetitions;
                     repetition++)
                {
                    var paths = repetition % 2 == 0
                        ? options.CommandPaths.Reverse()
                        : options.CommandPaths;
                    foreach (var commandPath in paths)
                    {
                        scenarios.Add(new OutboxWriteProfileScenario(
                            target,
                            commandPath,
                            concurrency,
                            repetition));
                    }
                }
            }
        }

        return scenarios;
    }
}
