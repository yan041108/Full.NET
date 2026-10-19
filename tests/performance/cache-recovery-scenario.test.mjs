import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

function scenario(status, payload = () => ({gridKey:'identity.users',schemaVersion:2,columns:[],version:0})) {
  const source = readFileSync(new URL('../../eng/load/k6/scenarios/cache-recovery.js', import.meta.url), 'utf8')
    .replace(/import\s+[\s\S]*?\s+from\s+['"][^'"]+['"];\s*/g, '')
    .replace('export default function', 'function run')
    .replaceAll('export const ', 'const ').replaceAll('export function ', 'function ');
  const errors = [], fakeActive = [], fakeDropped = [], urls = [];
  const profile = JSON.parse(readFileSync(new URL('../../eng/load/profiles/2k.json', import.meta.url)));
  const api = vm.runInNewContext(`${source}\n;({run,handleSummary,options});`, {
    __ENV: { FULLNET_LOAD_MODEL: 'open_loop', FULLNET_CACHE_BEARER_TOKEN: 'fixture-token', FULLNET_ACTUAL_ACTIVE_REQUESTS: '10000' },
    SharedArray: function (_, create) { return create(); }, open: () => JSON.stringify(profile),
    baseUrl: () => 'http://fixture', databaseProvider: () => 'SqlServer', assertNeverTreatVuAsInFlight: () => {},
    http: { get: (url) => { urls.push(url); return {status, json: payload}; } },
    check: (response, checks) => Object.values(checks).every(check => check(response)), sleep: () => {},
    httpErrorRate: { add: value => errors.push(value) },
    actualActiveRequests: { add: value => fakeActive.push(value) },
    arrivalRateDroppedIterations: { add: value => fakeDropped.push(value) },
  });
  return { ...api, errors, fakeActive, fakeDropped, urls };
}

test('cache recovery counts transport and non-success responses as errors', () => {
  for (const status of [0, 401, 403, 404, 429, 500]) {
    const subject = scenario(status); subject.run();
    assert.deepEqual(subject.errors, [true], `status ${status} must fail`);
  }
  const successful = scenario(200); successful.run();
  assert.deepEqual(successful.errors, [false]);
  assert.match(successful.urls[0], /\/api\/v1\/me\/grid-preferences\/identity\.users$/);
});

test('cache recovery never fabricates in-flight or dropped-iteration evidence', () => {
  const subject = scenario(0); subject.run();
  assert.deepEqual(subject.fakeActive, []);
  assert.deepEqual(subject.fakeDropped, []);
  const summary = JSON.parse(subject.handleSummary({metrics:{dropped_iterations:{values:{count:7}}}}).stdout);
  assert.equal(summary.droppedIterations, 7);
  assert.equal(summary.actualActiveRequests, null);
  assert.equal(summary.evidenceStatus, 'Incomplete');
});

test('HTTP 200 with an unrelated or malformed body is not a cache success', () => {
  for (const payload of [() => ({}), () => { throw new Error('HTML response'); }]) {
    const subject = scenario(200, payload); subject.run();
    assert.deepEqual(subject.errors, [true]);
  }
});
