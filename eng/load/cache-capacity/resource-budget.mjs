// Windows 剩余内存与 WSL 内可回收页缓存分属不同层；同时检查，避免把已提交给虚拟机的页缓存算两遍。
export function guestMemory(text) {
  const fields = {};
  for (const line of text.trim().split(/\r?\n/)) {
    const m = /^(\w+):\s+(\d+)\s+kB$/.exec(line);
    if (!m) continue;
    if (Object.hasOwn(fields, m[1])) throw Error('Duplicate guest memory field');
    fields[m[1]] = Number(m[2]) * 1024;
  }
  for (const name of ['MemTotal', 'MemAvailable', 'Cached', 'SwapFree'])
    if (!Number.isSafeInteger(fields[name]) || fields[name] < 0) throw Error('Missing or invalid guest memory field: ' + name);
  if (fields.MemAvailable > fields.MemTotal) throw Error('Guest available memory exceeds total');
  return fields;
}
export function memoryWithinBudget(host, phase) {
  if (!['start', 'running'].includes(phase)) throw Error('Unknown memory budget phase');
  const gib = 1024 ** 3;
  const minimum = phase === 'start' ? {physical: 4, virtual: 5, guest: 14} : {physical: 2, virtual: 1, guest: 2};
  return Number.isFinite(host.FreePhysicalBytes) && Number.isFinite(host.FreeVirtualBytes)
    && Number.isFinite(host.Guest?.MemAvailable)
    && host.FreePhysicalBytes >= minimum.physical * gib && host.FreeVirtualBytes >= minimum.virtual * gib
    && host.Guest.MemAvailable >= minimum.guest * gib;
}

export function imageIdFromInspect(text) {
  const entries = JSON.parse(text);
  if (!Array.isArray(entries) || entries.length !== 1 || !/^sha256:[0-9a-f]{64}$/.test(entries[0]?.Id))
    throw Error('Expected exactly one immutable Docker image identity');
  return entries[0].Id;
}

export function collectorEndpoint(mapping) {
  const m = /^127\.0\.0\.1:(\d+)$/.exec(mapping.trim());
  if (!m || Number(m[1]) < 1 || Number(m[1]) > 65535) throw Error('Expected one loopback Collector port mapping');
  return 'http://127.0.0.1:' + Number(m[1]) + '/metrics';
}

// 资源所有权名称可以包含完整 UUID；网络 DNS 别名独立保持在单标签 63 字符限制内。
export const collectorDnsAlias = 'otel';
export const collectorOtlpEndpoint = 'http://' + collectorDnsAlias + ':4317';

// 独立认证修复候选使用独立所有权标签；只接受已知标签，不能用任意标签绕过卷归属核对。
export function privateVolumeOwnerLabel(value = 'fullnet.cache-sdk402.owner') {
  if (!['fullnet.cache-sdk402.owner', 'fullnet.cache-auth.owner'].includes(value))
    throw Error('Unknown private build volume owner label');
  return value;
}
// 后续批次可以收窄已诊断的 Provider 边界，但不能扩大既定档位或把未执行档位写成通过。
export function stageLevels(model, ceiling) {
  const levels = {closed: [1, 16, 64, 128], open: [50, 200, 500, 1000]}[model];
  if (!levels || (ceiling !== undefined && !levels.includes(ceiling))) throw Error('Invalid bounded stage ceiling');
  return levels.filter(value => ceiling === undefined || value <= ceiling);
}

// 连接预算复测只覆盖已失败档位及相邻对照；全量基线保留原档位，禁止静默扩大压力范围。
export function profileLevels(provider, model, selection, ceiling) {
  const bounded = stageLevels(model, ceiling);
  if (selection === undefined || selection === 'full') return bounded;
  if (selection !== 'connection-budget-recheck' || !['SqlServer', 'MySql'].includes(provider))
    throw Error('Unknown capacity profile selection');
  const levels = provider === 'MySql' ? {closed: [64, 128], open: [200, 500]} : {closed: [64], open: [500]};
  const selected = levels[model].filter(level => bounded.includes(level));
  if (!selected.length) throw Error('Capacity ceiling excludes every selected stage');
  return selected;
}
