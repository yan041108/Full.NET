import { describe, expect, it } from 'vitest';
import { validatePersonalScheduleSourcePreview } from './ai-personal-schedule-sources.js';

const id = '01900000-0000-7000-8000-000000000001';
const source = () => ({ classification: 'restricted', sources: [{ kind: 'calendar-personal-schedule-v1', purpose: 'knowledge-assistant-v1', id,
  version: 1, projectionHash: 'A'.repeat(64), content: '<script>纯文本</script>', startAtUtc: '2026-10-30T00:00:00+00:00',
  endAtUtc: '2026-10-30T00:00:00Z', status: 'pending' }] });
describe('本人日程固定来源投影', () => {
  it('保留完整文本和合法零时长，不把预览当作已批准动作', () => {
    expect(validatePersonalScheduleSourcePreview(source(), [id]).sources[0]?.content).toContain('<script>');
  });
  it.each(['kind', 'purpose', 'id', 'version', 'projectionHash', 'status', 'startAtUtc', 'content'])('拒绝漂移字段 %s', field => {
    const value = source(); Object.assign(value.sources[0]!, { [field]: field === 'version' ? 0 : field === 'content' ? '中'.repeat(257) : 'invalid' });
    expect(() => validatePersonalScheduleSourcePreview(value, [id])).toThrow('client.invalid_personal_schedule_sources');
  });
  it('拒绝来源缺失和附加项，不能仅校验命中引用', () => {
    const value = source(); value.sources.push({ ...value.sources[0]!, id: '01900000-0000-7000-8000-000000000002' });
    expect(() => validatePersonalScheduleSourcePreview(value, [id])).toThrow();
    expect(() => validatePersonalScheduleSourcePreview(source(), [id, value.sources[1]!.id])).toThrow();
  });
  it('拒绝公共分类、重复选择与非UTC日期', () => {
    const value = source(); value.classification = 'public'; expect(() => validatePersonalScheduleSourcePreview(value, [id])).toThrow();
    expect(() => validatePersonalScheduleSourcePreview(source(), [id, id])).toThrow();
    const local = source(); local.sources[0]!.startAtUtc = '2026-10-30T00:00:00+08:00';
    expect(() => validatePersonalScheduleSourcePreview(local, [id])).toThrow();
  });
});
