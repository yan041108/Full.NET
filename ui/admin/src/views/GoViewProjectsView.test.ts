import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { routerKey } from 'vue-router';
import { createShareSession } from '../test/document-share-fixtures';
let wrapper: VueWrapper;
afterEach(() => wrapper?.unmount());
import View from './GoViewProjectsView.vue';
import { listGoViewProjects } from '../api/goview-projects';
vi.mock('../api/goview-projects', () => ({ listGoViewProjects: vi.fn(), createGoViewProject: vi.fn() }));
describe('GoViewProjectsView', () => {
  beforeEach(() => { vi.resetAllMocks(); vi.mocked(listGoViewProjects).mockResolvedValue([]); });
  it('只读用户可读目录但不创建写入入口', async () => {
    const context = createShareSession(['goview.projects.read']); wrapper = mount(View, { global: { plugins: [context.pinia], provide: { [routerKey as symbol]: { push: vi.fn() } } } }); await flushPromises();
    expect(listGoViewProjects).toHaveBeenCalledOnce(); expect(wrapper.find('[data-testid="goview-project-create"]').exists()).toBe(false);
  });
});
