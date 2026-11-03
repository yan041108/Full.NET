using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Features.CompletePersonalSchedules;

/// <summary>静态工具只提交原独立批准的不可变意图；accepted不表示日程已完成。</summary>
internal sealed class ScheduleCompletionToolHandler(AiScheduleCompletionService service) : IAgentToolHandler
{
    public bool ValidateArguments(JsonElement arguments) => ScheduleCompletionArguments.Parse(arguments) is not null;
    public async ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken) =>
        JsonSerializer.SerializeToElement(await service.AcceptAsync(invocation, actor, cancellationToken).ConfigureAwait(false),
            AiJsonSerializerContext.Default.AiPersonalScheduleCompletionStatus);
}

/// <summary>参数只含目标和准确版本；拒绝重复、未知字段及空身份。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ScheduleCompletionArguments(Guid ScheduleId, int ExpectedVersion)
{
    // 展示与消费共用规范版本摘要，不能批准不可见版本或因两端格式漂移误判原批准。
    internal string ChangeSummary => FormattableString.Invariant($"pending -> completed; expected version: {ExpectedVersion}");

    internal static ScheduleCompletionArguments? Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 2
            || !value.TryGetProperty("scheduleId", out var id) || id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var target) || target == Guid.Empty
            || !value.TryGetProperty("expectedVersion", out var number) || number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var version) || version is < 1 or int.MaxValue) return null;
        return new(target, version);
    }
}
