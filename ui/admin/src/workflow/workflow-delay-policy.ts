import { readWorkflowWorkCalendar } from './workflow-work-calendar';
/** 工作时长只引用固定版本；发布快照只做无损保存，由服务端重新绑定。 */
export type WorkflowDelayPolicy = { modeKey: 'relative'; timeZoneKey: string; duration: number; unitKey: 'second' | 'minute' | 'hour' | 'day' }
  | { modeKey: 'working'; timeZoneKey: string; duration: number; unitKey: 'second' | 'minute' | 'hour' | 'day'; calendarVersionId: string; boundCalendarHash?: string; boundCalendar?: Record<string, unknown> }
  | { modeKey: 'absolute'; timeZoneKey: string; at: string; fieldKey?: never }
  | { modeKey: 'absolute'; timeZoneKey: string; fieldKey: string; at?: never };
const multipliers = { second: 1, minute: 60, hour: 3600, day: 86400 } as const;
const invalid = (): never => { throw new Error('client.invalid_workflow_delay_policy'); };

export function readWorkflowDelayPolicy(value: unknown): WorkflowDelayPolicy {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return invalid();
  const item = value as Record<string, unknown>;
  if (item.modeKey === 'working') return readWorking(item);
  if (Object.keys(item).some(key => !['modeKey', 'timeZoneKey', 'duration', 'unitKey', 'at', 'fieldKey'].includes(key)) ||
    typeof item.timeZoneKey !== 'string' || item.timeZoneKey.length > 128 || !item.timeZoneKey.trim()) return invalid();
  try { new Intl.DateTimeFormat('en', { timeZone: item.timeZoneKey }).format(); } catch { return invalid(); }
  if (item.modeKey === 'relative') {
    if ('at' in item || 'fieldKey' in item || typeof item.unitKey !== 'string' || !Object.hasOwn(multipliers, item.unitKey) ||
      typeof item.duration !== 'number' || !Number.isSafeInteger(item.duration) || item.duration < 1 ||
      item.duration * multipliers[item.unitKey as keyof typeof multipliers] > 31622400) return invalid();
    return { modeKey: 'relative', timeZoneKey: item.timeZoneKey, duration: item.duration, unitKey: item.unitKey as keyof typeof multipliers };
  }
  if (item.modeKey !== 'absolute' || 'duration' in item || 'unitKey' in item || ('at' in item) === ('fieldKey' in item)) return invalid();
  if ('fieldKey' in item) {
    if (typeof item.fieldKey !== 'string' || !/^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u.test(item.fieldKey)) return invalid();
    return { modeKey: 'absolute', timeZoneKey: item.timeZoneKey, fieldKey: item.fieldKey };
  }
  if (typeof item.at !== 'string' || !validInstant(item.at)) return invalid();
  return { modeKey: 'absolute', timeZoneKey: item.timeZoneKey, at: item.at };
}

function readWorking(item: Record<string, unknown>): WorkflowDelayPolicy {
  if (Object.keys(item).some(key => !['modeKey', 'timeZoneKey', 'duration', 'unitKey', 'calendarVersionId', 'boundCalendarHash', 'boundCalendar'].includes(key)) ||
    typeof item.timeZoneKey !== 'string' || !item.timeZoneKey.trim() || item.timeZoneKey.length > 128 || typeof item.calendarVersionId !== 'string' ||
    !/^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(item.calendarVersionId) ||
    typeof item.unitKey !== 'string' || !Object.hasOwn(multipliers, item.unitKey) || typeof item.duration !== 'number' || !Number.isSafeInteger(item.duration) ||
    item.duration < 1 || item.duration * multipliers[item.unitKey as keyof typeof multipliers] > 31622400 ||
    ('boundCalendarHash' in item) !== ('boundCalendar' in item)) return invalid();
  if ('boundCalendar' in item) {
    const snapshot = item.boundCalendar;
    if (!snapshot || typeof snapshot !== 'object' || Array.isArray(snapshot) || typeof item.boundCalendarHash !== 'string' || !/^[0-9a-f]{64}$/u.test(item.boundCalendarHash)) return invalid();
    const copy = { ...snapshot } as Record<string, unknown>;
    if (typeof copy.timeZoneRules !== 'string' || !copy.timeZoneRules.length || copy.timeZoneRules.length > 65536 ||
      copy.timeZoneKey !== item.timeZoneKey || new TextEncoder().encode(JSON.stringify(copy)).length > 131072) return invalid();
    delete copy.timeZoneRules;
    try { readWorkflowWorkCalendar(copy); } catch { return invalid(); }
  }
  return { modeKey: 'working', timeZoneKey: item.timeZoneKey, duration: item.duration, unitKey: item.unitKey as keyof typeof multipliers, calendarVersionId: item.calendarVersionId,
    ...('boundCalendar' in item ? { boundCalendarHash: item.boundCalendarHash as string, boundCalendar: JSON.parse(JSON.stringify(item.boundCalendar)) as Record<string, unknown> } : {}) };
}

function validInstant(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(Z|[+-](\d{2}):(\d{2}))$/u.exec(value);
  if (!match) return false;
  const [year, month, day, hour, minute, second] = match.slice(1, 7).map(Number);
  const offsetHour = Number(match[8] ?? 0); const offsetMinute = Number(match[9] ?? 0);
  return year! >= 1000 && year! < 9999 && month! >= 1 && month! <= 12 && day! >= 1 &&
    day! <= new Date(Date.UTC(year!, month!, 0)).getUTCDate() && hour! <= 23 && minute! <= 59 && second! <= 59 &&
    offsetHour <= 14 && offsetMinute <= 59 && (offsetHour < 14 || offsetMinute === 0) && Number.isFinite(Date.parse(value));
}
