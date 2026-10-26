import { describe, expect, it } from 'vitest';
import { defaultWorkflowWorkCalendar, readWorkflowWorkCalendar } from './workflow-work-calendar';
describe('工作日历闭合草稿', () => {
  it('保留每周窗口、休息例外及24点结束，不混入发布快照', () => {
    const draft = { ...defaultWorkflowWorkCalendar(), exceptions: [{ date: '2026-10-26', ranges: [] }, { date: '2026-10-24', ranges: [{ start: '00:00', end: '24:00' }] }] };
    expect(readWorkflowWorkCalendar(draft)).toEqual(draft);
    expect(() => readWorkflowWorkCalendar({ ...draft, tenantId: 'foreign' })).toThrow();
    expect(() => readWorkflowWorkCalendar({ ...draft, timeZoneRules: 'client rules' })).toThrow();
  });
  it.each([
    { weekdays: [{ dayOfWeek: 1, ranges: [{ start: '09:00', end: '12:00' }, { start: '11:00', end: '13:00' }] }] },
    { weekdays: [{ dayOfWeek: 1, ranges: [{ start: '24:00', end: '24:00' }] }] },
    { weekdays: [{ dayOfWeek: 1, ranges: [{ start: '9:00', end: '10:00' }] }] },
    { weekdays: [{ dayOfWeek: 1, ranges: [{ start: '09:00', end: '10:00' }] }, { dayOfWeek: 1, ranges: [] }] },
    { weekdays: [{ dayOfWeek: 7, ranges: [{ start: '09:00', end: '10:00' }] }] },
    { exceptions: [{ date: '2025-02-29', ranges: [] }] },
    { exceptions: [{ date: '2026-10-26', ranges: [] }, { date: '2026-10-26', ranges: [] }] },
    { exceptions: Array.from({ length: 367 }, () => ({ date: '2026-10-26', ranges: [] })) },
    { weekdays: [] },
    { timeZoneKey: '' }
  ])('拒绝歧义或超界草稿 %j', patch => expect(() => readWorkflowWorkCalendar({ ...defaultWorkflowWorkCalendar(), ...patch })).toThrow());
  it('允许相邻窗口，且不修改传入对象', () => {
    const draft = { ...defaultWorkflowWorkCalendar(), weekdays: [{ dayOfWeek: 1, ranges: [{ start: '13:00', end: '17:00' }, { start: '09:00', end: '13:00' }] }] };
    const result = readWorkflowWorkCalendar(draft); expect(result).toEqual(draft); expect(result).not.toBe(draft);
    expect(() => readWorkflowWorkCalendar({ ...draft, weekdays: [{ ...draft.weekdays[0], ranges: Array.from({ length: 9 }, () => ({ start: '09:00', end: '10:00' })) }] })).toThrow();
  });
});
