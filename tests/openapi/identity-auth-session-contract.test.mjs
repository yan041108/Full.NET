import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/identity-auth-session-v1.json');
const loginEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/Login/Endpoint.cs'
);
const refreshEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/RefreshSession/Endpoint.cs'
);
const logoutEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/Logout/Endpoint.cs'
);
const localeEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/UpdateLocale/Endpoint.cs'
);
const identityModulePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/IdentityModule.cs'
);
const loginRequestPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/LoginRequest.cs'
);
const tokenResponsePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/TokenResponse.cs'
);
const updateLocaleRequestPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/UpdateLocaleRequest.cs'
);
const localePreferencePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/LocalePreferenceResponse.cs'
);

test('Auth Session OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const loginEndpoint = await readFile(loginEndpointPath, 'utf8');
  const refreshEndpoint = await readFile(refreshEndpointPath, 'utf8');
  const logoutEndpoint = await readFile(logoutEndpointPath, 'utf8');
  const localeEndpoint = await readFile(localeEndpointPath, 'utf8');
  const identityModule = await readFile(identityModulePath, 'utf8');
  const loginRequest = await readFile(loginRequestPath, 'utf8');
  const tokenResponse = await readFile(tokenResponsePath, 'utf8');
  const updateLocaleRequest = await readFile(updateLocaleRequestPath, 'utf8');
  const localePreference = await readFile(localePreferencePath, 'utf8');

  assert.equal(contract.id, 'identity-auth-session-v1');
  assert.match(identityModule, /MapGroup\("\/api\/v1\/auth"\)\.WithTags\("IdentityAuthSession"\)/u);
  assert.match(loginEndpoint, /WithName\("identityLogin"\)/u);
  assert.match(refreshEndpoint, /WithName\("identityRefreshSession"\)/u);
  assert.match(logoutEndpoint, /WithName\("identityLogout"\)/u);
  assert.match(logoutEndpoint, /Produces\(StatusCodes\.Status204NoContent\)/u);
  assert.match(localeEndpoint, /MapPut\("\/api\/v1\/me\/locale"/u);
  assert.match(localeEndpoint, /WithName\("identityUpdatePreferredLocale"\)/u);
  assert.match(localeEndpoint, /WithTags\("IdentityAuthSession"\)/u);
  assert.match(loginRequest, /record LoginRequest/u);
  assert.match(tokenResponse, /record TokenResponse/u);
  assert.match(updateLocaleRequest, /record UpdateLocaleRequest/u);
  assert.match(localePreference, /record LocalePreferenceResponse/u);

  for (const property of contract.schemas.TokenResponse.properties) {
    const pascal = property.charAt(0).toUpperCase() + property.slice(1);
    assert.match(tokenResponse, new RegExp(pascal, 'u'), `TokenResponse.${property} 未在 C# 契约中找到`);
  }

  assert.ok(contract.paths.some((entry) => entry.path === '/api/v1/auth/logout'
    && entry.operations.some((operation) => operation.successStatus === 204)));
});
