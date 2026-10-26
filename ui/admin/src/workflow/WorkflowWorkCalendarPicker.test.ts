import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ElSelect } from 'element-plus';
import { useSessionStore } from '../auth/session';
import * as api from '../api/workflow-calendars';
import WorkflowWorkCalendarPicker from './WorkflowWorkCalendarPicker.vue';
vi.mock('../api/workflow-calendars', () => ({ listWorkflowWorkCalendars: vi.fn(), listWorkflowWorkCalendarVersions: vi.fn(), getWorkflowWorkCalendarVersion: vi.fn() }));
const id = '019a23f0-0000-7000-8000-000000000001'; const versionId = '019a23f0-0000-7000-8000-000000000002';
const version = { id: versionId, calendarId: id, versionNumber: 1, contentHash: 'a'.repeat(64), timeZoneKey: 'UTC', publishedAtUtc: '2026-10-26T00:00:00Z' };
function setup() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.currentUser = { id, username: 'tester', displayName: 'Tester', tenantId: id, actorScope: 'tenant', scope: 'tenant', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions: ['workflow.calendars.read'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 };
  return { pinia, session };
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listWorkflowWorkCalendars).mockResolvedValue({ items: [{ id, calendarKey: 'office.week', displayName: 'Office', revision: 1, latestVersionNumber: 1,
    latestVersionId: versionId, timeZoneKey: 'UTC', createdAtUtc: '2026-10-26T00:00:00Z', updatedAtUtc: '2026-10-26T00:00:00Z' }], nextAfterId: null });
  vi.mocked(api.listWorkflowWorkCalendarVersions).mockResolvedValue({ items: [version], nextBeforeVersionNumber: null });
});
describe('固定工作日历版本选择', () => {
  it('选择日历后仍须显式选择已发布版本，只读时忽略事件', async () => {
    const { pinia } = setup(); const wrapper = mount(WorkflowWorkCalendarPicker, { props: { disabled: false }, global: { plugins: [pinia] } });
    try {
      await flushPromises(); wrapper.findAllComponents(ElSelect)[0]!.vm.$emit('update:modelValue', id); await flushPromises();
      expect(wrapper.emitted('select')).toEqual([[undefined]]);
      wrapper.findAllComponents(ElSelect)[1]!.vm.$emit('update:modelValue', versionId); await flushPromises();
      expect(wrapper.emitted('select')!.at(-1)).toEqual([version]);
      await wrapper.setProps({ disabled: true }); const count = wrapper.emitted('select')!.length;
      wrapper.findAllComponents(ElSelect)[1]!.vm.$emit('update:modelValue', versionId);
      expect(wrapper.emitted('select')).toHaveLength(count);
    } finally { wrapper.unmount(); }
  });

  it('租户切换清除固定版本，旧作用域读取不能回填', async () => {
    let finish!: (value: api.WorkflowWorkCalendarVersionResponse) => void;
    vi.mocked(api.getWorkflowWorkCalendarVersion).mockReturnValue(new Promise(resolve => { finish = resolve; }));
    const { pinia, session } = setup(); const wrapper = mount(WorkflowWorkCalendarPicker, { props: { disabled: false, modelValue: versionId }, global: { plugins: [pinia] } });
    try {
      await flushPromises(); session.currentUser = { ...session.currentUser!, tenantId: versionId }; await flushPromises();
      finish({ ...version, calendar: { schemaVersion: 1, timeZoneKey: 'UTC', weekdays: [] } }); await flushPromises();
      expect(wrapper.emitted('select')!.at(-1)).toEqual([undefined]);
      expect(wrapper.findAllComponents(ElSelect)[0]!.props('modelValue')).toBe('');
      expect(api.listWorkflowWorkCalendarVersions).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });
});
