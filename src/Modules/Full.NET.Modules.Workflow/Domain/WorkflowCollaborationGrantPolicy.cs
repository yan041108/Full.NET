using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>源只声明自己同意共享的字段及目标流程键，目标用户和目标版本由目标作用域接受时固化。</summary>
internal sealed record WorkflowCollaborationOfferPolicy(int SchemaVersion, Guid SourceDefinitionId, Guid SourceFormVersionId,
    string TargetWorkflowKey, IReadOnlyList<string> InputFieldKeys, IReadOnlyList<string> OutputFieldKeys, int MaximumTargetActivations, DateTimeOffset ExpiresAtUtc)
{
    public static bool TryRead(JsonElement value, out WorkflowCollaborationOfferPolicy? policy)
    {
        policy = null;
        if (!WorkflowVotePolicy.UniqueObject(value) || value.EnumerateObject().Count() != 8 ||
            value.EnumerateObject().Any(property => property.Name is not ("schemaVersion" or "sourceDefinitionId" or "sourceFormVersionId" or "targetWorkflowKey" or "inputFieldKeys" or "outputFieldKeys" or "maximumTargetActivations" or "expiresAtUtc")) ||
            !value.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32Safe(out var version) || version != 1 ||
            !GuidValue(value, "sourceDefinitionId", out var definition) || !GuidValue(value, "sourceFormVersionId", out var form) ||
            !value.TryGetProperty("targetWorkflowKey", out var target) || target.ValueKind != JsonValueKind.String || !WorkflowNodeKeyValidator.IsValid(target.GetString()) ||
            !Fields(value, "inputFieldKeys", out var input) || !Fields(value, "outputFieldKeys", out var output) ||
            !value.TryGetProperty("maximumTargetActivations", out var budget) || !budget.TryGetInt32Safe(out var maximum) || maximum is < 1 or > WorkflowActivityScheduler.InstanceActivityBudget ||
            !value.TryGetProperty("expiresAtUtc", out var expiry) || expiry.ValueKind != JsonValueKind.String || !expiry.TryGetDateTimeOffset(out var expires)) return false;
        policy = new(1, definition, form, target.GetString()!, input!, output!, maximum, NormalizeTime(expires)); return true;
    }
    /// <summary>双库均以微秒保存期限，先规范化再摘要，避免合法授权因第七位小数漂移而失效。</summary>
    internal static DateTimeOffset NormalizeTime(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
    internal static bool GuidValue(JsonElement value, string name, out Guid id)
    {
        id = default;
        return value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && item.TryGetGuid(out id) && id != Guid.Empty;
    }
    private static bool Fields(JsonElement value, string name, out IReadOnlyList<string>? fields)
    {
        fields = null;
        if (!value.TryGetProperty(name, out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 64) return false;
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
            if (item.ValueKind != JsonValueKind.String || !WorkflowSubprocessPolicy.Key(item.GetString()) || !result.Add(item.GetString()!)) return false;
        fields = Array.AsReadOnly(result.Order(StringComparer.Ordinal).ToArray()); return true;
    }
}

/// <summary>目标在自己的可信目录选择活动成员并绑定不可变流程版本；字段映射不得超出源同意范围。</summary>
internal sealed record WorkflowCollaborationAcceptancePolicy(int SchemaVersion, Guid TargetDefinitionVersionId, Guid StarterUserId, int MaximumActivities,
    IReadOnlyList<Guid> AllowedUserIds, IReadOnlyDictionary<string, string> InputFields, IReadOnlyDictionary<string, string> OutputFields, string TargetSummaryHash)
{
    public static bool TryRead(JsonElement value, WorkflowCollaborationOfferPolicy offer, out WorkflowCollaborationAcceptancePolicy? policy)
    {
        policy = null;
        if (!WorkflowVotePolicy.UniqueObject(value) || value.EnumerateObject().Count() != 8 ||
            value.EnumerateObject().Any(property => property.Name is not ("schemaVersion" or "targetDefinitionVersionId" or "starterUserId" or "maximumActivities" or "allowedUserIds" or "inputFields" or "outputFields" or "targetSummaryHash")) ||
            !value.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32Safe(out var version) || version != 1 ||
            !WorkflowCollaborationOfferPolicy.GuidValue(value, "targetDefinitionVersionId", out var definition) ||
            !WorkflowCollaborationOfferPolicy.GuidValue(value, "starterUserId", out var starter) ||
            !value.TryGetProperty("maximumActivities", out var budget) || !budget.TryGetInt32Safe(out var maximum) || maximum < 1 || maximum > offer.MaximumTargetActivations ||
            !value.TryGetProperty("allowedUserIds", out var users) || users.ValueKind != JsonValueKind.Array || users.GetArrayLength() is < 1 or > 100 ||
            !value.TryGetProperty("targetSummaryHash", out var summary) || summary.ValueKind != JsonValueKind.String || !Digest(summary.GetString()) ||
            !Map(value, "inputFields", out var input) || !Map(value, "outputFields", out var output) ||
            input!.Keys.Any(key => !offer.InputFieldKeys.Contains(key, StringComparer.Ordinal)) ||
            output!.Values.Any(key => !offer.OutputFieldKeys.Contains(key, StringComparer.Ordinal))) return false;
        var members = new HashSet<Guid>();
        foreach (var user in users.EnumerateArray())
            if (user.ValueKind != JsonValueKind.String || !user.TryGetGuid(out var id) || id == Guid.Empty || !members.Add(id)) return false;
        if (!members.Contains(starter)) return false;
        policy = new(1, definition, starter, maximum, Array.AsReadOnly(members.Order().ToArray()), input!, output!, summary.GetString()!); return true;
    }
    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Map(JsonElement value, string name, out IReadOnlyDictionary<string, string>? mapping)
    {
        mapping = null;
        if (!value.TryGetProperty(name, out var items) || !WorkflowVotePolicy.UniqueObject(items) || items.EnumerateObject().Count() > 64) return false;
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal); var destinations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in items.EnumerateObject())
        {
            if (!WorkflowSubprocessPolicy.Key(property.Name) || property.Value.ValueKind != JsonValueKind.String || !WorkflowSubprocessPolicy.Key(property.Value.GetString()) ||
                !destinations.Add(property.Value.GetString()!)) return false;
            result.Add(property.Name, property.Value.GetString()!);
        }
        mapping = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(result); return true;
    }
}

/// <summary>摘要固定双方同意的内容，身份权威仍来自登录上下文、授权记录及受信投递，不把摘要当数字签名。</summary>
internal static class WorkflowCollaborationGrantPolicy
{
    internal static string ScopeKey(Guid? tenantId) => tenantId is { } id ? $"tenant:{id:N}" : "host";
    internal static bool IsParticipant(WorkflowCollaborationGrantFact grant, string trustedScope) =>
        Shape(grant) && (grant.SourceTenantScopeKey == trustedScope || grant.TargetTenantScopeKey == trustedScope);

    public static bool TryAuthorize(WorkflowCollaborationGrantFact grant, string trustedScope, bool targetSide,
        long expectedRevision, DateTimeOffset now, out WorkflowCollaborationOfferPolicy? offer, out WorkflowCollaborationAcceptancePolicy? acceptance)
    {
        offer = null; acceptance = null;
        if (!Shape(grant) || grant.StatusKey != "active" || grant.Revision != expectedRevision || grant.Revision != 2 ||
            grant.TargetActorId is not { } targetActor || targetActor == Guid.Empty || grant.AcceptedAtUtc is not { } accepted || accepted < grant.CreatedAtUtc ||
            accepted > now || grant.CreatedAtUtc > now || grant.RevokedAtUtc is not null || grant.RevokedById is not null || grant.RevokedByScopeKey is not null || grant.ExpiresAtUtc <= now ||
            trustedScope != (targetSide ? grant.TargetTenantScopeKey : grant.SourceTenantScopeKey) || grant.AcceptanceJson is null || grant.PolicyHash is null || grant.TargetSummaryJson is null || grant.TargetSummaryHash is null ||
            grant.OfferJson.Length > 32_768 || grant.AcceptanceJson.Length > 32_768 || grant.TargetSummaryJson.Length > 65_536) return false;
        try
        {
            using var proposed = JsonDocument.Parse(grant.OfferJson); using var confirmed = JsonDocument.Parse(grant.AcceptanceJson);
            if (!WorkflowCollaborationOfferPolicy.TryRead(proposed.RootElement, out var parsedOffer) || parsedOffer!.ExpiresAtUtc != grant.ExpiresAtUtc || parsedOffer.SourceDefinitionId != grant.SourceDefinitionId || parsedOffer.SourceFormVersionId != grant.SourceFormVersionId ||
                !WorkflowCollaborationAcceptancePolicy.TryRead(confirmed.RootElement, parsedOffer, out var parsedAcceptance) || parsedAcceptance!.TargetDefinitionVersionId != grant.TargetDefinitionVersionId || parsedAcceptance.TargetSummaryHash != grant.TargetSummaryHash ||
                grant.TargetSummaryHash != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(grant.TargetSummaryJson))) ||
                grant.PolicyHash != Hash(grant.Id, grant.SourceTenantId, grant.TargetTenantId, grant.SourceActorId, targetActor, parsedOffer, parsedAcceptance!)) return false;
            offer = parsedOffer; acceptance = parsedAcceptance; return true;
        }
        catch (JsonException) { offer = null; acceptance = null; return false; }
    }

    public static string Hash(Guid id, Guid? sourceTenant, Guid? targetTenant, Guid sourceActor, Guid targetActor,
        WorkflowCollaborationOfferPolicy offer, WorkflowCollaborationAcceptancePolicy acceptance)
    {
        // 只排序固定集合，不去重掩盖非法输入；读取策略仍单独拒绝重复身份与映射。
        offer = offer with { InputFieldKeys = Array.AsReadOnly(offer.InputFieldKeys.Order(StringComparer.Ordinal).ToArray()),
            OutputFieldKeys = Array.AsReadOnly(offer.OutputFieldKeys.Order(StringComparer.Ordinal).ToArray()), ExpiresAtUtc = WorkflowCollaborationOfferPolicy.NormalizeTime(offer.ExpiresAtUtc) };
        acceptance = acceptance with { AllowedUserIds = Array.AsReadOnly(acceptance.AllowedUserIds.Order().ToArray()),
            InputFields = acceptance.InputFields.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            OutputFields = acceptance.OutputFields.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) };
        var proposed = JsonSerializer.Serialize(offer, WorkflowJsonSerializerContext.Default.WorkflowCollaborationOfferPolicy);
        var confirmed = JsonSerializer.Serialize(acceptance, WorkflowJsonSerializerContext.Default.WorkflowCollaborationAcceptancePolicy);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:D}\n{ScopeKey(sourceTenant)}\n{ScopeKey(targetTenant)}\n{sourceActor:D}\n{targetActor:D}\n{proposed}\n{confirmed}")));
    }
    private static bool Shape(WorkflowCollaborationGrantFact grant) => grant.Id != Guid.Empty && grant.SourceActorId != Guid.Empty && grant.SourceDefinitionId != Guid.Empty && grant.SourceFormVersionId != Guid.Empty &&
        grant.SourceTenantId != Guid.Empty && grant.TargetTenantId != Guid.Empty && grant.SourceTenantId != grant.TargetTenantId &&
        grant.SourceTenantScopeKey == ScopeKey(grant.SourceTenantId) && grant.TargetTenantScopeKey == ScopeKey(grant.TargetTenantId);
}

/// <summary>控制面授权事实；业务数据仍各属源/目标本地事务，第三作用域不能作为参与方。</summary>
internal sealed record WorkflowCollaborationGrantFact(Guid Id, Guid? SourceTenantId, Guid? TargetTenantId, string SourceTenantScopeKey,
    string TargetTenantScopeKey, long Revision, string StatusKey, Guid SourceActorId, Guid? TargetActorId,
    string OfferJson, string? AcceptanceJson, string? PolicyHash, DateTimeOffset ExpiresAtUtc, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AcceptedAtUtc, DateTimeOffset? RevokedAtUtc, Guid? RevokedById, string? RevokedByScopeKey, Guid SourceDefinitionId, Guid SourceFormVersionId, Guid? TargetDefinitionVersionId, string? TargetSummaryJson, string? TargetSummaryHash);
