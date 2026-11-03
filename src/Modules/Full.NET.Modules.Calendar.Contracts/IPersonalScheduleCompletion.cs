using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Calendar.Contracts;

/// <summary>助手只允许完成本人准确pending版本；该用途不授予内容、时间或删除写权限。</summary>
public static class PersonalScheduleCompletionKeys
{
    /// <summary>与Calendar SetStatus权限同时成立的窄用途。</summary>
    public const string Permission = "calendar.personal_schedules.assistant_complete";
    /// <summary>静态工具身份；不能按客户端输入选择其他业务动作。</summary>
    public const string Tool = "calendar.personal_schedules.complete";
}

/// <summary>AI已消费独立批准的不可变意图；仅供模块间可信投递，不接受HTTP直接绑定。</summary>
/// <param name="RunId">真实静态运行身份。</param>
/// <param name="OperationId">该运行原写节点的稳定操作身份。</param>
/// <param name="ScheduleId">准确个人日程身份。</param>
/// <param name="ExpectedVersion">批准时准确pending版本。</param>
/// <param name="ApproverId">批准者，必须不同于原主体。</param>
/// <param name="Original">服务器持久化的原会话，不是客户端指定主体。</param>
/// <param name="ExpiresAtUtc">原意图执行期限；重投不得延长。</param>
public sealed record PersonalScheduleCompletionRequest(Guid RunId, Guid OperationId, Guid ScheduleId,
    int ExpectedVersion, Guid ApproverId, SessionBindingSnapshot Original, DateTimeOffset ExpiresAtUtc)
{
    /// <summary>准确载荷绑定；不同消息重投同一操作不改变摘要，不接受参数篡改。</summary>
    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
        "calendar-completion-v1", RunId.ToString("N"), OperationId.ToString("N"), ScheduleId.ToString("N"),
        ExpectedVersion.ToString(CultureInfo.InvariantCulture), ApproverId.ToString("N"), Original.UserId.ToString("N"),
        Original.TenantId?.ToString("N") ?? "host", Original.SessionId.ToString("N"),
        Hash(Original.SecurityStamp), Hash(Original.ActorScope), Hash(Original.EffectiveScope), Hash(Original.SessionKind),
        ExpiresAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)))));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>Calendar本地事务保存的准确原操作事实；不含私人正文，completed只来自实际CAS。</summary>
/// <param name="OperationId">原操作。</param>
/// <param name="RequestDigest">原完整请求摘要。</param>
/// <param name="Outcome">completed或rejected。</param>
/// <param name="Reason">拒绝的稳定安全原因，成功为空。</param>
/// <param name="ActualVersion">实际完成后的准确版本；拒绝不返回当前资源版本。</param>
/// <param name="CompletedAtUtc">本操作实际完成时间；拒绝为空。</param>
public sealed record PersonalScheduleCompletionReceipt(Guid OperationId, string RequestDigest, string Outcome,
    string? Reason, int? ActualVersion, DateTimeOffset? CompletedAtUtc)
{
    /// <summary>只接纳准确原操作及闭合结果组合；拒绝伪完成、缺失版本或混用其它请求的回执。</summary>
    public bool IsFor(PersonalScheduleCompletionRequest request) => request.ExpectedVersion is > 0 and < int.MaxValue
        && request.RunId != Guid.Empty && request.OperationId != Guid.Empty && request.ScheduleId != Guid.Empty
        && OperationId == request.OperationId && RequestDigest == request.Digest()
        && (Outcome == "completed" && Reason is null && ActualVersion == request.ExpectedVersion + 1 && CompletedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero
            || Outcome == "rejected" && Reason is "authorization_unavailable" or "intent_expired" or "target_changed" && ActualVersion is null && CompletedAtUtc is null);
}

/// <summary>Calendar拥有的受控写接收方；所有Contract调用均位于调用者事务外。</summary>
public interface IPersonalScheduleCompletion
{
    /// <summary>按原会话当前权限核对本人准确pending目标，不交付正文。</summary>
    Task<bool> CanCompleteAsync(Guid scheduleId, int expectedVersion, SessionBindingSnapshot original, CancellationToken cancellationToken = default);
    /// <summary>接收原意图并在Calendar自己的事务内CAS/去重/保存回执；重复只返回原事实。</summary>
    Task<PersonalScheduleCompletionReceipt> ReceiveAsync(PersonalScheduleCompletionRequest request, CancellationToken cancellationToken = default);
    /// <summary>仅按完整原操作读取已存事实，不能依据当前日程状态猜测或执行；失效原会话不抹掉已提交事实。</summary>
    Task<PersonalScheduleCompletionReceipt?> FindReceiptAsync(PersonalScheduleCompletionRequest request, CancellationToken cancellationToken = default);
}
