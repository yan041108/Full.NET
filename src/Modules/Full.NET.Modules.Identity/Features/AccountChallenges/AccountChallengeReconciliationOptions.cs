using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.AccountChallenges;

/// <summary>Worker 挑战投递巡检策略；默认关闭，更新配置后在下一轮生效。</summary>
internal sealed class AccountChallengeReconciliationOptions
{
    public const string SectionName = "Identity:AccountChallenges:Reconciliation";
    public bool Enabled { get; set; }
    /// <summary>每轮最多读取的挑战数，范围 1～500，默认 100；每条候选最多一次 CAS。</summary>
    public int BatchSize { get; set; } = 100;
    /// <summary>每页间隔秒数，范围 30～3600，默认 60；全表收敛时间取决于数据规模和页数。</summary>
    public int PollSeconds { get; set; } = 60;
}

internal sealed class AccountChallengeReconciliationOptionsValidator : IValidateOptions<AccountChallengeReconciliationOptions>
{
    public ValidateOptionsResult Validate(string? name, AccountChallengeReconciliationOptions options) =>
        options.BatchSize is >= 1 and <= 500 && options.PollSeconds is >= 30 and <= 3600
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Identity:AccountChallenges:Reconciliation requires BatchSize 1..500 and PollSeconds 30..3600.");
}
