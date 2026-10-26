using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Serialization;

namespace Full.NET.Modules.SerialNumbers.Features.WorkflowCommands;

/// <summary>先验证闭合快照，再计算业务输入摘要；JSON空白和字段顺序不产生另一份授权输入。</summary>
internal sealed record SerialRuleCommandSnapshot(string InputHash, long ExpectedVersion, UpdateSerialNumberRuleRequest? Update)
{
    public static bool TryCreate(DataApprovalScenarioCommand command, out SerialRuleCommandSnapshot? snapshot)
    {
        snapshot = null;
        if (command.CommandId == Guid.Empty || command.ApprovalRequestId == Guid.Empty || command.TargetEntityId == Guid.Empty ||
            command.RequestedByUserId == Guid.Empty || command.SnapshotJson is null || Encoding.UTF8.GetByteCount(command.SnapshotJson) > 32768 ||
            command.ScenarioKey is not (DataApprovalScenarioKeys.SerialRuleHostUpdate or DataApprovalScenarioKeys.SerialRuleHostDisable)) return false;
        try
        {
            using var document = JsonDocument.Parse(command.SnapshotJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) return false;
            if (!document.RootElement.TryGetProperty("version", out var versionValue) || versionValue.ValueKind != JsonValueKind.Number ||
                !versionValue.TryGetInt64(out _)) return false;
            string canonical; long version; UpdateSerialNumberRuleRequest? update = null;
            if (command.ScenarioKey == DataApprovalScenarioKeys.SerialRuleHostUpdate)
            {
                if (properties.Any(item => item.Name is not ("displayName" or "description" or "scope" or "resetInterval" or "pattern" or
                    "minimumValue" or "maximumValue" or "displayOrder" or "isEnabled" or "version"))) return false;
                update = JsonSerializer.Deserialize(command.SnapshotJson, SerialNumbersJsonSerializerContext.Default.UpdateSerialNumberRuleRequest);
                if (update is null) return false;
                version = update.Version;
                canonical = JsonSerializer.Serialize(update, SerialNumbersJsonSerializerContext.Default.UpdateSerialNumberRuleRequest);
            }
            else
            {
                if (properties.Length != 1 || properties[0].Name != "version") return false;
                var disable = JsonSerializer.Deserialize(command.SnapshotJson, SerialNumbersJsonSerializerContext.Default.ChangeSerialNumberRuleStatusRequest);
                if (disable is null) return false;
                version = disable.Version;
                canonical = JsonSerializer.Serialize(disable, SerialNumbersJsonSerializerContext.Default.ChangeSerialNumberRuleStatusRequest);
            }
            if (version < 1 || version > long.MaxValue - 2) return false;
            var fingerprint = $"{command.ApprovalRequestId:N}\n{command.ScenarioKey}\n{command.TargetEntityId:N}\n{command.RequestedByUserId:N}\n{canonical}";
            snapshot = new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))), version, update);
            return true;
        }
        catch (JsonException) { return false; }
    }
}
