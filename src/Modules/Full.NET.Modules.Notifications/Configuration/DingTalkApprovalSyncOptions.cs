using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Configuration;

/// <summary>钉钉审批镜像同步的闭合配置；只支持固定首种 processCode 场景。</summary>
public sealed class DingTalkApprovalSyncOptions
{
    /// <summary>配置节路径。</summary>
    public const string SectionName = "Notifications:Providers:DingTalk:Workflow";

    /// <summary>是否启用镜像同步与轮询。</summary>
    public bool Enabled { get; init; }

    /// <summary>钉钉审批模板 processCode。</summary>
    public string ProcessCode { get; init; } = string.Empty;

    /// <summary>微应用 AgentId。</summary>
    public long AgentId { get; init; }

    /// <summary>出站 AppKey。</summary>
    public string AppKey { get; init; } = string.Empty;

    /// <summary>出站 AppSecret 的 env:// 引用。</summary>
    public string AppSecretReference { get; init; } = string.Empty;

    /// <summary>回调验签 Secret 的 env:// 引用。</summary>
    public string CallbackSecretReference { get; init; } = string.Empty;

    /// <summary>轮询间隔秒数。</summary>
    public int PollIntervalSeconds { get; init; } = 60;
}

/// <summary>校验钉钉审批镜像同步配置在启用时必须闭合。</summary>
internal sealed class DingTalkApprovalSyncOptionsValidator : IValidateOptions<DingTalkApprovalSyncOptions>
{
    public ValidateOptionsResult Validate(string? name, DingTalkApprovalSyncOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.ProcessCode)
            || options.ProcessCode.Length > 128
            || options.AgentId <= 0
            || string.IsNullOrWhiteSpace(options.AppKey)
            || options.AppKey.Length > 64
            || string.IsNullOrWhiteSpace(options.AppSecretReference)
            || options.AppSecretReference.Length > 256
            || string.IsNullOrWhiteSpace(options.CallbackSecretReference)
            || options.CallbackSecretReference.Length > 256
            || options.PollIntervalSeconds is < 15 or > 3600)
        {
            return ValidateOptionsResult.Fail(
                "DingTalk approval sync options are invalid when enabled.");
        }

        return ValidateOptionsResult.Success;
    }
}
