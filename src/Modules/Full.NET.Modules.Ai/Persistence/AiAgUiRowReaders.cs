#if FULLNET_AOT_COMPILE || FULLNET_AI_ROW_READER_TESTS
using System.Data.Common;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>公开AG-UI读取的四种投影必须显式进入Native闭包，不能回退动态物化或把未知用量补零。</summary>
internal static partial class AiBudgetRowReaders
{
    internal static AiAgentEventSequenceBounds ReadAgUiSequenceBounds(DbDataReader reader) => new()
    { MaxSequence = Integer(reader, "MaxSequence") };

    internal static AiAgentEventRecord ReadAgUiEvent(DbDataReader reader) => new()
    {
        Sequence = Integer(reader, "Sequence"), EventType = Text(reader, "EventType"),
        PayloadVersion = checked((int)Integer(reader, "PayloadVersion")), Payload = Text(reader, "Payload"),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc")
    };

    internal static AiAgentStepSummaryRecord ReadAgUiStep(DbDataReader reader) => new()
    {
        StepKey = Text(reader, "StepKey"), Attempt = checked((int)Integer(reader, "Attempt")), StatusKey = Text(reader, "StatusKey"),
        InputTokens = Null(reader, "InputTokens") ? null : Integer(reader, "InputTokens"),
        OutputTokens = Null(reader, "OutputTokens") ? null : Integer(reader, "OutputTokens"),
        ErrorCode = Null(reader, "ErrorCode") ? null : Text(reader, "ErrorCode")
    };

    internal static AiAgentRunBudgetSummaryRecord ReadAgUiBudget(DbDataReader reader) => new()
    {
        InputTokens = Null(reader, "InputTokens") ? null : Integer(reader, "InputTokens"),
        OutputTokens = Null(reader, "OutputTokens") ? null : Integer(reader, "OutputTokens"),
        UsageStatus = Text(reader, "UsageStatus"), Outcome = Text(reader, "Outcome")
    };
}
#endif
