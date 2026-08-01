import http from 'k6/http';
import { check, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { baseUrl, databaseProvider, assertNeverTreatVuAsInFlight } from '../lib/config.js';
import {
  actualActiveRequests,
  arrivalRateDroppedIterations,
  httpErrorRate,
} from '../lib/metrics.js';

/**
 * Read-heavy mix: hot/cold cache reads, missing keys, tenant resolution.
 * Dedicated capacity cluster only — not for developer laptops.
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
          startTime: profile.phases.warmup,
        },
      },
      thresholds: profile.thresholds,
    };

const paths = [
  "/health/ready",
  "/api/v1/tenancy/current",
  "/api/v1/identity/me"
];

export default function () {
  const url = `${baseUrl()}${paths[Math.floor(Math.random() * paths.length)]}`;
  const response = http.get(url, {
    tags: {
      scenario: 'read-heavy',
      provider,
      profile: profile.name,
      model,
    },
    timeout: profile.requestTimeout,
  });

  const ok = check(response, {
    'status is not 5xx': (r) => r.status < 500,
  });
  httpErrorRate.add(!ok);

  // Placeholder: replace with application-exported actual active requests gauge scrape.
  actualActiveRequests.add(Number(__ENV.FULLNET_ACTUAL_ACTIVE_REQUESTS || 0));
  if (model === 'open_loop' && response.status === 0) {
    arrivalRateDroppedIterations.add(1);
  }

  sleep(profile.thinkTimeSeconds);
}

export function handleSummary(data) {
  return {
    stdout: JSON.stringify({
      scenario: 'read-heavy',
      profile: profile.name,
      provider,
      model,
      targetInFlight: profile.targetInFlight,
      note: 'Validate actual active requests separately; do not equate VUs to in-flight.',
      metrics: data.metrics,
    }, null, 2),
  };
}
