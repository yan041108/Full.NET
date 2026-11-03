import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import Dialog from './AiPersonalScheduleSourceDialog.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-personal-schedule-assistant';
vi.mock('../api/ai-personal-schedule-assistant', () => ({ previewPersonalScheduleSources: vi.fn() }));
const id = '01900000-0000-7000-8000-000000000001';
const value = () => ({ classification: 'restricted', sources: [{ kind: 'calendar-personal-schedule-v1', purpose: 'knowledge-assistant-v1', id,
  version: 1, projectionHash: 'A'.repeat(64), content: '<script>日程纯文本</script>', startAtUtc: '2026-10-30T00:00:00Z', endAtUtc: '2026-10-30T01:00:00Z', status: 'pending' }] });
const wrappers: VueWrapper[] = [];
function render(permissions = ['calendar.personal_schedules.read', 'calendar.personal_schedules.assistant_read'], expectedHash?: string) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { permissions } as NonNullable<typeof session.currentUser>; session.switching = false;
  const wrapper = mount(Dialog, { props: { scheduleId: id, expectedVersion: 1, expectedHash }, global: { plugins: [pinia], stubs: {
    ElDialog: { template: '<section><slot/><slot name="footer"/></section>' },
    AiPersonalScheduleAskPanel: { name: 'AiPersonalScheduleAskPanel', template: '<div data-testid="ask-panel-slot" />' }
  } } });
  wrappers.push(wrapper); return { wrapper, session };
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.previewPersonalScheduleSources).mockResolvedValue(value()); });
afterEach(() => { for (const wrapper of wrappers) wrapper.unmount(); wrappers.length = 0; });
it('从真实API包装器读取固定版本，正文安全渲染为文本', async () => {
  const { wrapper } = render(); await flushPromises();
  expect(api.previewPersonalScheduleSources).toHaveBeenCalledWith([id], expect.any(AbortSignal));
  expect(wrapper.text()).toContain('<script>日程纯文本</script>'); expect(wrapper.findAll('script')).toHaveLength(0);
  expect(wrapper.text()).toContain('不会调用模型或更改日程');
});
it('缺少独立用途权限不读取来源或显示重新读取按钮', async () => {
  const { wrapper } = render(['calendar.personal_schedules.read']); await flushPromises();
  expect(api.previewPersonalScheduleSources).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="personal-schedule-source"]').exists()).toBe(false);
});
it('准确版本变化拒绝交付新正文并提示刷新', async () => {
  const changed = value(); changed.sources[0]!.version = 2; vi.mocked(api.previewPersonalScheduleSources).mockResolvedValue(changed);
  const { wrapper } = render(); await flushPromises(); expect(wrapper.text()).not.toContain('日程纯文本'); expect(wrapper.text()).toContain('日程已变化');
});
it('持久引用摘要变化时不能用相同版本的新正文替换旧证明', async () => {
  const { wrapper } = render(undefined, 'B'.repeat(64)); await flushPromises();
  expect(wrapper.text()).not.toContain('日程纯文本'); expect(wrapper.emitted('invalidated')).toBeTruthy();
});
it('业务来源默认接入问答，显式只读引用可以关闭问答', async () => {
  const { wrapper } = render(); await flushPromises(); expect(wrapper.find('[data-testid="ask-panel-slot"]').exists()).toBe(true);
  await wrapper.setProps({ allowAsk: false }); expect(wrapper.find('[data-testid="ask-panel-slot"]').exists()).toBe(false);
});
it('撤权后同步清空已显示来源并关闭', async () => {
  const { wrapper, session } = render(); await flushPromises(); expect(wrapper.text()).toContain('日程纯文本');
  session.currentUser = { ...session.currentUser!, permissions: [] }; await flushPromises();
  expect(wrapper.text()).not.toContain('日程纯文本'); expect(wrapper.emitted('close')).toBeTruthy();
});
it('切换会话时取消在途请求，迟到响应不能重新写回', async () => {
  let finish!: (reply: ReturnType<typeof value>) => void;
  vi.mocked(api.previewPersonalScheduleSources).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  const { wrapper, session } = render(); await flushPromises();
  const signal = vi.mocked(api.previewPersonalScheduleSources).mock.calls[0]![1]!;
  session.switching = true; await flushPromises(); expect(signal.aborted).toBe(true);
  finish(value()); await flushPromises(); expect(wrapper.text()).not.toContain('日程纯文本');
});
it('来源409后清空原结果，不能保留旧正文', async () => {
  const { wrapper } = render(); await flushPromises();
  vi.mocked(api.previewPersonalScheduleSources).mockRejectedValue({ status: 409 });
  await wrapper.findAll('button').find(button => button.text() === '重新读取')!.trigger('click'); await flushPromises();
  expect(wrapper.text()).not.toContain('日程纯文本'); expect(wrapper.text()).toContain('日程已变化');
  expect(wrapper.emitted('invalidated')).toBeTruthy();
});
