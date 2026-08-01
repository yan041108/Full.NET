import { Counter, Trend, Rate } from 'k6/metrics';

/** Custom metrics required for capacity certification evidence. */
export const actualActiveRequests = new Trend('fullnet_actual_active_requests');
export const arrivalRateDroppedIterations = new Counter('fullnet_arrival_rate_dropped_iterations');
export const httpErrorRate = new Rate('fullnet_http_error_rate');
export const recoverySeconds = new Trend('fullnet_recovery_seconds');

export const evidenceChecklist = [
  'application_metrics',
  'load_generator_metrics',
  'pod_metrics',
  'node_metrics',
  'database_metrics',
  'redis_cache_metrics',
  'redis_realtime_metrics',
  's3_metrics',
  'collector_metrics',
  'actual_active_requests',
  'arrival_rate_dropped_iterations',
  'threadpool_queue_thread_count',
  'allocation_rate',
  'gc_pause_gen2',
  'socket_httpclient',
  'db_connection_pool_wait',
  'log_audit_worker_backlog',
  'image_digest',
  'git_sha',
  'helm_values',
  'hardware',
  'database_parameters',
  'redis_parameters',
  'data_scale',
  'load_model',
  'raw_results_uri',
];

export function incompleteIfMissing(presentKeys) {
  const missing = evidenceChecklist.filter((key) => !presentKeys.includes(key));
  return missing.length === 0
    ? { status: 'CompleteCandidate', missing: [] }
    : { status: 'Incomplete', missing };
}
