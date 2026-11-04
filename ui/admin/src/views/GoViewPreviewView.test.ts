import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { routeLocationKey } from 'vue-router';
import { createShareSession } from '../test/document-share-fixtures';
import { goViewProject, goViewPreview } from '../test/goview-fixtures';
let wrapper: VueWrapper;
afterEach(() => wrapper?.unmount());
import View from './GoViewPreviewView.vue';
import { previewGoViewProject } from '../api/goview-projects';
vi.mock('../api/goview-projects', () => ({ previewGoViewProject: vi.fn() }));
describe('GoViewPreviewView', () => {
  beforeEach(() => { vi.resetAllMocks(); vi.mocked(previewGoViewProject).mockResolvedValue(goViewPreview()); });
  it('具有读取及预览权限时展示发布画布', async () => {
    const context = createShareSession(['goview.projects.read', 'goview.projects.preview']); wrapper = mount(View, { global: { plugins: [context.pinia], provide: { [routeLocationKey as symbol]: { name: 'goview-preview', params: { projectId: goViewProject.id }, query: {} } } } }); await flushPromises();
    expect(wrapper.find('[data-testid="goview-preview-stage"]').exists()).toBe(true);
  });
});
