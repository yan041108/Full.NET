import http from 'k6/http';
import {check} from 'k6';
import {Rate, Counter} from 'k6/metrics';

// 负载档位只描述发生器配置；应用真实在途数由独立 OTLP 采集器记录。
const model = __ENV.MODEL;
const level = Number(__ENV.LEVEL);
if (!['closed', 'open'].includes(model) || !Number.isInteger(level) || level < 1 || level > 2000)
  throw new Error('Invalid bounded capacity stage');
const duration = __ENV.DURATION || '60s';
const failed = new Rate('fullnet_capacity_errors');
const malformed = new Rate('fullnet_capacity_payload_errors');
const unauthorized = new Counter('fullnet_capacity_authorization_errors');
const accepted = new Counter('fullnet_capacity_valid_responses');
const throttled = new Counter('fullnet_capacity_rate_limit_responses');
const serverErrors = new Counter('fullnet_capacity_server_errors');
const transportErrors = new Counter('fullnet_capacity_transport_errors');
const phase = model === 'closed'
  ? {executor: 'constant-vus', vus: level, duration, gracefulStop: '15s'}
  : {executor: 'constant-arrival-rate', rate: level, timeUnit: '1s', duration,
      preAllocatedVUs: 128, maxVUs: 256, gracefulStop: '15s'};
export const options = {
  scenarios: {capacity: phase},
  summaryTrendStats: ['avg', 'min', 'max', 'p(50)', 'p(95)', 'p(99)', 'count'],
  thresholds: {
    fullnet_capacity_errors: [{threshold: 'rate<0.01', abortOnFail: true, delayAbortEval: '15s'}],
    fullnet_capacity_payload_errors: [{threshold: 'rate==0', abortOnFail: true, delayAbortEval: '5s'}],
    fullnet_capacity_authorization_errors: [{threshold: 'count==0', abortOnFail: true, delayAbortEval: '5s'}],
    http_req_duration: [{threshold: 'p(99)<1000', abortOnFail: true, delayAbortEval: '15s'}, 'p(95)<500'],
    ...(model === 'open' ? {dropped_iterations: ['count==0']} : {}),
  },
};
export default function () {
  const node = (__VU + __ITER) % 2;
  const response = http.get(__ENV['URL_' + node] + '/api/v1/me/grid-preferences/identity.users', {
    headers: {Host: 'localhost', Authorization: 'Bearer ' + __ENV['TOKEN_' + node]},
    tags: {node: String(node)}, timeout: '10s',
  });
  let valid = false;
  if (response.status === 200) {
    try {
      const value = response.json();
      valid = value.gridKey === 'identity.users' && Number.isInteger(value.schemaVersion)
        && Number.isInteger(value.version) && Array.isArray(value.columns);
    } catch { /* 非 JSON 的成功响应同样不能当作正确缓存结果。 */ }
  }
  check(response, {'protected Grid response is valid': () => valid});
  failed.add(!valid);
  malformed.add(response.status === 200 && !valid);
  unauthorized.add(response.status === 401 || response.status === 403 ? 1 : 0);
  accepted.add(valid ? 1 : 0);
  throttled.add(response.status === 429 ? 1 : 0);
  serverErrors.add(response.status >= 500 ? 1 : 0);
  transportErrors.add(response.status === 0 ? 1 : 0);
}
export function handleSummary(data) {
  return {'/evidence/summary.json': JSON.stringify({model, level, duration,
    capacityStatus: 'Capacity-not-verified', ...data}, null, 2)};
}
