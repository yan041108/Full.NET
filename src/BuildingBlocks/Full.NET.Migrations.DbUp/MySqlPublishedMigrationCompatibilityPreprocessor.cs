using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DbUp.Engine;

namespace Full.NET.Migrations.DbUp;

/// <summary>
/// 保持已记账 094 脚本字节不变，同时在 MySQL 8 执行前移除其不支持的
/// ADD CONSTRAINT IF NOT EXISTS 语句；紧随其后的 095 负责幂等补齐约束。
/// </summary>
internal sealed partial class MySqlPublishedMigrationCompatibilityPreprocessor
    : IScriptPreprocessor
{
    /// <summary>只改写已批准的历史执行缺陷，不改变原始资源与记账名称。</summary>
    /// <param name="contents">DbUp 加载的原始脚本内容。</param>
    public string Process(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        // 归一化换行后匹配完整内容摘要；新增或修改脚本必须走正常迁移，不能套用历史豁免。
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents.ReplaceLineEndings("\n"))));
        if (PublishedUuidScriptDigests.Contains(digest))
        {
            return PublishedTextUuidDeclaration().Replace(contents, "BINARY(16)");
        }
        return UnsupportedConstraintSyntax().Replace(
            contents,
            "-- 094 compatibility: constraints converge in migration 095.\n");
    }

    /// <summary>165–199 中确证 UUID 类型错误的 16 份原始脚本摘要；对象注释写回后按当前全文重新记账。</summary>
    private static readonly HashSet<string> PublishedUuidScriptDigests = new(StringComparer.Ordinal)
    {
        "E1336EB2A3BC7E73949B43061F6F5BDFEC138DFCCAB8C013C880806C1CFBE1E1", // 165_DocumentVersionDeletionAudit.sql
        "98F710F450610F1661F61A9A534D2539D5577DFEE3183BFBC0B64D43E7F5C3A5", // 167_DocumentAccessLog.sql
        "CD35465D5D07657B85A0B2F93C10552FEDE2905ED64FA5B82938D93B0D1FA7B5", // 169_DocumentPreviewTask.sql
        "C913081680634BCCC8AF81228F159868BCA2AC663E17FAA6EC8B41D8316E5DD2", // 171_ImportExportTask.sql
        "55DB983CB230FB7C23CF783FC72B4873067BE27F00ECA4A4E2FCB03D9A7C7A2A", // 173_ImportExportTaskExecution.sql
        "288DB2350582D9101C2727D77BE386437E47B65CD4A395B1DE2F01D51FF49E93", // 175_ReportingDataSource.sql
        "510B8892D9806F656E50CF722F3ABF78C51FB6A9219B88A89FD3466CAB93E6D7", // 180_ReportingExportTask.sql
        "7ACFC9054E4FF2EB17E1312622480DFB2BE93F7FE1B45AC66AB896C4AC64C7DF", // 182_PrintingTemplate.sql
        "41656877898E6F4B3F09FF96EA2E618A71EB2AF7D178ED90FB433636483EA115", // 184_AiModelConfig.sql
        "2CCF23981E326A768221F20DE3C5628AFC47C9270F9DFF77FB39F916DB9A26D4", // 186_AiChatSession.sql
        "E4A53B26D2F05C5D8E72622C80FDF537FF467A62A0330E7594341DA6ACD1B606", // 188_AiAgentToolCall.sql
        "55560720FC03B2812A71132FFC92AE7B49313216CF8AED7D1788D8B0F9F30514", // 190_PaymentMerchantConfig.sql
        "09496E6184781BF50708FC39E7A70FC765A26A2A2C1BD2FFC79F75E7F2F51E3C", // 192_PaymentNotifyRefund.sql
        "AAEF2CDE67F0DF065558B0AADF42DC7CCC4F240CD2916E3A987ADE08A69E8414", // 195_GoViewProject.sql
        "EF2BCBCBA4AE9B195206613411466631E31EAC2EDB4D5D035AF898D28917231B", // 197_K3CloudFoundation.sql
        "8B93ED88F9411B8C6BE48B5A7A4F14BEF1BFD3D348EC8340878CBF4F6DA1C461", // 199_OcrFoundation.sql
    };

    /// <summary>在已匹配全文的脚本内替换 UUID 类型及专属字符集属性。</summary>
    [GeneratedRegex(@"char\(36\)(?: CHARACTER SET ascii COLLATE ascii_general_ci| COLLATE utf8mb4_bin)?", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedTextUuidDeclaration();

    /// <summary>识别 094 中已批准的三项不兼容约束声明。</summary>
    [GeneratedRegex(
        @"(?ms)^\s*ALTER\s+TABLE\s+fn_messaging_stream_ownership\s+ADD\s+CONSTRAINT\s+IF\s+NOT\s+EXISTS\s+CK_fn_messaging_stream_ownership_(?:SchemaVersion|CurrentOwner|PreviousOwner)\s+CHECK\s*\([^;]+;\s*")]
    private static partial Regex UnsupportedConstraintSyntax();
}
