import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElSelect } from 'element-plus';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import Dialog from './AiKnowledgePolicyDialog.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-knowledge';
import { listAiModelConfigs } from '../api/ai-model-configs';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
vi.mock('../api/ai-knowledge'); vi.mock('../api/ai-model-configs');
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
const id = '01900000-0000-7000-8000-000000000001'; const modelId = '01900000-0000-7000-8000-000000000002';
const base = { id, name: '知识库', description: null, isEnabled: true, dataClassification: 'internal', embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: modelId, generationModelVersion: 2, createdAtUtc: 'now', updatedAtUtc: null, version: 9 };
const model = { id: modelId, tenantId: null, name: '生成模型', providerKey: 'openai', maskedEndpointBaseUrl: 'https://example.invalid', modelId: 'gpt-4.1', hasApiKey: true, isDefault: false, isEnabled: true, lastTestedAtUtc: null, lastTestStatusKey: null, lastTestMessage: null, createdAtUtc: 'now', updatedAtUtc: null, version: 3 };
const dialog = { props: ['open', 'showConfirm', 'confirmTestId'], emits: ['confirm', 'update:open'], template: '<section v-if="open"><slot/><button v-if="showConfirm" :data-testid="confirmTestId" @click="$emit(\'confirm\')">save</button><button data-testid="close" @click="$emit(\'update:open\', false)">close</button></section>' };
let wrappers: VueWrapper[] = [];
async function render(allow: (code: string) => boolean = () => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const wrapper = mount(Dialog, { props: { open: true, knowledgeBaseId: id }, global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  wrappers.push(wrapper); await flushPromises(); return wrapper;
}
beforeEach(() => {
  vi.clearAllMocks(); vi.mocked(api.getKnowledgeBase).mockResolvedValue(base);
  vi.mocked(listAiModelConfigs).mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 });
  vi.mocked(api.updateKnowledgePolicy).mockResolvedValue(base);
});
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers = []; vi.restoreAllMocks(); });
it('独立策略权限缺失时不读取或显示提交', async () => {
  const wrapper = await render(code => code === 'ai.knowledge_bases.read');
  expect(api.getKnowledgeBase).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="knowledge-policy-save"]').exists()).toBe(false);
});
it('分类更新保留原审批版本，不需要模型目录读取权', async () => {
  const wrapper = await render(code => code !== 'ai.models.read');
  wrapper.findAllComponents(ElSelect)[0]!.vm.$emit('update:modelValue', 'restricted'); await flushPromises();
  await wrapper.get('[data-testid="knowledge-policy-save"]').trigger('click'); await flushPromises();
  expect(listAiModelConfigs).not.toHaveBeenCalled();
  expect(api.updateKnowledgePolicy).toHaveBeenCalledWith({ knowledgeBaseId: id, body: { version: 9, dataClassification: 'restricted', embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: modelId, generationModelVersion: 2 } });
});
it('显式撤销审批同时清除模型与版本', async () => {
  const wrapper = await render(); wrapper.findAllComponents(ElSelect)[2]!.vm.$emit('update:modelValue', ''); await flushPromises();
  await wrapper.get('[data-testid="knowledge-policy-save"]').trigger('click'); await flushPromises();
  expect(api.updateKnowledgePolicy).toHaveBeenCalledWith(expect.objectContaining({ body: expect.objectContaining({ generationModelConfigId: null, generationModelVersion: null }) }));
});
it('模型目录出现新版本不会自动替换原审批', async () => {
  vi.mocked(listAiModelConfigs).mockResolvedValue({ items: [model], page: 1, pageSize: 100, total: 1 }); const wrapper = await render();
  await wrapper.get('[data-testid="knowledge-policy-save"]').trigger('click'); await flushPromises();
  expect(listAiModelConfigs).toHaveBeenCalledWith({ page: 1, pageSize: 100, isEnabled: true }, expect.any(AbortSignal));
  expect(api.updateKnowledgePolicy).toHaveBeenCalledWith(expect.objectContaining({ body: expect.objectContaining({ generationModelConfigId: modelId, generationModelVersion: 2 }) }));
});
it('显式选中新模型版本才批准升级，伪造选项不能提交', async () => {
  vi.mocked(listAiModelConfigs).mockResolvedValue({ items: [model], page: 1, pageSize: 100, total: 1 }); const wrapper = await render();
  wrapper.findAllComponents(ElSelect)[2]!.vm.$emit('update:modelValue', `${modelId}:999`); await flushPromises();
  await wrapper.get('[data-testid="knowledge-policy-save"]').trigger('click'); expect(api.updateKnowledgePolicy).not.toHaveBeenCalled();
  wrapper.findAllComponents(ElSelect)[2]!.vm.$emit('update:modelValue', `${modelId}:3`); await flushPromises();
  await wrapper.get('[data-testid="knowledge-policy-save"]').trigger('click'); await flushPromises();
  expect(api.updateKnowledgePolicy).toHaveBeenCalledWith(expect.objectContaining({ body: expect.objectContaining({ generationModelConfigId: modelId, generationModelVersion: 3 }) }));
});
it('不匹配的知识库响应不能建立可写版本', async () => {
  vi.mocked(api.getKnowledgeBase).mockResolvedValue({ ...base, id: modelId }); const wrapper = await render();
  expect(wrapper.find('[data-testid="knowledge-policy-save"]').exists()).toBe(false); expect(listAiModelConfigs).not.toHaveBeenCalled();
});
it('读取失败后不能使用旧版本保存', async () => {
  vi.mocked(api.getKnowledgeBase).mockRejectedValue(new Error('denied')); const wrapper = await render();
  expect(wrapper.find('[data-testid="knowledge-policy-save"]').exists()).toBe(false); expect(showProblem).toHaveBeenCalled();
});
it('资源切换丢弃迟到读取', async () => {
  let finish!: (value: typeof base) => void; vi.mocked(api.getKnowledgeBase).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const wrapper = await render(); await wrapper.setProps({ open: false }); finish(base); await flushPromises();
  expect(wrapper.find('[data-testid="knowledge-policy-save"]').exists()).toBe(false);
});
it('保存防重复，409 不自动重试覆盖', async () => {
  let reject!: (reason: unknown) => void; vi.mocked(api.updateKnowledgePolicy).mockImplementation(() => new Promise((_, fail) => { reject = fail; }));
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-policy-save"]').trigger('click'); await wrapper.get('[data-testid="knowledge-policy-save"]').trigger('click');
  expect(api.updateKnowledgePolicy).toHaveBeenCalledTimes(1); reject({ status: 409 }); await flushPromises(); expect(showProblem).toHaveBeenCalled(); expect(api.getKnowledgeBase).toHaveBeenCalledTimes(1);
});
it('租户切换后的迟到写入不通知成功', async () => {
  let finish!: (value: typeof base) => void; vi.mocked(api.updateKnowledgePolicy).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  const wrapper = await render(); await wrapper.get('[data-testid="knowledge-policy-save"]').trigger('click'); useSessionStore().switching = true; await flushPromises(); finish(base); await flushPromises();
  expect(showSuccess).not.toHaveBeenCalled(); expect(wrapper.emitted('saved')).toBeUndefined();
});
