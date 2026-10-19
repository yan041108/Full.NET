import {createHash} from 'node:crypto';

// 逻辑角色只绑定真实宿主启动事实；不能让预设 OTEL 属性覆盖产品实际实例身份。
export function apiIdentityMap(identities, context) {
  if (!Array.isArray(identities) || identities.length !== 2 || !/^(SqlServer|MySql)-(C0|S2)$/u.test(context ?? '')) throw Error('Exactly two context-bound API identity facts required');
  const map = new Map(), roles = new Set(), addresses = new Set(), logs = new Set();
  for (const fact of identities) {
    if (!['cache-node-one', 'cache-node-two'].includes(fact.role) || roles.has(fact.role)
      || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u.test(fact.instance)
      || map.has(fact.instance) || fact.application !== 'Full.NET.Host.Api' || fact.context !== context
      || !/^http:\/\/127\.0\.0\.1:[1-9][0-9]{0,4}\/$/u.test(fact.baseAddress ?? '') || Number(new URL(fact.baseAddress).port) > 65535
      || addresses.has(fact.baseAddress) || !/^fullnet-native-aot-[0-9a-f]{32}\.log$/u.test(fact.logFileName ?? '')
      || !fact.logFile?.endsWith('/' + fact.logFileName) || logs.has(fact.logFileName) || typeof fact.announcement !== 'string'
      || createHash('sha256').update(fact.announcement).digest('hex') !== fact.announcementSha256) throw Error('Invalid or duplicate API identity fact');
    const event = JSON.parse(fact.announcement);
    if (event.Instance !== fact.instance || event.Application !== fact.application || event.HostRole !== 'Api'
      || event.SourceContext !== 'Full.NET.Hosting.Observability.LoggingResourceAnnouncementService') throw Error('API identity announcement mismatch');
    map.set(fact.instance, fact.role); roles.add(fact.role); addresses.add(fact.baseAddress); logs.add(fact.logFileName);
  }
  return map;
}

// 指标缺失、重置与身份混用必须保留为未知；原始 Prometheus 文本始终保持真实 UUID。
export function readMetrics(text, identities, context) {
  const map = identities === undefined ? null : apiIdentityMap(identities, context);
  return text.split(/\r?\n/).map(s => s.trim()).filter(s => s && !s.startsWith('#')).map(line => {
    const m = /^([a-zA-Z_:][a-zA-Z0-9_:]*)(?:\{(.*)\})?\s+([^\s]+)(?:\s+-?\d+)?$/.exec(line);
    if (!m) throw Error('Unsupported metric sample');
    const labels = {}, expression = /([a-zA-Z_][a-zA-Z0-9_]*)="((?:\\.|[^"\\])*)"/g;
    if ((m[2] ?? '').replace(expression, '').replace(/[\s,]/g, '')) throw Error('Invalid metric labels');
    for (const match of (m[2] ?? '').matchAll(expression)) {
      if (Object.hasOwn(labels, match[1])) throw Error('Duplicate metric label');
      labels[match[1]] = JSON.parse('"' + match[2] + '"');
    }
    const value = m[3] === '+Inf' ? Infinity : m[3] === '-Inf' ? -Infinity : Number(m[3]);
    if (Number.isNaN(value) && m[3] !== 'NaN') throw Error('Invalid metric value');
    let observedServiceInstanceId;
    if (map?.has(labels.service_instance_id) && labels.service_name !== 'Full.NET.Host.Api') throw Error('API resource identity mismatch');
    if (map && labels.service_name === 'Full.NET.Host.Api') {
      observedServiceInstanceId = labels.service_instance_id;
      const role = map.get(observedServiceInstanceId);
      if (!role) throw Error('Unknown API instance in actual telemetry');
      labels.service_instance_id = role;
    }
    return {name: m[1], labels, value, ...(observedServiceInstanceId ? {observedServiceInstanceId} : {})};
  });
}
export function apiRole(row) {
  if (!['cache-node-one', 'cache-node-two'].includes(row.labels.service_instance_id)) return null;
  if (row.labels.service_name !== 'Full.NET.Host.Api') throw Error('API resource identity mismatch');
  return row.labels.service_instance_id;
}
export function selectSqlCounter(name = 'fullnet_data_sql_completed_operations_total') {
  if (name !== 'fullnet_data_sql_completed_operations_total')
    throw Error('Unsupported SQL counter metric');
  return name;
}
const stableSqlRow = row => !Object.hasOwn(row.labels, 'outcome') && !Object.hasOwn(row.labels, 'failure_reason')
  && typeof row.labels.statement_name === 'string' && row.labels.statement_name.length > 0
  && ['sql_server', 'my_sql'].includes(row.labels.provider)
  && ['query_single', 'query', 'execute', 'query_multiple'].includes(row.labels.operation);
export function missingGridSqlRoles(rows, sqlCounterMetric) {
  const name = selectSqlCounter(sqlCounterMetric);
  const observed = new Set(rows.filter(r => r.name === name
    && stableSqlRow(r)
    && r.labels.statement_name === 'settings.grid_preference.find_by_verified_user_and_grid'
    && Number.isFinite(r.value) && r.value > 0).map(apiRole).filter(Boolean));
  return ['cache-node-one', 'cache-node-two'].filter(role => !observed.has(role));
}
export function counterDelta(before, after, name, predicate = apiRole) {
  const select = rows => rows.filter(r => r.name === name && predicate(r));
  const original = select(before), current = select(after);
  if (!current.length) throw Error('Missing counter: ' + name);
  const key = r => JSON.stringify(Object.entries(r.labels).sort(([a], [b]) => a.localeCompare(b)));
  const previous = new Map(original.map(r => [key(r), r.value]));
  if (previous.size !== original.length) throw Error('Duplicate counter series');
  const seen = new Set(); let total = 0;
  for (const row of current) {
    const k = key(row);
    if (!previous.has(k)) throw Error('Missing counter baseline');
    const d = row.value - previous.get(k);
    if (seen.has(k) || !Number.isFinite(d) || d < 0) throw Error('Duplicate, reset or non-finite counter');
    seen.add(k); previous.delete(k); total += d;
  }
  if (previous.size) throw Error('Counter series disappeared');
  return total;
}
// 当前采集只接受稳定总量；历史证据必须使用对应的冻结解析器。
export function sqlCounterDelta(before, after, sqlCounterMetric, predicate = apiRole) {
  const name = selectSqlCounter(sqlCounterMetric);
  const selected = [...before, ...after].filter(r => r.name === name && predicate(r));
  if (selected.some(row => !stableSqlRow(row))) throw Error('Invalid stable SQL counter labels');
  return ['cache-node-one', 'cache-node-two'].reduce((total, role) => total
    + counterDelta(before, after, name, row => apiRole(row) === role && predicate(row)), 0);
}

// 每个节点/访问模式分别计算已完成阶段的增量均值；阶段不是互斥请求分段，不能相减或外推 P99。
export function connectionPhaseTimings(before, after) {
  const prefix = 'fullnet_db_connection_phase_duration_seconds';
  const select = rows => rows.filter(row => [prefix + '_count', prefix + '_sum'].includes(row.name) && apiRole(row));
  const initial = select(before), final = select(after), combined = [...initial, ...final];
  if (!combined.length) return {status: 'unavailable', rows: [], unknown: ['Connection phase metrics missing']};
  const key = row => JSON.stringify(Object.entries(row.labels).sort(([a], [b]) => a.localeCompare(b)));
  const groups = new Map(combined.map(row => [key(row), row.labels]));
  const rows = [], unknown = [];
  for (const [identity, labels] of groups) {
    const row = {node: labels.service_instance_id, provider: labels.provider, accessMode: labels.access_mode,
      phase: labels.phase, count: null, totalSeconds: null, meanMilliseconds: null};
    try {
      if (!['admission', 'open', 'hold'].includes(labels.phase)
        || !['normal', 'critical', 'authorization'].includes(labels.access_mode)
        || !['sqlserver', 'mysql'].includes(labels.provider) || labels.host_role !== 'api'
        || Object.hasOwn(labels, 'outcome') || Object.hasOwn(labels, 'failure_reason'))
        throw Error('Invalid connection phase labels');
      const value = (samples, suffix) => {
        const matches = samples.filter(sample => key(sample) === identity && sample.name === prefix + suffix);
        if (matches.length !== 1 || !Number.isFinite(matches[0].value) || matches[0].value < 0
          || (suffix === '_count' && !Number.isSafeInteger(matches[0].value)))
          throw Error('Missing, duplicate or invalid connection phase sample');
        return matches[0].value;
      };
      const count = value(final, '_count') - value(initial, '_count');
      const seconds = value(final, '_sum') - value(initial, '_sum');
      if (count < 0 || seconds < 0 || (count === 0 && seconds !== 0)) throw Error('Connection phase reset or inconsistent delta');
      const mean = count > 0 ? seconds / count * 1000 : null;
      if (mean !== null && !Number.isFinite(mean)) throw Error('Non-finite connection phase mean');
      row.count = count; row.totalSeconds = seconds;
      row.meanMilliseconds = mean;
    } catch (error) { unknown.push(`${row.node}/${row.accessMode}/${row.phase}: ${error.message}`); }
    rows.push(row);
  }
  for (const node of ['cache-node-one', 'cache-node-two'])
    if (!combined.some(row => apiRole(row) === node)) unknown.push(node + ': connection phase metrics missing');
  return {status: unknown.length ? 'incomplete' : 'complete', rows, unknown};
}

// 命令阶段包含客户端准备/物化/结果投影，不是数据库服务端执行时间；计数为已进入阶段的尝试。
export function sqlCommandTimings(before, after) {
  const prefix = 'fullnet_data_sql_command_duration_milliseconds';
  const select = samples => samples.filter(row => [prefix + '_count', prefix + '_sum'].includes(row.name) && apiRole(row));
  const initial = select(before), final = select(after), combined = [...initial, ...final];
  if (!combined.length) return {status: 'unavailable', rows: [], unknown: ['SQL command timing metrics missing']};
  const key = row => JSON.stringify(Object.entries(row.labels).sort(([a], [b]) => a.localeCompare(b)));
  const groups = new Map(combined.map(row => [key(row), row.labels])), rows = [], unknown = [];
  for (const [identity, labels] of groups) {
    const row = {node: labels.service_instance_id, statementName: labels.statement_name, provider: labels.provider,
      operation: labels.operation, count: null, totalMilliseconds: null, meanMilliseconds: null};
    try {
      if (!stableSqlRow({labels})) throw Error('Invalid SQL command timing labels');
      const value = (samples, suffix) => {
        const matches = samples.filter(sample => key(sample) === identity && sample.name === prefix + suffix);
        if (matches.length !== 1 || !Number.isFinite(matches[0].value) || matches[0].value < 0
          || (suffix === '_count' && !Number.isSafeInteger(matches[0].value)))
          throw Error('Missing, duplicate or invalid SQL command timing sample');
        return matches[0].value;
      };
      const count = value(final, '_count') - value(initial, '_count');
      const milliseconds = value(final, '_sum') - value(initial, '_sum');
      if (count < 0 || milliseconds < 0 || (count === 0 && milliseconds !== 0)) throw Error('SQL command timing reset or inconsistent delta');
      row.count = count; row.totalMilliseconds = milliseconds;
      row.meanMilliseconds = count > 0 ? milliseconds / count : null;
    } catch (error) { unknown.push(`${row.node}/${row.statementName}/${row.operation}: ${error.message}`); }
    rows.push(row);
  }
  for (const node of ['cache-node-one', 'cache-node-two'])
    if (!combined.some(row => apiRole(row) === node)) unknown.push(node + ': SQL command timing metrics missing');
  return {status: unknown.length ? 'incomplete' : 'complete', rows, unknown};
}
export function summarizeSample(summary, exitCode, snapshots, before, after, sqlCounterMetric) {
  const sqlName = selectSqlCounter(sqlCounterMetric);
  const metrics = summary?.metrics ?? {}, unknown = [], unavailableOptionalMetrics = [];
  const number = (name, field) => {
    const value = metrics[name]?.values?.[field];
    if (!Number.isFinite(value) || value < 0) { unknown.push(name + '.' + field); return null; }
    return value;
  };
  const requests = number('http_reqs', 'count'), rps = number('http_reqs', 'rate');
  const errors = number('fullnet_capacity_errors', 'rate');
  const malformed = number('fullnet_capacity_payload_errors', 'rate');
  const authorization = number('fullnet_capacity_authorization_errors', 'count');
  const validResponses = number('fullnet_capacity_valid_responses', 'count');
  const validRps = number('fullnet_capacity_valid_responses', 'rate');
  const rateLimited = number('fullnet_capacity_rate_limit_responses', 'count');
  const serverErrors = number('fullnet_capacity_server_errors', 'count');
  const transportErrors = number('fullnet_capacity_transport_errors', 'count');
  const latency = Object.fromEntries(['p(50)', 'p(95)', 'p(99)'].map(q => [q, number('http_req_duration', q)]));
  const dropped = summary?.model === 'open' ? number('dropped_iterations', 'count') : null;
  const active = snapshots.map(rows => rows.filter(r => r.name === 'http_server_active_requests' && apiRole(r)));
  // 两个实例均须有真实样本；节点导出时刻不同，合计只作为采样指标，不能证明同时在途峰值或其下界。
  const valid = active.filter(rows => new Set(rows.map(apiRole)).size === 2 && rows.every(r => Number.isFinite(r.value) && r.value >= 0));
  const activePeak = valid.length ? Math.max(...valid.map(rows => rows.reduce((n, r) => n + r.value, 0))) : null;
  if (activePeak === null) unknown.push('two API active-request gauges');
  const deltas = {}, maxima = {};
  const names = new Set(after.filter(apiRole).map(r => r.name));
  for (const name of names) {
    if (/(?:fullnet_data_sql_(?:executions|completed_operations)_total|fullnet_cache_.*_total|fullnet_db_connection_.*_(?:count|sum|total)|(?:dotnet_|process_runtime_dotnet_).*(?:allocat|gc).*(?:_total|_count|_sum))$/.test(name)) {
      try { deltas[name] = name === sqlName ? sqlCounterDelta(before, after, name) : counterDelta(before, after, name); }
      catch (e) { deltas[name] = null; (name === sqlName ? unknown : unavailableOptionalMetrics).push(name + ': ' + e.message); }
    }
  }
  if (!Object.hasOwn(deltas, sqlName)) {
    unknown.push('SQL execution counter');
    deltas[sqlName] = null;
  }
  for (const row of snapshots.flat().filter(apiRole)) {
    if (/^(?:dotnet_|process_runtime_dotnet_|fullnet_db_connection_admission)/.test(row.name)
      && !/_(?:bucket|sum|count|total)$/.test(row.name) && Number.isFinite(row.value))
      maxima[row.name] = Math.max(maxima[row.name] ?? -Infinity, row.value);
  }
  let gridSql = null;
  try { gridSql = sqlCounterDelta(before, after, sqlName, r => apiRole(r)
    && r.labels.statement_name === 'settings.grid_preference.find_by_verified_user_and_grid'); }
  catch { unknown.push('Grid statement delta'); }
  const thresholds = Object.values(metrics).flatMap(m => Object.values(m.thresholds ?? {}));
  if (!thresholds.length) unknown.push('k6 threshold verdicts');
  const correctnessFailure = malformed > 0 || authorization > 0;
  const exceeded = errors >= .01 || latency['p(95)'] >= 500 || latency['p(99)'] >= 1000 || dropped > 0 || thresholds.some(t => t.ok !== true);
  const classification = correctnessFailure ? 'correctness-failure'
    : !requests || unknown.length || ![0, 99].includes(exitCode) ? 'Incomplete'
    : exceeded || exitCode === 99 ? 'budget-exceeded' : 'within-local-budget';
  const budgetFailureKinds = [rateLimited > 0 ? 'rate-limit' : null, serverErrors > 0 ? 'server-errors' : null,
    transportErrors > 0 ? 'transport-errors' : null, errors >= .01 ? 'business-error-rate' : null,
    latency['p(95)'] >= 500 || latency['p(99)'] >= 1000 ? 'latency' : null, dropped > 0 ? 'arrival-drops' : null].filter(Boolean);
  return {sqlCounterMetric: sqlName,
    classification, exitCode, requests, throughputRps: rps, validResponses, successfulThroughputRps: validRps,
    rateLimitedResponses: rateLimited, serverErrors, transportErrors, budgetFailureKinds, errorRate: errors, payloadErrorRate: malformed,
    authorizationErrors: authorization, latencyMs: latency, droppedIterations: dropped,
    sampledApiActivePeak: activePeak, activeScope: 'Maximum sum of separately exported API gauges; asynchronous node timestamps; not a simultaneous global peak or guaranteed lower bound; never VUs',
    counterDeltas: deltas, gridSqlExecutions: gridSql, gridSqlPerRequest: requests && gridSql !== null ? gridSql / requests : null,
    individualApiGaugeMaxima: maxima, connectionPhaseTimings: connectionPhaseTimings(before, after),
    sqlCommandTimings: sqlCommandTimings(before, after),
    unknown, unavailableOptionalMetrics, capacityStatus: 'Capacity-not-verified'};
}

export function verifyLifecycle(discoveryText, trxText) {
  const tests = JSON.parse(discoveryText).tests;
  if (tests?.length !== 2 || new Set(tests.map(t => t.uid)).size !== 2
    || ['SqlServer', 'MySql'].some(p => !tests.some(t => JSON.stringify(t).includes(p))))
    throw Error('Expected exactly two provider lifecycle identities');
  const attrs = text => Object.fromEntries([...text.matchAll(/(\w+)="([^"]*)"/g)].map(([, k, v]) => [k, v]));
  const actual = [...trxText.matchAll(/<UnitTestResult\b[^>]*>/g)].map(([s]) => attrs(s));
  const counters = Object.fromEntries(Object.entries(attrs(trxText.match(/<Counters\b[^>]*\/>/)?.[0] ?? '')).map(([k, v]) => [k, Number(v)]));
  const expected = tests.map(t => t.uid).sort(), observed = actual.map(r => r.testId).sort();
  if (JSON.stringify(expected) !== JSON.stringify(observed) || actual.some(r => r.outcome !== 'Passed')
    || ['total', 'executed', 'passed'].some(k => counters[k] !== 2)
    || ['failed', 'error', 'timeout', 'aborted', 'inconclusive', 'notExecuted', 'notRunnable', 'disconnected'].some(k => counters[k] !== 0))
    throw Error('Actual lifecycle identities or terminal counters are incomplete');
  return {uids: expected, counters, scope: 'Driver lifecycle only; load budgets independently evaluated'};
}
