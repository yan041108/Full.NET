namespace Full.NET.Abstractions.Tenancy;

public interface ICurrentTenant
{
    bool IsAvailable { get; }

    bool IsHost { get; }

    Guid? Id { get; }

    string? Identifier { get; }

    string? Name { get; }
}
