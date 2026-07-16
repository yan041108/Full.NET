namespace Full.NET.Data.Abstractions;

public sealed record SqlStatement(
    string Name,
    string Text,
    SqlDataScope Scope);
