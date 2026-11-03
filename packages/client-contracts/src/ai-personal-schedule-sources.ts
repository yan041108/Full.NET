import type { AiPersonalScheduleSourcePreview } from './generated/models.generated.js';
import { readAiPersonalScheduleSourcePreview } from './generated/guards.generated.js';

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const digest = /^[0-9A-F]{64}$/;
const invalid = () => new Error('client.invalid_personal_schedule_sources');

/** 固定本人日程投影必须完整对应实际选择，不把展示响应当成可再次提交的授权凭证。 */
export function validatePersonalScheduleSourcePreview(value: unknown, expectedIds: readonly string[]): AiPersonalScheduleSourcePreview {
  let preview: AiPersonalScheduleSourcePreview;
  try { preview = readAiPersonalScheduleSourcePreview(value); } catch { throw invalid(); }
  if (!preview || preview.classification !== 'restricted' || expectedIds.length < 1 || expectedIds.length > 8
    || expectedIds.some(id => !uuid.test(id)) || new Set(expectedIds.map(id => id.toLowerCase())).size !== expectedIds.length
    || preview.sources.length !== expectedIds.length) throw invalid();
  for (const [index, row] of preview.sources.entries()) {
    if (row.kind !== 'calendar-personal-schedule-v1' || row.purpose !== 'knowledge-assistant-v1'
      || !uuid.test(row.id) || row.id.toLowerCase() !== expectedIds[index]?.toLowerCase()
      || !Number.isSafeInteger(row.version) || row.version < 1 || !digest.test(row.projectionHash)
      || row.content.length < 1 || row.content.length > 256 || !['pending', 'completed'].includes(row.status)
      || !Number.isFinite(Date.parse(row.startAtUtc)) || !Number.isFinite(Date.parse(row.endAtUtc))
      || !row.startAtUtc.endsWith('Z') && !row.startAtUtc.endsWith('+00:00')
      || !row.endAtUtc.endsWith('Z') && !row.endAtUtc.endsWith('+00:00')
      || Date.parse(row.startAtUtc) > Date.parse(row.endAtUtc)) throw invalid();
  }
  return preview;
}
