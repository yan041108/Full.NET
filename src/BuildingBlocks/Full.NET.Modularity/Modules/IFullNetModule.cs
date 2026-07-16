using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modularity.Modules;

public interface IFullNetModule
{
    string Name { get; }

    IReadOnlyCollection<Type> Dependencies { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);

    Task InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
