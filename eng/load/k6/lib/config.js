/**
 * Shared k6 profile loader helpers.
 * VU count is NEVER treated as actual in-flight requests.
 */
export function requireEnv(name, fallback = '') {
  const value = __ENV[name] ?? fallback;
  if (!value) {
    throw new Error(`Missing required env ${name}`);
  }
  return value;
}

export function loadProfileName() {
  return requireEnv('FULLNET_LOAD_PROFILE', '2k');
}

export function baseUrl() {
  return requireEnv('FULLNET_BASE_URL', 'http://localhost:8080');
}

export function databaseProvider() {
  const provider = requireEnv('FULLNET_DB_PROVIDER', 'SqlServer');
  if (provider !== 'SqlServer' && provider !== 'MySql') {
    throw new Error('FULLNET_DB_PROVIDER must be SqlServer or MySql');
  }
  return provider;
}

export function assertNeverTreatVuAsInFlight(profile) {
  if (profile.treatVuAsActualInFlight === true) {
    throw new Error('Profiles must not treat k6 VUs as actual active requests');
  }
}
