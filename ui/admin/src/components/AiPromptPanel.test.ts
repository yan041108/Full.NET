import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { ElMessageBox, type MessageBoxData } from 'element-plus';
import Panel from './AiPromptPanel.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-prompts';
vi.mock('../api/ai-prompts');
const head = { id: 'catalog', name: 'Catalog', revision: 3, lastVersion: 2, activeVersionId: 'active', epoch: 0 };
const draft = { id: 'draft', promptId: 'catalog', version: 2, body: { modelConfigId: 'model', languageTag: 'zh-CN', template: '<img src=x onerror=alert(1)> {{input}}', inputTokenLimit: 100, outputTokenLimit: 100 }, bodyHash: 'a'.repeat(64), wasPublished: false, modelConfigVersion: 1, providerKey: 'ollama', modelId: 'model', createdAtUtc: 'now', definitionKey: 'fullnet-single-text-v1', inputSchemaKey: 'ai.prompt.text.v1', outputSchemaKey: 'ai.prompt.text.v1', allowedToolNames: [], retrievalStrategyKey: 'none' };
const active = { ...draft, id: 'active', version: 1, wasPublished: true };
const wrappers: VueWrapper[] = [];
function render(allow = (_code: string) => true) {
  const pinia = createPinia(); setActivePinia(pinia); vi.spyOn(useSessionStore(), 'can').mockImplementation(allow);
  const wrapper = mount(Panel, { props: { modelId: 'model' }, global: { plugins: [pinia] } }); wrappers.push(wrapper); return wrapper;
}
async function choose(wrapper: VueWrapper) {
  await wrapper.get('[data-testid="prompt-refresh"]').trigger('click'); await flushPromises();
  await wrapper.get('[data-testid="prompt-catalog"]').setValue('catalog'); await flushPromises();
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listPrompts).mockResolvedValue({ items: [head], page: 1, pageSize: 100, total: 1 });
  vi.mocked(api.getPromptVersions).mockResolvedValue({ items: [active, draft], page: 1, pageSize: 100, total: 2 });
  vi.mocked(api.publishPrompt).mockResolvedValue(head);
  vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm' as MessageBoxData);
});
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks(); });
it('缺少读取权限时不读取或展示目录', () => {
  const w = render(code => code !== 'ai.prompts.read'); expect(w.find('[data-testid="prompt-panel"]').exists()).toBe(false); expect(api.listPrompts).not.toHaveBeenCalled();
});
it('发布经过确认并发送精确修订及版本；正文只显示文本', async () => {
  const w = render(); await choose(w); expect(w.find('img').exists()).toBe(false); expect(w.text()).toContain('<img');
  await w.get('[data-testid="prompt-publish"]').trigger('click'); await flushPromises();
  expect(ElMessageBox.confirm).toHaveBeenCalled();
  expect(api.publishPrompt).toHaveBeenCalledWith({ promptId: 'catalog', body: { revision: 3, versionId: 'draft' } }, expect.any(AbortSignal));
});
it('缺少回滚权限时不能选择曾发布旧版本作为发布', async () => {
  vi.mocked(api.getPromptVersions).mockResolvedValue({ items: [active, { ...active, id: 'older' }], page: 1, pageSize: 100, total: 2 });
  const w = render(code => code !== 'ai.prompts.rollback'); await choose(w);
  expect(w.find('[data-testid="prompt-rollback"]').exists()).toBe(false); expect(w.find('[data-testid="prompt-publish"]').exists()).toBe(false);
});
it('只使用当前发布版本；携带完整精确模型和预算策略', async () => {
  const w = render(); await choose(w); await w.get('[data-testid="prompt-use"]').trigger('click');
  expect(w.emitted('select')!.at(-1)).toEqual([active]);
});
it('租户切换中晚到正文不重新显示', async () => {
  let resolve!: (value: Awaited<ReturnType<typeof api.getPromptVersions>>) => void;
  vi.mocked(api.getPromptVersions).mockReturnValue(new Promise(r => { resolve = r; }));
  const w = render(); await w.get('[data-testid="prompt-refresh"]').trigger('click'); await flushPromises();
  await w.get('[data-testid="prompt-catalog"]').setValue('catalog'); await flushPromises();
  useSessionStore().currentContextName = 'Tenant B'; resolve({ items: [draft], page: 1, pageSize: 100, total: 1 }); await flushPromises();
  expect(w.find('[data-testid="prompt-version"]').exists()).toBe(false); expect(w.emitted('select')!.at(-1)).toEqual([undefined]);
});
it('未知创建结果保留原键和载荷重试', async () => {
  vi.mocked(api.createPrompt).mockRejectedValue(new Error('timeout'));
  const w = render(); await w.get('[data-testid="prompt-name"]').setValue('Catalog');
  await w.get('[data-testid="prompt-create"]').trigger('click'); await flushPromises();
  await w.get('[data-testid="prompt-create"]').trigger('click'); await flushPromises();
  expect(api.createPrompt).toHaveBeenCalledTimes(2);
  expect(vi.mocked(api.createPrompt).mock.calls[0]![0]).toEqual(vi.mocked(api.createPrompt).mock.calls[1]![0]);
});
it('追加发生修订冲突后先刷新再写入', async () => {
  vi.mocked(api.appendPrompt).mockRejectedValue({ status: 409 });
  const w = render(); await choose(w); await w.get('[data-testid="prompt-append"]').trigger('click'); await flushPromises();
  expect(w.get('[data-testid="prompt-append"]').attributes('disabled')).toBeDefined();
  await w.get('[data-testid="prompt-refresh"]').trigger('click'); await flushPromises();
  expect(w.get('[data-testid="prompt-append"]').attributes('disabled')).toBeUndefined();
});
it('确认框随上下文失效关闭，确认晚到也不能发布', async () => {
  let resolve!: (value: MessageBoxData) => void;
  vi.mocked(ElMessageBox.confirm).mockReturnValue(new Promise(r => { resolve = r; }));
  const close = vi.spyOn(ElMessageBox, 'close').mockImplementation(() => undefined);
  const w = render(); await choose(w); await w.get('[data-testid="prompt-publish"]').trigger('click'); await flushPromises();
  useSessionStore().currentContextName = 'Tenant B'; expect(close).toHaveBeenCalledTimes(1);
  resolve('confirm' as MessageBoxData); await flushPromises(); expect(api.publishPrompt).not.toHaveBeenCalled();
});
it('旧确认晚到不能清除新上下文确认框的所有权', async () => {
  let first!: (value: MessageBoxData) => void; let second!: (value: MessageBoxData) => void;
  vi.mocked(ElMessageBox.confirm).mockReturnValueOnce(new Promise(r => { first = r; })).mockReturnValueOnce(new Promise(r => { second = r; }));
  const close = vi.spyOn(ElMessageBox, 'close').mockImplementation(() => undefined);
  const w = render(); await choose(w); await w.get('[data-testid="prompt-publish"]').trigger('click'); await flushPromises();
  useSessionStore().currentContextName = 'Tenant B'; await flushPromises(); await choose(w); await w.get('[data-testid="prompt-publish"]').trigger('click'); await flushPromises();
  first('confirm' as MessageBoxData); await flushPromises();
  useSessionStore().currentContextName = 'Tenant C'; expect(close).toHaveBeenCalledTimes(2);
  second('confirm' as MessageBoxData); await flushPromises(); expect(api.publishPrompt).not.toHaveBeenCalled();
});
