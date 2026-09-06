import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import GoViewEditorView from './GoViewEditorView.vue';
import { getGoViewProject } from '../api/goview-projects';

vi.mock('../api/goview-projects', () => ({
  getGoViewProject: vi.fn(),
  updateGoViewProject: vi.fn(),
  publishGoViewProject: vi.fn()
}));

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { projectId: '00000000-0000-4000-8000-000000000001' } }),
  useRouter: () => ({ push: vi.fn() })
}));

const getMock = vi.mocked(getGoViewProject);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(GoViewEditorView, { global: { plugins: [pinia] } });
}

describe('GoViewEditorView', () => {
  beforeEach(() => {
    getMock.mockResolvedValue({
      id: '00000000-0000-4000-8000-000000000001',
      projectKey: 'demo',
      name: 'Demo',
      canvasJson: '{"width":1920,"height":1080,"backgroundColor":"#0a1628","components":[]}',
      latestPublishedVersionNumber: 0,
      isEnabled: true,
      createdAtUtc: '2026-01-01T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    });
  });

  it('hides save action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(wrapper.find('[data-testid="goview-editor-save"]').exists()).toBe(false);
  });
});
