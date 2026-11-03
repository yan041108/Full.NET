import { aiPreviewPersonalScheduleSources, validatePersonalScheduleSourcePreview } from '@fullnet/client-contracts';
import { http } from './http';

/** 显式来源预览只读本人日程；当前版本及用途由服务端重新证明。 */
export async function previewPersonalScheduleSources(scheduleIds: readonly string[], signal?: AbortSignal) {
  const frozen = [...scheduleIds];
  return validatePersonalScheduleSourcePreview(
    await aiPreviewPersonalScheduleSources(http, { body: { scheduleIds: frozen } }, signal), frozen);
}
