namespace Full.NET.Abstractions.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
