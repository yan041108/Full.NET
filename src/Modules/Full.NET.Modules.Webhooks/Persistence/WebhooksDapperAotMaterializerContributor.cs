#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks.Persistence;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions.Persistence;

namespace Full.NET.Modules.Webhooks.Persistence;

internal sealed class WebhooksDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<WebhookSubscriptionRecord>(ReadWebhookSubscriptionRecord);
        registrar.Register<WebhookSubscriptionDeliveryRecord>(ReadWebhookSubscriptionDeliveryRecord);
        registrar.Register<WebhookDeliveryWorkItem>(ReadWebhookDeliveryWorkItem);
    }

    private static WebhookSubscriptionRecord ReadWebhookSubscriptionRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadBoolean(reader, 4),
            reader.GetInt32(5));

    private static WebhookSubscriptionDeliveryRecord ReadWebhookSubscriptionDeliveryRecord(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadBoolean(reader, 5),
            reader.GetInt32(6));

    private static WebhookDeliveryWorkItem ReadWebhookDeliveryWorkItem(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetGuid(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9));
}
#endif
