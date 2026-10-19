import http from 'k6/http';
import { check, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { baseUrl, databaseProvider, assertNeverTreatVuAsInFlight } from '../lib/config.js';
import { httpErrorRate } from '../lib/metrics.js';

/**
 * 缓存读取负载；Redis 故障/批量失效由隔离环境外部编排并单独留证。
 * 本地可执行，但不得将脚本完成当作容量或恢复认证。
 */
const profileName = __ENV.FULLNET_LOAD_PROFILE || '2k';
const profiles = new SharedArray('profiles', () => [
  JSON.parse(open('../../profiles/2k.json')),
  JSON.parse(open('../../profiles/5k.json')),
  JSON.parse(open('../../profiles/10k.json')),
  JSON.parse(open('../../profiles/soak.json')),
]);
const profile = profiles.find((item) => item.name === profileName);
if (!profile) {
  throw new Error(`Unknown profile ${profileName}`);
}
assertNeverTreatVuAsInFlight(profile);

const provider = databaseProvider();
const model = __ENV.FULLNET_LOAD_MODEL || 'closed_loop';
if (model !== 'open_loop' && model !== 'closed_loop') throw new Error('Unknown load model');
const bearerToken = __ENV.FULLNET_CACHE_BEARER_TOKEN;
if (!bearerToken) throw new Error('FULLNET_CACHE_BEARER_TOKEN is required for the protected cache path');

export const options = model === 'open_loop'
  ? {
      scenarios: {
        open_loop: {
          executor: 'constant-arrival-rate',
          rate: profile.openLoop.arrivalRatePerSecond,
          timeUnit: '1s',
          duration: profile.phases.steady,
          preAllocatedVUs: profile.openLoop.preAllocatedVUs,
          maxVUs: profile.openLoop.maxVUs,
        },
      },
      thresholds: profile.thresholds,
    }
  : {
      scenarios: {
        closed_loop: {
          executor: 'constant-vus',
          vus: profile.closedLoop.vus,
          duration: profile.phases.steady,
        },
      },
      thresholds: profile.thresholds,
    };

export default function () {
  const url = `${baseUrl()}/api/v1/me/grid-preferences/identity.users`;
  const response = http.get(url, {
    headers: { Authorization: `Bearer ${bearerToken}` },
    tags: {
      scenario: 'cache-recovery',
      provider,
      profile: profile.name,
      model,
    },
    timeout: profile.requestTimeout,
  });

  const ok = check(response, {
    'cache read returns expected payload': (r) => {
      if (r.status !== 200) return false;
      try {
        const body = r.json();
        return body.gridKey === 'identity.users' && Number.isInteger(body.schemaVersion)
          && Array.isArray(body.columns) && Number.isInteger(body.version);
      } catch { return false; }
    },
  });
  httpErrorRate.add(!ok);

  sleep(profile.thinkTimeSeconds);
}

export function handleSummary(data) {
  return {
    stdout: JSON.stringify({
      scenario: 'cache-recovery',
      profile: profile.name,
      provider,
      model,
      targetInFlight: profile.targetInFlight,
      capacityStatus: 'Capacity-not-verified',
      evidenceStatus: 'Incomplete',
      actualActiveRequests: null,
      droppedIterations: data.metrics.dropped_iterations?.values?.count ?? null,
      faultInjectionEvidence: null,
      note: 'Validate actual active requests separately; do not equate VUs to in-flight.',
      metrics: data.metrics,
    }, null, 2),
  };
}
