using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Mqtt.Configuration;
using Full.NET.Modules.Mqtt.Resources;
using Full.NET.Modules.Mqtt.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Mqtt;

/// <summary>MQTT 控制面模块：提供受控发布、客户端目录与消息记录查询。</summary>
public sealed class MqttModule : IFullNetModule
{
    /// <inheritdoc />
    public string Name => "Mqtt";

    /// <inheritdoc />
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            MqttAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            MqttErrorResourceSource>());
        services.AddOptions<MqttBrokerOptions>()
            .Bind(configuration.GetSection(MqttBrokerOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<MqttBrokerOptions>,
            MqttBrokerOptionsValidator>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<Security.MqttPublishRateLimiter>();
        services.TryAddScoped<Infrastructure.MqttBrokerPublisher>();
        services.TryAddScoped<Features.ManageControlPlane.MqttBrokerStatusService>();
        services.TryAddScoped<Features.ManageControlPlane.MqttClientQueryService>();
        services.TryAddScoped<Features.ManageControlPlane.MqttMessageQueryService>();
        services.TryAddScoped<Features.ManageControlPlane.MqttMessagePublishService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                MqttJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.MqttDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        Features.ManageControlPlane.Endpoint.Map(endpoints);
}
