/** 与 bootstrap-stack createOidcStackEnv 一致，供附着栈 Host.Api 环境变量注入。 */
export function createOidcStackEnv(apiBaseUrl) {
  const issuer = `${apiBaseUrl.replace(/\/$/, '')}/identity`;
  return {
    Identity__Oidc__Enable: 'true',
    Identity__Oidc__Issuer: issuer,
    Identity__Oidc__AllowDevelopmentEphemeralSigningKey: 'true',
    Identity__Oidc__Clients__0__ClientId: 'e2e-oidc-rp-a',
    Identity__Oidc__Clients__0__RedirectUris__0: 'http://localhost:5173/',
    Identity__Oidc__Clients__0__Scopes__0: 'openid',
    Identity__Oidc__Clients__0__Scopes__1: 'profile',
    Identity__Oidc__Clients__0__IsFirstParty: 'true',
    Identity__Oidc__Clients__1__ClientId: 'e2e-oidc-rp-b',
    Identity__Oidc__Clients__1__ClientSecret: 'e2e-oidc-rp-b-secret',
    Identity__Oidc__Clients__1__RedirectUris__0: 'http://localhost:5174/',
    Identity__Oidc__Clients__1__Scopes__0: 'openid',
    Identity__Oidc__Clients__1__Scopes__1: 'profile',
    Identity__Oidc__Clients__1__IsFirstParty: 'true',
    Identity__Oidc__Clients__2__ClientId: 'e2e-admin-oidc-spa',
    Identity__Oidc__Clients__2__RedirectUris__0: 'http://localhost:25175/',
    Identity__Oidc__Clients__2__RedirectUris__1: 'http://127.0.0.1:25175/',
    Identity__Oidc__Clients__2__Scopes__0: 'openid',
    Identity__Oidc__Clients__2__Scopes__1: 'profile',
    Identity__Oidc__Clients__2__Scopes__2: 'offline_access',
    Identity__Oidc__Clients__2__IsFirstParty: 'true'
  };
}
