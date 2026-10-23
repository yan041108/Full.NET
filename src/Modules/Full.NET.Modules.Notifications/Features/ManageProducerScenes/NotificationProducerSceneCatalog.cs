using System.Text;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;

namespace Full.NET.Modules.Notifications.Features.ManageProducerScenes;

/// <summary>
/// Producer/Scene 受控目录的闭合语义：合法政策类别、合法渠道集合、渠道 JSON 的确定性读写。
/// </summary>
/// <remarks>
/// 分类只能来自目录；可编辑模板的内容分级不得把普通消息提升为强制通知。
/// 受理与发送链路都通过本类解析目录，避免各处自行推断分类或渠道集合。
/// </remarks>
internal static class NotificationProducerSceneCatalog
{
    internal const string CategoryMandatory = "mandatory";
    internal const string CategoryTransactional = "transactional";
    internal const string CategoryInformational = "informational";
    internal const string CategoryMarketing = "marketing";

    /// <summary>闭合的政策类别集合；新增类别必须同时更新目录约束与迁移。</summary>
    internal static readonly IReadOnlySet<string> PolicyCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        CategoryMandatory,
        CategoryTransactional,
        CategoryInformational,
        CategoryMarketing,
    };

    /// <summary>
    /// 判断渠道是否允许写入目录声明：内建 inbox，或当前已注册 Adapter 真实支持的渠道。
    /// </summary>
    /// <remarks>
    /// 合法渠道与模板渠道、偏好渠道使用同一闭合来源（<see cref="NotificationTemplateCompiler.NormalizeChannel"/>），
    /// 因此适配器增删会同时约束目录声明、模板校验与偏好登记，避免多处各维护一份硬编码渠道列表造成漂移。
    /// </remarks>
    /// <param name="channelKey">待校验渠道键。</param>
    /// <param name="providerTypeCatalog">由已注册 Adapter 构成的闭合渠道目录。</param>
    /// <returns>渠道可被声明为合法时为 <see langword="true"/>。</returns>
    internal static bool IsChannelDeclarable(
        string channelKey,
        INotificationProviderTypeCatalog providerTypeCatalog) =>
        NotificationTemplateCompiler.NormalizeChannel(channelKey, providerTypeCatalog).IsSuccess;

    /// <summary>按受信作用域与 Producer/Scene 读取目录项；未配置返回 <see langword="null"/>。</summary>
    /// <param name="queryExecutor">模块查询执行器。</param>
    /// <param name="tenantScopeKey">可信租户作用域键。</param>
    /// <param name="producerKey">生产者稳定键。</param>
    /// <param name="sceneKey">业务场景稳定键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>目录投影；缺失或已停用时由调用方决定失败关闭策略。</returns>
    internal static Task<NotificationProducerSceneRecord?> FindAsync(
        IQueryExecutor queryExecutor,
        string tenantScopeKey,
        string producerKey,
        string sceneKey,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<NotificationProducerSceneRecord>(
            NotificationPlatformSql.FindProducerScene,
            NotificationPlatformSqlParameters.Create(
                ("TenantScopeKey", tenantScopeKey),
                ("ProducerKey", producerKey),
                ("SceneKey", sceneKey)),
            cancellationToken);

    /// <summary>解析目录声明的渠道 JSON；结构不合法时返回空集合，由调用方失败关闭。</summary>
    /// <param name="json">目录中的渠道 JSON 数组。</param>
    /// <returns>渠道键列表；结构非法或非字符串元素时为空。</returns>
    internal static IReadOnlyList<string> ParseChannels(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var channels = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return [];
                }

                var value = element.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return [];
                }

                channels.Add(value);
            }

            return channels;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>以确定性顺序序列化渠道集合，避免同一声明在不同实例产生不同 JSON。</summary>
    /// <param name="channels">已校验的渠道键集合。</param>
    /// <returns>稳定 JSON 数组文本。</returns>
    internal static string SerializeChannels(IReadOnlyList<string> channels)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var channel in channels)
            {
                writer.WriteStringValue(channel);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>判断意图渠道是否在目录声明的合法渠道内。</summary>
    /// <param name="record">目录投影。</param>
    /// <param name="channelKey">待校验渠道键。</param>
    /// <returns>渠道被声明为合法时为 <see langword="true"/>。</returns>
    internal static bool IsChannelAllowed(NotificationProducerSceneRecord record, string channelKey)
    {
        var channels = ParseChannels(record.AllowedChannelKeysJson);
        foreach (var channel in channels)
        {
            if (string.Equals(channel, channelKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
