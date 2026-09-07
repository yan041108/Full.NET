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

    /// <summary>165–199 中确证 UUID 类型错误的 16 份原始脚本摘要。</summary>
    private static readonly HashSet<string> PublishedUuidScriptDigests = new(StringComparer.Ordinal)
    {
        "A50A30A325BE3E782FC98587BB01D191F10C0CEB59332D126A4FF2012C1FB8CF", // 165_DocumentVersionDeletionAudit.sql
        "83351E06677879EA53F97179BFC5CE26FEBBDDF296A7EA0D55CE8757354178C3", // 167_DocumentAccessLog.sql
        "FF6DB0ED3B1612F77D53A8D82A5AB671DCD4EA04C2B433306C5CBD069B796BB5", // 169_DocumentPreviewTask.sql
        "C0C1879D815E4913A2137DECFC2F75B44D2E6C7E76BA6962249E9A92ECA67DF0", // 171_ImportExportTask.sql
        "55DB983CB230FB7C23CF783FC72B4873067BE27F00ECA4A4E2FCB03D9A7C7A2A", // 173_ImportExportTaskExecution.sql
        "D478BDDA61CC9514CD8D477D51314AD79003F90C9D2895138A75DA33FB51F16B", // 175_ReportingDataSource.sql
        "33FFC4894C16F97F1A75AA0E0CB6B31FB48DEB7FAE472F2293D704379DA53B3E", // 180_ReportingExportTask.sql
        "E3E1E52C2879890A0B522830A528D19434F45D7D8866357067FC2C148653E569", // 182_PrintingTemplate.sql
        "0B950DE9F9CBCC944C1A8269553828AC9154BF972F9CCC7CD0B3A33CDC0F27A8", // 184_AiModelConfig.sql
        "096BB97DBB85F27D321C0387C36267CBE635ECE438458B19DC2DB5CFDDB8E36C", // 186_AiChatSession.sql
        "7DD2CA3809483411468DD6B607955883708B987AC8758D842EFC3DEA854C1F04", // 188_AiAgentToolCall.sql
        "AC5F74E15E9C3C34202ED23D5019A24E37437F86EAFA04612A286A83463BDD7D", // 190_PaymentMerchantConfig.sql
        "27A51A77E825394A5B5BED0EBDBED063C0DA78D446EC6448A4E21DAC9EAFC310", // 192_PaymentNotifyRefund.sql
        "272AB6116251133C8797ABBB2E7096040C79D4DEA6AA5CD9F6B55E9EA1D293A5", // 195_GoViewProject.sql
        "EFD07926EF5ACD38F61A01C9C40CA6136C4BFD8C9127AFB1AEE345AD3A034908", // 197_K3CloudFoundation.sql
        "B129431F624CAB9689C50A8BAA71A9ACCDB0BD45569423A149D66A8B9C4727E9", // 199_OcrFoundation.sql
    };

    /// <summary>在已匹配全文的脚本内替换 UUID 类型及专属字符集属性。</summary>
    [GeneratedRegex(@"char\(36\)(?: CHARACTER SET ascii COLLATE ascii_general_ci| COLLATE utf8mb4_bin)?", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedTextUuidDeclaration();

    /// <summary>识别 094 中已批准的三项不兼容约束声明。</summary>
    [GeneratedRegex(
        @"(?ms)^\s*ALTER\s+TABLE\s+fn_messaging_stream_ownership\s+ADD\s+CONSTRAINT\s+IF\s+NOT\s+EXISTS\s+CK_fn_messaging_stream_ownership_(?:SchemaVersion|CurrentOwner|PreviousOwner)\s+CHECK\s*\([^;]+;\s*")]
    private static partial Regex UnsupportedConstraintSyntax();
}
