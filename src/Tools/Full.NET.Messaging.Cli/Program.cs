using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

const string TokenEnvironmentVariable = "FULLNET_ACCESS_TOKEN";

try
{
    var arguments = ParseArguments(args);
    var request = CreateRequest(arguments);
    var accessToken = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
    if (string.IsNullOrWhiteSpace(accessToken))
    {
        throw new ArgumentException(
            $"Environment variable {TokenEnvironmentVariable} is required.");
    }

    using var client = new HttpClient
    {
        BaseAddress = new Uri(GetRequired(arguments, "api-base-uri"), UriKind.Absolute),
        Timeout = TimeSpan.FromMinutes(10),
    };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        accessToken);
    using var response = await client.PostAsJsonAsync(
        "/api/v1/messaging/kafka/replay",
        request);
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(
            $"Kafka range replay failed with HTTP {(int)response.StatusCode}: {body}");
        return 2;
    }

    using var document = JsonDocument.Parse(body);
    Console.WriteLine(JsonSerializer.Serialize(
        document.RootElement,
        new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or FormatException)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine(
        "Usage: --api-base-uri <uri> --topic <code> --consumer <code> --reason <text> "
        + "(--from-offset <n> --to-offset <n> | --from-time <utc> --to-time <utc>) "
        + "[--partitions 0,1] [--max-messages 1000]. "
        + $"Set {TokenEnvironmentVariable} instead of passing credentials on the command line.");
    return 1;
}

static Dictionary<string, string> ParseArguments(string[] args)
{
    var allowedNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "api-base-uri",
        "topic",
        "consumer",
        "reason",
        "from-offset",
        "to-offset",
        "from-time",
        "to-time",
        "partitions",
        "max-messages",
    };
    if (args.Length == 0 || args.Length % 2 != 0)
    {
        throw new ArgumentException("Arguments must be supplied as --name value pairs.");
    }

    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < args.Length; index += 2)
    {
        var name = args[index];
        if (!name.StartsWith("--", StringComparison.Ordinal)
            || !allowedNames.Contains(name[2..])
            || !result.TryAdd(name[2..], args[index + 1]))
        {
            throw new ArgumentException($"Invalid or duplicate argument '{name}'.");
        }
    }

    return result;
}

static ReplayRequest CreateRequest(IReadOnlyDictionary<string, string> arguments)
{
    var hasOffsets = arguments.ContainsKey("from-offset")
        || arguments.ContainsKey("to-offset");
    var hasTimes = arguments.ContainsKey("from-time")
        || arguments.ContainsKey("to-time");
    if (hasOffsets == hasTimes)
    {
        throw new ArgumentException(
            "Exactly one complete Offset or UTC time range must be supplied.");
    }

    var partitions = arguments.TryGetValue("partitions", out var partitionText)
        ? partitionText.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray()
        : [];
    return new ReplayRequest(
        GetRequired(arguments, "topic"),
        hasTimes ? DateTimeOffset.Parse(GetRequired(arguments, "from-time")) : null,
        hasTimes ? DateTimeOffset.Parse(GetRequired(arguments, "to-time")) : null,
        hasOffsets ? long.Parse(GetRequired(arguments, "from-offset")) : null,
        hasOffsets ? long.Parse(GetRequired(arguments, "to-offset")) : null,
        partitions,
        GetRequired(arguments, "consumer"),
        arguments.TryGetValue("max-messages", out var maximum) ? int.Parse(maximum) : 1_000,
        GetRequired(arguments, "reason"));
}

static string GetRequired(
    IReadOnlyDictionary<string, string> arguments,
    string name) =>
    arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Argument --{name} is required.");

internal sealed record ReplayRequest(
    string TopicCode,
    DateTimeOffset? FromTimestampUtc,
    DateTimeOffset? ToTimestampUtc,
    long? FromOffset,
    long? ToOffset,
    IReadOnlyList<int> Partitions,
    string ReplayConsumerName,
    int MaxMessages,
    string Reason);
