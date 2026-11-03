import type { AiPersonalScheduleCompletionProposal, AiPersonalScheduleCompletionStatus } from './generated/models.generated.js';
import { readAiPersonalScheduleCompletionProposal, readAiPersonalScheduleCompletionStatus } from './generated/guards.generated.js';
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const positive = (value: number) => Number.isSafeInteger(value) && value > 0 && value < 2147483647;
const invalid = () => new Error('client.invalid_schedule_completion_response');
const closed = (value: object, keys: string[]) => Object.keys(value).every(key => keys.includes(key));

/** 运行成功、意图accepted和unknown均不证明业务完成；未知不能填零版本或假造时间。 */
export function validateScheduleCompletionStatus(input: unknown, runId: string): AiPersonalScheduleCompletionStatus {
  const value = readAiPersonalScheduleCompletionStatus(input), complete = value.state === 'completed', rejected = value.state === 'rejected';
  if (!closed(value, ['runId','operationId','scheduleId','expectedVersion','state','reason','actualVersion','completedAtUtc'])
    || value.runId !== runId || ![value.runId,value.operationId,value.scheduleId].every(id => guid.test(id)) || !positive(value.expectedVersion)
    || !['accepted','unknown','completed','rejected'].includes(value.state)
    || (complete ? value.actualVersion !== value.expectedVersion + 1 || !value.completedAtUtc || !Number.isFinite(Date.parse(value.completedAtUtc))
      || !/(?:Z|\+00:00)$/.test(value.completedAtUtc) || value.reason != null : value.actualVersion != null || value.completedAtUtc != null)
    || (rejected ? !['authorization_unavailable','intent_expired','target_changed'].includes(value.reason ?? '') : value.reason != null)) throw invalid();
  return value;
}

/** 公开提案与准确原运行/目标/静态工具及参数同时绑定，不交付或接收主体、租户或私人正文。 */
export function validateScheduleCompletionProposal(input: unknown, runId: string): AiPersonalScheduleCompletionProposal {
  const value = readAiPersonalScheduleCompletionProposal(input);
  if (!closed(value,['runId','operationId','scheduleId','expectedVersion','toolName','toolVersion','argumentsJson']) || value.runId !== runId
    || ![value.runId,value.operationId,value.scheduleId].every(id => guid.test(id)) || !positive(value.expectedVersion)
    || value.toolName !== 'calendar.personal_schedules.complete' || value.toolVersion !== 1 || value.argumentsJson.length > 1024) throw invalid();
  let args: unknown; try { args = JSON.parse(value.argumentsJson); } catch { throw invalid(); }
  if (typeof args !== 'object' || args === null || Array.isArray(args) || !closed(args,['scheduleId','expectedVersion'])
    || (args as Record<string,unknown>).scheduleId !== value.scheduleId || (args as Record<string,unknown>).expectedVersion !== value.expectedVersion) throw invalid();
  if (value.argumentsJson !== JSON.stringify({scheduleId:value.scheduleId,expectedVersion:value.expectedVersion})) throw invalid();
  return value;
}
