namespace Full.NET.Hosting.Observability;

public sealed class LoggingOptions
{
    public const string SectionName = "FullNet:Logging";

    public int AsyncBufferSize { get; set; } = 10_000;

    public bool BlockWhenFull { get; set; }
}
