import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ElLoading } from 'element-plus';
import { useSessionStore } from '../auth/session';
import * as api from '../api/workflow-calendars';
import { defaultWorkflowWorkCalendar } from '../workflow/workflow-work-calendar';
import WorkflowCalendarsView from './WorkflowCalendarsView.vue';
vi.mock('../api/workflow-calendars', () => ({ listWorkflowWorkCalendars: vi.fn(), getWorkflowWorkCalendar: vi.fn(), createWorkflowWorkCalendar: vi.fn(),
  updateWorkflowWorkCalendar: vi.fn(), publishWorkflowWorkCalendar: vi.fn(), listWorkflowWorkCalendarVersions: vi.fn(), getWorkflowWorkCalendarVersion: vi.fn() }));
const id = '019a23f0-0000-7000-8000-000000000001'; const versionId = '019a23f0-0000-7000-8000-000000000002';
const record = { id, calendarKey: 'office.week', displayName: 'Office', draft: defaultWorkflowWorkCalendar(), revision: 1, latestVersionNumber: null, latestVersionId: null,
  createdAtUtc: '2026-10-26T00:00:00Z', updatedAtUtc: '2026-10-26T00:00:00Z' };
const version = { id: versionId, calendarId: id, versionNumber: 1, contentHash: 'a'.repeat(64), timeZoneKey: 'UTC', publishedAtUtc: '2026-10-26T00:00:00Z',
  calendar: { ...defaultWorkflowWorkCalendar(), timeZoneRules: 'rules' } };
function setup(permissions = ['workflow.calendars.read', 'workflow.calendars.create', 'workflow.calendars.update', 'workflow.calendars.publish']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.currentUser = { id, username: 'tester', displayName: 'Tester', tenantId: id, actorScope: 'tenant', scope: 'tenant', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 };
  return { wrapper: mount(WorkflowCalendarsView, { attachTo: document.body, global: { plugins: [pinia], directives: { loading: ElLoading.directive } } }), session };
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listWorkflowWorkCalendars).mockResolvedValue({ items: [], nextAfterId: null });
  vi.mocked(api.listWorkflowWorkCalendarVersions).mockResolvedValue({ items: [version], nextBeforeVersionNumber: null });
  vi.mocked(api.getWorkflowWorkCalendar).mockResolvedValue(record);
  vi.mocked(api.createWorkflowWorkCalendar).mockResolvedValue(record);
  vi.mocked(api.updateWorkflowWorkCalendar).mockResolvedValue({ ...record, displayName: 'Changed', revision: 2 });
  vi.mocked(api.publishWorkflowWorkCalendar).mockResolvedValue(version);
  vi.mocked(api.getWorkflowWorkCalendarVersion).mockResolvedValue(version);
});
async function createDraft(wrapper: ReturnType<typeof setup>['wrapper']) {
  await wrapper.get('[data-testid="work-calendar-create"]').trigger('click'); await flushPromises();
  await wrapper.get('input[data-testid="work-calendar-key"]').setValue(record.calendarKey);
  await wrapper.get('input[data-testid="work-calendar-name"]').setValue(record.displayName);
  await wrapper.get('[data-testid="work-calendar-save"]').trigger('click'); await flushPromises();
}
describe('工作日历管理页面', () => {
  it('创建后保存固定修订，查看发布历史不修改草稿', async () => {
    const { wrapper } = setup();
    try {
      await createDraft(wrapper);
      expect(api.createWorkflowWorkCalendar).toHaveBeenCalledWith({ calendarKey: record.calendarKey, displayName: record.displayName, draft: record.draft });
      await wrapper.get('input[data-testid="work-calendar-name"]').setValue('Changed');
      expect(wrapper.get('[data-testid="work-calendar-publish"]').attributes('disabled')).toBeDefined();
      await wrapper.get('[data-testid="work-calendar-save"]').trigger('click'); await flushPromises();
      expect(api.updateWorkflowWorkCalendar).toHaveBeenCalledWith(id, { expectedRevision: 1, displayName: 'Changed', draft: record.draft });
      const show = wrapper.findAll('.work-calendar-page__version button')[0]!; await show.trigger('click'); await flushPromises();
      expect(api.getWorkflowWorkCalendarVersion).toHaveBeenCalledWith(versionId);
      expect(wrapper.get('[data-testid="work-calendar-save"]').attributes('disabled')).toBeDefined();
      expect(api.publishWorkflowWorkCalendar).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });

  it('发布已成功但重读失败时显示回执并阻止重复发布', async () => {
    const { wrapper } = setup();
    try {
      await createDraft(wrapper); vi.mocked(api.getWorkflowWorkCalendar).mockRejectedValue(new Error('network'));
      await wrapper.get('[data-testid="work-calendar-publish"]').trigger('click'); await flushPromises();
      expect(api.publishWorkflowWorkCalendar).toHaveBeenCalledWith(id, { expectedRevision: 1 });
      expect(wrapper.text()).toContain('版本 1');
      expect(wrapper.get('[data-testid="work-calendar-publish"]').attributes('disabled')).toBeDefined();
      await wrapper.get('[data-testid="work-calendar-publish"]').trigger('click');
      expect(api.publishWorkflowWorkCalendar).toHaveBeenCalledTimes(1);
    } finally { wrapper.unmount(); }
  });

  it('租户切换后丢弃迟到创建响应，只读权限不显示写入按钮', async () => {
    let finish!: (value: api.WorkflowWorkCalendarResponse) => void;
    vi.mocked(api.createWorkflowWorkCalendar).mockReturnValue(new Promise(resolve => { finish = resolve; }));
    const { wrapper, session } = setup();
    try {
      await createDraft(wrapper); session.currentUser = { ...session.currentUser!, tenantId: versionId, permissions: ['workflow.calendars.read'] }; await flushPromises();
      finish(record); await flushPromises();
      expect(wrapper.find('[data-testid="work-calendar-create"]').exists()).toBe(false);
      expect(wrapper.find('[data-testid="work-calendar-publish"]').exists()).toBe(false);
      expect(api.listWorkflowWorkCalendarVersions).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });
});
