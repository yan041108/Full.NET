namespace Full.NET.Modules.Cryptography.Persistence;

internal static class CryptographySqlParameters
{
    public static Dictionary<string, object?> Create(params (string Key, object? Value)[] values)
    {
        var parameters = new Dictionary<string, object?>(values.Length, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            parameters[key] = value;
        }

        return parameters;
    }
}
