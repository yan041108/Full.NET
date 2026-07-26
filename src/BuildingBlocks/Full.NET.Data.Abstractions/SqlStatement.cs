namespace Full.NET.Data.Abstractions;

public sealed record SqlStatement(
    string Name,
    string Text,
    SqlDataScope Scope,
    SqlTenantBinding TenantBinding)
{
    public SqlStatement(
        string Name,
        string Text,
        SqlDataScope Scope)
        : this(Name, Text, Scope, SqlTenantBinding.None)
    {
    }

    public void Deconstruct(
        out string Name,
        out string Text,
        out SqlDataScope Scope)
    {
        Name = this.Name;
        Text = this.Text;
        Scope = this.Scope;
    }
}
