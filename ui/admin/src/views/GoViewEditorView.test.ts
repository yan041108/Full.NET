import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { routeLocationKey, routerKey } from 'vue-router';
import { createShareSession } from '../test/document-share-fixtures';
import { goViewProject } from '../test/goview-fixtures';
let wrapper: VueWrapper;
afterEach(() => wrapper?.unmount());
import View from './GoViewEditorView.vue';
import { getGoViewProject } from '../api/goview-projects';
vi.mock('../api/goview-projects', () => ({ getGoViewProject: vi.fn(), updateGoViewProject: vi.fn(), publishGoViewProject: vi.fn() }));
describe('GoViewEditorView', () => {
  beforeEach(() => { vi.resetAllMocks(); vi.mocked(getGoViewProject).mockResolvedValue(goViewProject); });
  it('只读用户可以读取草稿，但不创建保存入口', async () => {
    const context = createShareSession(['goview.projects.read']); wrapper = mount(View, { global: { plugins: [context.pinia], provide: { [routerKey as symbol]: { push: vi.fn() }, [routeLocationKey as symbol]: { name: 'goview-editor', params: { projectId: goViewProject.id }, query: {} } } } }); await flushPromises();
    expect(getGoViewProject).toHaveBeenCalledOnce(); expect(wrapper.find('[data-testid="goview-editor-save"]').exists()).toBe(false);
  });
});
