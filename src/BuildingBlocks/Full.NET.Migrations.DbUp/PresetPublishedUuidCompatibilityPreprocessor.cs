using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DbUp.Engine;

namespace Full.NET.Migrations.DbUp;

/// <summary>固定预设下仅执行 203 中所选建表脚本对应的 UUID 校验与恢复，不改写历史资源。</summary>
internal sealed partial class PresetPublishedUuidCompatibilityPreprocessor(IReadOnlySet<string>? selectedScripts)
    : IScriptPreprocessor
{
    private const string MySqlDigest = "EA50F03F9E3C09FDC2762965EE65C09DDE193136D8C3C79E48B2A7583A94C909";
    private const string SqlServerDigest = "978B6F39B576FB5A0A5EAAFFFCC947ACC424E50A17D131378299E4C7798D3739";

    private static readonly IReadOnlyDictionary<string, string> TableOrigins = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fn_document_version_deletion_audit"] = "165_DocumentVersionDeletionAudit.sql",
        ["fn_document_access_log"] = "167_DocumentAccessLog.sql",
        ["fn_document_preview_task"] = "169_DocumentPreviewTask.sql",
        ["fn_import_export_task"] = "171_ImportExportTask.sql",
        ["fn_reporting_data_source"] = "175_ReportingDataSource.sql",
        ["fn_reporting_export_task"] = "180_ReportingExportTask.sql",
        ["fn_printing_template"] = "182_PrintingTemplate.sql",
        ["fn_printing_template_version"] = "182_PrintingTemplate.sql",
        ["fn_ai_model_config"] = "184_AiModelConfig.sql",
        ["fn_ai_tenant_quota"] = "184_AiModelConfig.sql",
        ["fn_ai_chat_session"] = "186_AiChatSession.sql",
        ["fn_ai_chat_message"] = "186_AiChatSession.sql",
        ["fn_ai_agent_tool_call"] = "188_AiAgentToolCall.sql",
        ["fn_payment_merchant_config"] = "190_PaymentMerchantConfig.sql",
        ["fn_payment_order"] = "190_PaymentMerchantConfig.sql",
        ["fn_payment_notify_receipt"] = "192_PaymentNotifyRefund.sql",
        ["fn_payment_refund"] = "192_PaymentNotifyRefund.sql",
        ["fn_goview_project"] = "195_GoViewProject.sql",
        ["fn_goview_project_version"] = "195_GoViewProject.sql",
        ["fn_k3cloud_connection_config"] = "197_K3CloudFoundation.sql",
        ["fn_k3cloud_document_sync"] = "197_K3CloudFoundation.sql",
        ["fn_ocr_provider_config"] = "199_OcrFoundation.sql",
        ["fn_ocr_id_card_task"] = "199_OcrFoundation.sql",
    };

    /// <summary>全文摘要和固定清单共同限定兼容变换；未选择的官方表若已存在则拒绝预设漂移。</summary>
    public string Process(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if (selectedScripts is null) return contents;
        var normalized = contents.ReplaceLineEndings("\n");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        if (digest == SqlServerDigest)
        {
            return SqlServerInvariantBlock().Replace(normalized, match =>
            {
                var table = match.Groups["table"].Value;
                return IsSelected(table) ? match.Value
                    : $"IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL\n"
                        + $"    THROW 51203, 'Preset migration inventory excludes existing table: {table}', 1;\n";
            });
        }
        if (digest != MySqlDigest) return contents;

        var excludedTables = new HashSet<string>(StringComparer.Ordinal);
        var result = MySqlTableBlock().Replace(normalized, match =>
        {
            var table = MySqlTableName().Match(match.Value).Groups["table"].Value;
            if (IsSelected(table)) return match.Value;
            if (!excludedTables.Add(table)) return "";
            // 不能用“表不存在就跳过”掩盖所选模块缺表；只有清单未选且数据库也不存在才允许省略。
            return "INSERT INTO fn_migrations_uuid_preflight (InvalidCount)\n"
                + "SELECT COUNT(*) FROM information_schema.TABLES\n"
                + $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}';\n\n";
        });
        return result;
    }

    private bool IsSelected(string table)
    {
        if (!TableOrigins.TryGetValue(table, out var script))
            throw new InvalidOperationException($"Unknown published UUID migration table: {table}");
        return selectedScripts!.Contains(script);
    }

    [GeneratedRegex(@"(?m)^IF NOT EXISTS \(SELECT 1 FROM sys\.columns WHERE object_id = OBJECT_ID\(N'dbo\.(?<table>fn_[a-z0-9_]+)'\)\n[^\n]+\n    THROW 51203, '[^'\n]+', 1;\n?")]
    private static partial Regex SqlServerInvariantBlock();

    [GeneratedRegex(@"(?ms)^-- (?:预检|转换) fn_[a-z0-9_]+\.[^\n]+\n.*?(?=^-- |\z)|^-- (?:只暂时移除本模块已知文档约束|恢复本模块文档关联)[^\n]+\n.*?DEALLOCATE PREPARE uuid_stmt;\n*")]
    private static partial Regex MySqlTableBlock();

    [GeneratedRegex(@"TABLE_NAME = '(?<table>fn_[a-z0-9_]+)'")]
    private static partial Regex MySqlTableName();
}
