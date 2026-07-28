using System.Net;

namespace Full.NET.Benchmarks.MixedLoad;

public enum MixedLoadAuthentication
{
    Jwt = 0,
    ApiKey = 1,
}

public enum MixedLoadOperation
{
    Read = 0,
    Write = 1,
}

public sealed record MixedLoadScenario(
    string Name,
    int Weight,
    MixedLoadAuthentication Authentication,
    MixedLoadOperation Operation,
    string Path,
    HttpStatusCode ExpectedStatusCode,
    bool IsAuditQuery = false,
    bool ProducesOutbox = false,
    bool IsExpectedValidationFailure = false);

public static class MixedLoadScenarioCatalog
{
    public static IReadOnlyList<MixedLoadScenario> Default { get; } =
    [
        new(
            "jwt-read",
            25,
            MixedLoadAuthentication.Jwt,
            MixedLoadOperation.Read,
            "/api/v1/platform/host-dashboard-summary",
            HttpStatusCode.OK),
        new(
            "jwt-write-outbox",
            15,
            MixedLoadAuthentication.Jwt,
            MixedLoadOperation.Write,
            "/api/v1/tenancy/tenants/{tenantId}",
            HttpStatusCode.OK,
            ProducesOutbox: true),
        new(
            "api-key-read",
            25,
            MixedLoadAuthentication.ApiKey,
            MixedLoadOperation.Read,
            "/api/v1/identity/users?page=1&pageSize=20",
            HttpStatusCode.OK),
        new(
            "api-key-write-outbox",
            15,
            MixedLoadAuthentication.ApiKey,
            MixedLoadOperation.Write,
            "/api/v1/tenancy/tenants/{tenantId}",
            HttpStatusCode.OK,
            ProducesOutbox: true),
        new(
            "audit-list",
            10,
            MixedLoadAuthentication.Jwt,
            MixedLoadOperation.Read,
            "/api/v1/auditing/access-logs/cursor?limit=20",
            HttpStatusCode.OK,
            IsAuditQuery: true),
        new(
            "validation-failure",
            10,
            MixedLoadAuthentication.Jwt,
            MixedLoadOperation.Write,
            "/api/v1/tenancy/tenants/{tenantId}",
            HttpStatusCode.BadRequest,
            IsExpectedValidationFailure: true),
    ];
}

public sealed class MixedLoadScenarioSelector
{
    private readonly IReadOnlyList<MixedLoadScenario> _scenarios;
    private readonly Random _random;
    private readonly int _totalWeight;

    public MixedLoadScenarioSelector(
        IReadOnlyList<MixedLoadScenario> scenarios,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        if (scenarios.Count == 0
            || scenarios.Any(scenario => scenario.Weight <= 0))
        {
            throw new ArgumentException(
                "场景清单不能为空且每个权重必须大于零。",
                nameof(scenarios));
        }

        _scenarios = scenarios;
        _random = new Random(seed);
        _totalWeight = scenarios.Sum(scenario => scenario.Weight);
    }

    public MixedLoadScenario Next()
    {
        var selection = _random.Next(_totalWeight);
        foreach (var scenario in _scenarios)
        {
            if (selection < scenario.Weight)
            {
                return scenario;
            }

            selection -= scenario.Weight;
        }

        throw new InvalidOperationException("场景权重选择超出清单边界。");
    }
}

public static class MixedLoadMetricContract
{
    public static IReadOnlyList<string> Required { get; } =
    [
        "client.request.duration",
        "client.response.status",
        "fullnet.dapper.statement",
        "db.client.connection.pool",
        "process.cpu",
        "process.gc",
        "database.container.cpu",
        "database.container.memory",
        "database.lock_wait",
        "fullnet.audit.write",
        "fullnet.outbox.backlog",
    ];
}
