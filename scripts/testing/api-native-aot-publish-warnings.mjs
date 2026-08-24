import path from 'node:path';

const allowedWarnings = new Map([
  ['MemoryPack.Core.dll', new Set(['IL2104', 'IL3053'])],
  ['Dapper.dll', new Set(['IL2104', 'IL3053'])],
  ['Microsoft.Data.SqlClient.dll', new Set(['IL2104', 'IL3053'])],
  ['Microsoft.Data.SqlClient.Internal.Logging.dll', new Set(['IL2104'])],
  ['System.Configuration.ConfigurationManager.dll', new Set(['IL2104'])],
  ['Confluent.Kafka.dll', new Set(['IL2104'])],
]);

/**
 * 只接受 ADR-0008 已登记的第三方程序集级 ILC 告警；自有代码或新告警码必须失败关闭。
 */
export function validatePublishWarnings(publishOutput) {
  const warnings = [];
  const rejected = [];
  for (const line of publishOutput.split(/\r?\n/)) {
    if (!/warning IL\d{4}:/.test(line)) {
      continue;
    }

    const match = /^(?<origin>.+?)\s+:\s+warning (?<code>IL\d{4}):/.exec(line);
    if (!match?.groups) {
      rejected.push(line.trim());
      continue;
    }

    const assembly = path.basename(match.groups.origin.replace(/\\/g, '/'));
    const code = match.groups.code;
    const allowedCodes = allowedWarnings.get(assembly);
    const warning = { assembly, code, line: line.trim() };
    warnings.push(warning);
    if (!allowedCodes?.has(code)) {
      rejected.push(warning.line);
    }
  }

  if (rejected.length > 0) {
    throw new Error(
      `发现未登记的 Native AOT publish warning：\n${rejected.join('\n')}`
    );
  }

  return warnings;
}
