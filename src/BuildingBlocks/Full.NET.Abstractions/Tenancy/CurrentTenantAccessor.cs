namespace Full.NET.Abstractions.Tenancy;

public sealed class CurrentTenantAccessor : ICurrentTenant
{
    private TenantContext? _tenant;

    public bool IsAvailable => IsHost || _tenant is not null;

    public bool IsHost { get; private set; }

    public Guid? Id => _tenant?.Id;

    public string? Identifier => _tenant?.Identifier;

    public string? Name => _tenant?.Name;

    public void SetTenant(TenantContext tenant)
    {
        _tenant = tenant;
        IsHost = false;
    }

    public void SetHost()
    {
        _tenant = null;
        IsHost = true;
    }

    public void Clear()
    {
        _tenant = null;
        IsHost = false;
    }
}
