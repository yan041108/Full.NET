namespace Full.NET.Abstractions.Tenancy;

public sealed class CurrentTenantAccessor : ICurrentTenant, ICurrentTenantContextWriter
{
    private TenantContext? _tenant;

    public bool IsAvailable => IsHost || _tenant is not null;

    public bool IsHost { get; private set; }

    public Guid? Id => _tenant?.Id;

    public string? Identifier => _tenant?.Identifier;

    public string? Name => _tenant?.Name;

    internal void SetTenant(TenantContext tenant)
    {
        _tenant = tenant;
        IsHost = false;
    }

    internal void SetHost()
    {
        _tenant = null;
        IsHost = true;
    }

    internal void Clear()
    {
        _tenant = null;
        IsHost = false;
    }

    void ICurrentTenantContextWriter.SetTenant(TenantContext tenant) => SetTenant(tenant);

    void ICurrentTenantContextWriter.SetHost() => SetHost();

    void ICurrentTenantContextWriter.Clear() => Clear();
}
