using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Full.NET.Modules.Messaging.Features.GetDeadLetters;
using Full.NET.Modules.Messaging.Features.GetDeliveryStatus;
using Full.NET.Modules.Messaging.Features.ReplayDeadLetter;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Modules.Messaging.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Messaging;

public sealed class MessagingModule : IFullNetModule
{
    public string Name => "Messaging";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            MessagingAuthorizationContributor>());
        services.TryAddScoped<DeadLetterQueryService>();
        services.TryAddScoped<DeadLetterReplayService>();
        services.TryAddScoped<DeliveryStatusQueryService>();
        services.TryAddScoped<DeliveryCutoverService>();
        services.TryAddScoped<
            ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>,
            MessagingDomainAuditWriter>();
        services.RemoveAll<IntegrationEventSubscriptionCatalog>();
        services.AddSingleton(provider =>
            new IntegrationEventSubscriptionCatalog(
                provider.GetServices<IntegrationEventTopicDefinition>(),
                provider.GetServices<IIntegrationEventSubscription>()));
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                MessagingJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.GetDeadLetters.Endpoint.Map(endpoints);
        Features.ReplayDeadLetter.Endpoint.Map(endpoints);
        Features.GetDeliveryStatus.Endpoint.Map(endpoints);
        Features.ChangeDeliveryOwner.Endpoint.Map(endpoints);
    }
}
