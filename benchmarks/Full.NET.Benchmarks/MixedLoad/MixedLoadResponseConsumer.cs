using System.Net;
using System.Net.Http.Json;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Benchmarks.MixedLoad;

public static class MixedLoadResponseConsumer
{
    public static async Task<TenantSummary?> ConsumeAsync(
        HttpResponseMessage response,
        MixedLoadScenario scenario,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(scenario);

        await response.Content
            .LoadIntoBufferAsync(cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK
            || !scenario.ProducesOutbox)
        {
            return null;
        }

        return await response.Content
            .ReadFromJsonAsync<TenantSummary>(cancellationToken)
            .ConfigureAwait(false);
    }
}
