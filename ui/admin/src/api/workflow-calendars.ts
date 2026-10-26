import { workflowListWorkCalendars, workflowGetWorkCalendar, workflowListWorkCalendarVersions, workflowGetWorkCalendarVersion,
  workflowCreateWorkCalendar, workflowUpdateWorkCalendarDraft, workflowPublishWorkCalendar,
  type WorkflowWorkCalendarResponse, type WorkflowWorkCalendarSummary, type WorkflowWorkCalendarVersionResponse,
  type WorkflowWorkCalendarVersionSummary, type WorkflowWorkCalendarPageResponse, type WorkflowWorkCalendarVersionPageResponse,
  type CreateWorkflowWorkCalendarRequest, type UpdateWorkflowWorkCalendarRequest, type PublishWorkflowWorkCalendarRequest } from '@fullnet/client-contracts';
import { http } from './http';
import { readWorkflowWorkCalendar } from '../workflow/workflow-work-calendar';
export type { WorkflowWorkCalendarResponse, WorkflowWorkCalendarSummary, WorkflowWorkCalendarVersionResponse, WorkflowWorkCalendarVersionSummary };
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const uuid = (value: unknown): value is string => typeof value === 'string' && /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(value);
const positive = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) > 0;
const instant = (value: unknown): value is string => typeof value === 'string' && Number.isFinite(Date.parse(value));
const hash = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{64}$/u.test(value);
const invalid = (): never => { throw new TypeError('client.invalid_workflow_work_calendar'); };
const exact = (value: unknown, keys: readonly string[]): value is Record<string, unknown> => object(value) && Object.keys(value).every(key => keys.includes(key));
const validName = (value: unknown): value is string => typeof value === 'string' && !!value.trim() && value.length <= 200;

function readSummary(value: unknown): void {
  if (!object(value) || !uuid(value.id) || typeof value.calendarKey !== 'string' || !/^[A-Za-z][A-Za-z0-9_.-]{2,127}$/u.test(value.calendarKey) ||
    typeof value.displayName !== 'string' || !value.displayName.trim() || value.displayName.length > 200 || !positive(value.revision) ||
    !instant(value.createdAtUtc) || !instant(value.updatedAtUtc) ||
    !((value.latestVersionNumber === null && value.latestVersionId === null) || positive(value.latestVersionNumber) && uuid(value.latestVersionId))) return invalid();
}
function readCalendar(value: unknown, expectedId?: string): WorkflowWorkCalendarResponse {
  readSummary(value); if (!object(value) || expectedId && value.id !== expectedId) return invalid();
  readWorkflowWorkCalendar(value.draft); return value as unknown as WorkflowWorkCalendarResponse;
}
function readVersionSummary(value: unknown, calendarId?: string): void {
  if (!object(value) || !uuid(value.id) || !uuid(value.calendarId) || calendarId && value.calendarId !== calendarId || !positive(value.versionNumber) ||
    !hash(value.contentHash) || typeof value.timeZoneKey !== 'string' || !value.timeZoneKey.trim() || value.timeZoneKey.length > 128 || !instant(value.publishedAtUtc)) return invalid();
}
export async function listWorkflowWorkCalendars(afterId?: string, signal?: AbortSignal): Promise<WorkflowWorkCalendarPageResponse> {
  if (afterId !== undefined && !uuid(afterId)) return invalid();
  const value = await workflowListWorkCalendars(http, { afterId }, signal);
  if (!object(value) || !Array.isArray(value.items) || value.items.length > 20 || new Set(value.items.map(item => object(item) ? item.id : undefined)).size !== value.items.length) return invalid();
  value.items.forEach(readSummary);
  if (value.nextAfterId !== null && (!uuid(value.nextAfterId) || value.items.length !== 20 || !object(value.items.at(-1)) || value.items.at(-1)!.id !== value.nextAfterId)) return invalid();
  return value as unknown as WorkflowWorkCalendarPageResponse;
}
export async function getWorkflowWorkCalendar(calendarId: string, signal?: AbortSignal): Promise<WorkflowWorkCalendarResponse> {
  if (!uuid(calendarId)) return invalid();return readCalendar(await workflowGetWorkCalendar(http, { calendarId }, signal), calendarId);
}
export async function listWorkflowWorkCalendarVersions(calendarId: string, beforeVersionNumber?: number, signal?: AbortSignal): Promise<WorkflowWorkCalendarVersionPageResponse> {
  if (!uuid(calendarId) || beforeVersionNumber !== undefined && !positive(beforeVersionNumber)) return invalid();
  const value = await workflowListWorkCalendarVersions(http, { calendarId, beforeVersionNumber }, signal);
  if (!object(value) || !Array.isArray(value.items) || value.items.length > 20) return invalid();
  let previous = beforeVersionNumber ?? Number.MAX_SAFE_INTEGER;
  const seen = new Set<string>();for (const item of value.items) {
    readVersionSummary(item, calendarId);if (!object(item) || seen.has(String(item.id)) || Number(item.versionNumber) >= previous) return invalid();
    seen.add(String(item.id));previous = Number(item.versionNumber);
  }
  if (value.nextBeforeVersionNumber !== null && (!positive(value.nextBeforeVersionNumber) || value.items.length !== 20 || value.nextBeforeVersionNumber !== previous)) return invalid();
  return value as unknown as WorkflowWorkCalendarVersionPageResponse;
}
export async function getWorkflowWorkCalendarVersion(versionId: string, signal?: AbortSignal): Promise<WorkflowWorkCalendarVersionResponse> {
  if (!uuid(versionId)) return invalid();const value = await workflowGetWorkCalendarVersion(http, { versionId }, signal);
  readVersionSummary(value);if (!object(value) || value.id !== versionId) return invalid();
  const calendar = readWorkflowWorkCalendar(value.calendar);if (calendar.timeZoneKey !== value.timeZoneKey) return invalid();
  return value as unknown as WorkflowWorkCalendarVersionResponse;
}
export async function createWorkflowWorkCalendar(body: CreateWorkflowWorkCalendarRequest, signal?: AbortSignal): Promise<WorkflowWorkCalendarResponse> {
  if (!exact(body, ['calendarKey', 'displayName', 'draft']) || typeof body.calendarKey !== 'string' || !/^[A-Za-z][A-Za-z0-9_.-]{2,127}$/u.test(body.calendarKey) || !validName(body.displayName)) return invalid();
  const request = { calendarKey: body.calendarKey, displayName: body.displayName, draft: readWorkflowWorkCalendar(body.draft) };
  const value = readCalendar(await workflowCreateWorkCalendar(http, { body: request }, signal));
  if (value.calendarKey !== body.calendarKey || value.revision !== 1) return invalid();return value;
}
export async function updateWorkflowWorkCalendar(calendarId: string, body: UpdateWorkflowWorkCalendarRequest, signal?: AbortSignal): Promise<WorkflowWorkCalendarResponse> {
  if (!uuid(calendarId) || !exact(body, ['expectedRevision', 'displayName', 'draft']) || !positive(body.expectedRevision) || !validName(body.displayName)) return invalid();
  const request = { expectedRevision: body.expectedRevision, displayName: body.displayName, draft: readWorkflowWorkCalendar(body.draft) };
  const value = readCalendar(await workflowUpdateWorkCalendarDraft(http, { calendarId, body: request }, signal), calendarId);
  if (value.revision !== body.expectedRevision + 1) return invalid();return value;
}
export async function publishWorkflowWorkCalendar(calendarId: string, body: PublishWorkflowWorkCalendarRequest, signal?: AbortSignal): Promise<WorkflowWorkCalendarVersionResponse> {
  if (!uuid(calendarId) || !exact(body, ['expectedRevision']) || !positive(body.expectedRevision)) return invalid();
  const value = await workflowPublishWorkCalendar(http, { calendarId, body: { expectedRevision: body.expectedRevision } }, signal);
  readVersionSummary(value, calendarId);if (!object(value)) return invalid();
  if (readWorkflowWorkCalendar(value.calendar).timeZoneKey !== value.timeZoneKey) return invalid();
  return value as unknown as WorkflowWorkCalendarVersionResponse;
}
