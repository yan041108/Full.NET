using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Oidc.Stores;

internal sealed class IdentityOidcStoreSqlResolver(IOptions<DatabaseOptions> databaseOptions)
{
    private DatabaseProvider Provider => databaseOptions.Value.Provider;

    internal SqlStatement ListApplications() => Provider switch
    {
        DatabaseProvider.SqlServer => IdentityOidcSql.ListApplicationsSqlServer,
        DatabaseProvider.MySql => IdentityOidcSql.ListApplicationsMySql,
        _ => throw new InvalidOperationException("The configured database provider is not supported."),
    };

    internal SqlStatement FindApplicationsByRedirectUri() => Provider switch
    {
        DatabaseProvider.SqlServer => IdentityOidcSql.FindApplicationsByRedirectUriSqlServer,
        DatabaseProvider.MySql => IdentityOidcSql.FindApplicationsByRedirectUriMySql,
        _ => throw new InvalidOperationException("The configured database provider is not supported."),
    };

    internal SqlStatement FindApplicationsByPostLogoutRedirectUri() => Provider switch
    {
        DatabaseProvider.SqlServer => IdentityOidcSql.FindApplicationsByPostLogoutRedirectUriSqlServer,
        DatabaseProvider.MySql => IdentityOidcSql.FindApplicationsByPostLogoutRedirectUriMySql,
        _ => throw new InvalidOperationException("The configured database provider is not supported."),
    };

    internal SqlStatement ListAuthorizations() => Provider switch
    {
        DatabaseProvider.SqlServer => IdentityOidcSql.ListAuthorizationsSqlServer,
        DatabaseProvider.MySql => IdentityOidcSql.ListAuthorizationsMySql,
        _ => throw new InvalidOperationException("The configured database provider is not supported."),
    };

    internal SqlStatement ListScopes() => Provider switch
    {
        DatabaseProvider.SqlServer => IdentityOidcSql.ListScopesSqlServer,
        DatabaseProvider.MySql => IdentityOidcSql.ListScopesMySql,
        _ => throw new InvalidOperationException("The configured database provider is not supported."),
    };

    internal SqlStatement ListTokens() => Provider switch
    {
        DatabaseProvider.SqlServer => IdentityOidcSql.ListTokensSqlServer,
        DatabaseProvider.MySql => IdentityOidcSql.ListTokensMySql,
        _ => throw new InvalidOperationException("The configured database provider is not supported."),
    };

    internal SqlStatement RedeemAuthorizationCodeToken() => Provider switch
    {
        DatabaseProvider.SqlServer => IdentityOidcSql.RedeemAuthorizationCodeToken,
        DatabaseProvider.MySql => IdentityOidcSql.RedeemAuthorizationCodeTokenMySql,
        _ => throw new InvalidOperationException("The configured database provider is not supported."),
    };

    internal SqlStatement FindScopesByResource() => Provider switch
    {
        DatabaseProvider.SqlServer => IdentityOidcSql.FindScopesByResourceSqlServer,
        DatabaseProvider.MySql => IdentityOidcSql.FindScopesByResourceMySql,
        _ => throw new InvalidOperationException("The configured database provider is not supported."),
    };
}