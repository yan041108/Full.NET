import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { ElInput, ElSelect } from 'element-plus';
import Panel from './AiPersonalScheduleAskPanel.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-model-configs';
vi.mock('../api/ai-model-configs', () => ({ listAiModelConfigs: vi.fn() }));
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const sources = [{ id: id(1), version: 2, projectionHash: 'A'.repeat(64), kind: 'calendar-personal-schedule-v1', purpose: 'knowledge-assistant-v1',
  content: '私人字段', startAtUtc: '2026-10-31T00:00:00Z', endAtUtc: '2026-10-31T00:00:00Z', status: 'pending' }];
const wrappers: VueWrapper[] = [];
async function render(allowed = true) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); vi.spyOn(session, 'can').mockReturnValue(allowed);
  session.currentUser = { tenantId: null } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(Panel, { props: { sources }, global: { plugins: [pinia], stubs: {
    AiKnowledgeBaseMultiSelect: { props: ['modelValue'], template: '<button data-testid="select-base" @click="$emit(\'update:modelValue\', [\'01900000-0000-7000-8000-000000000002\'])">选择库</button>' },
    AiKnowledgeSearchAnswerDialog: { name: 'AiKnowledgeSearchAnswerDialog', props: ['open', 'target'], template: '<div data-testid="frozen-answer" />' }
  } } }); wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
beforeEach(() => { vi.mocked(api.listAiModelConfigs).mockResolvedValue({ items: [{ id: id(3), name: '索引模型', modelId: 'text-embedding-3-small', isEnabled: true }], page: 1, pageSize: 100, total: 1 } as Awaited<ReturnType<typeof api.listAiModelConfigs>>); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); vi.clearAllMocks(); });
it('缺少精确权限不读取模型或显示问答入口', async () => {
  const { wrapper } = await render(false); expect(api.listAiModelConfigs).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="schedule-ask-panel"]').exists()).toBe(false);
});
it('显式选库和模型后只冻结来源期望值，不把私人正文作为客户端授权', async () => {
  const { wrapper } = await render(); await wrapper.get('[data-testid="select-base"]').trigger('click');
  wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', id(3)); wrapper.findComponent(ElInput).vm.$emit('update:modelValue', '结合日程提问');
  await flushPromises(); await wrapper.get('[data-testid="schedule-ask-prepare"]').trigger('click'); await flushPromises();
  const target = wrapper.findComponent({ name: 'AiKnowledgeSearchAnswerDialog' }).props('target');
  expect(target).toEqual({ knowledgeBaseIds: [id(2)], embeddingModelConfigId: id(3), question: '结合日程提问', personalSchedules: [{ id: id(1), version: 2, projectionHash: 'A'.repeat(64) }] });
  expect(JSON.stringify(target)).not.toContain('私人字段');
});
it('会话切换和来源替换同步关闭待确认问答', async () => {
  const { wrapper, session } = await render(); session.switching = true; await flushPromises();
  expect(wrapper.find('[data-testid="frozen-answer"]').exists()).toBe(false); expect(wrapper.find('[data-testid="schedule-ask-panel"]').exists()).toBe(false);
});
it('租户即使持有权限也不能显示Host模型写入口', async () => {
  const { wrapper, session } = await render(); session.currentUser = { ...session.currentUser!, tenantId: id(8) }; await flushPromises();
  vi.mocked(api.listAiModelConfigs).mockClear(); await wrapper.setProps({ sources: [...sources] }); await flushPromises();
  expect(wrapper.find('[data-testid="schedule-ask-panel"]').exists()).toBe(false); expect(api.listAiModelConfigs).not.toHaveBeenCalled();
});
