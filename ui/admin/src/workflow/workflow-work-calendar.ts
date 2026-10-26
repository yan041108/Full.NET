import type { WorkflowWorkCalendarDraft, WorkflowWorkCalendarRange } from '@fullnet/client-contracts';

const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const exact = (value: Record<string, unknown>, keys: readonly string[]) => Object.keys(value).every(key => keys.includes(key));
const invalid = (): never => { throw new TypeError('client.invalid_workflow_work_calendar'); };

/** 日历草稿只包含窗口与例外，不接受发布快照、时区规则或租户字段。 */
export function readWorkflowWorkCalendar(value: unknown): WorkflowWorkCalendarDraft {
  if (!object(value) || !exact(value, ['schemaVersion', 'timeZoneKey', 'weekdays', 'exceptions']) || value.schemaVersion !== 1 ||
    typeof value.timeZoneKey !== 'string' || !value.timeZoneKey.trim() || value.timeZoneKey.length > 128 ||
    !Array.isArray(value.weekdays) || value.weekdays.length > 7 ||
    value.exceptions !== undefined && value.exceptions !== null && (!Array.isArray(value.exceptions) || value.exceptions.length > 366)) return invalid();
  const days = new Set<number>();
  const weekdays = value.weekdays.map(day => {
    if (!object(day) || !exact(day, ['dayOfWeek', 'ranges']) || typeof day.dayOfWeek !== 'number' || !Number.isInteger(day.dayOfWeek) || day.dayOfWeek < 0 || day.dayOfWeek > 6 || days.has(day.dayOfWeek)) return invalid();
    days.add(day.dayOfWeek); return { dayOfWeek: day.dayOfWeek, ranges: readRanges(day.ranges) };
  });
  const dates = new Set<string>();
  const exceptions = ((value.exceptions ?? []) as unknown[]).map(day => {
    if (!object(day) || !exact(day, ['date', 'ranges']) || !validDate(day.date) || dates.has(day.date)) return invalid();
    dates.add(day.date); return { date: day.date, ranges: readRanges(day.ranges) };
  });
  if (!weekdays.some(day => day.ranges.length) && !exceptions.some(day => day.ranges.length)) return invalid();
  const result = { schemaVersion: 1, timeZoneKey: value.timeZoneKey, weekdays, exceptions };
  if (new TextEncoder().encode(JSON.stringify(result)).length > 131072) return invalid();
  return result;
}

function readRanges(value: unknown): WorkflowWorkCalendarRange[] {
  if (!Array.isArray(value) || value.length > 8) return invalid();
  const ranges = value.map(range => {
    if (!object(range) || !exact(range, ['start', 'end']) || typeof range.start !== 'string' || typeof range.end !== 'string') return invalid();
    const from = minute(range.start, false); const to = minute(range.end, true);
    if (from === undefined || to === undefined || from >= to) return invalid();
    return { start: range.start, end: range.end };
  });
  const ordered = [...ranges].sort((left, right) => minute(left.start, false)! - minute(right.start, false)!);
  for (let index = 1; index < ordered.length; index++) if (minute(ordered[index]!.start, false)! < minute(ordered[index - 1]!.end, true)!) return invalid();
  return ranges;
}
function minute(value: string, end: boolean): number | undefined {
  if (end && value === '24:00') return 1440;
  if (!/^([01]\d|2[0-3]):[0-5]\d$/u.test(value)) return undefined;
  return Number(value.slice(0, 2)) * 60 + Number(value.slice(3));
}
function validDate(value: unknown): value is string {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  return year! >= 1000 && year! < 9999 && month! >= 1 && month! <= 12 && day! >= 1 && day! <= new Date(Date.UTC(year!, month!, 0)).getUTCDate();
}

export function defaultWorkflowWorkCalendar(): WorkflowWorkCalendarDraft {
  return { schemaVersion: 1, timeZoneKey: 'UTC', weekdays: [1, 2, 3, 4, 5].map(dayOfWeek => ({ dayOfWeek, ranges: [{ start: '09:00', end: '17:00' }] })), exceptions: [] };
}
