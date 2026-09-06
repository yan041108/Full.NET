import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import GoViewPreviewView from './GoViewPreviewView.vue';
import { previewGoViewProject } from '../api/goview-projects';

vi.mock('../api/goview-projects', () => ({
  previewGoViewProject: vi.fn()
}));

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { projectId: '00000000-0000-4000-8000-000000000001' } })
}));

const previewMock = vi.mocked(previewGoViewProject);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(GoViewPreviewView, { global: { plugins: [pinia] } });
}

describe('GoViewPreviewView', () => {
  beforeEach(() => {
    previewMock.mockResolvedValue({
      projectId: '00000000-0000-4000-8000-000000000001',
      projectKey: 'demo',
      projectName: 'Demo',
      versionNumber: 1,
      canvasJson: '{"width":1920,"height":1080,"backgroundColor":"#0a1628","components":[{"type":"text","x":10,"y":10,"width":120,"height":40,"props":{"text":"Hello"}}]}',
      generatedAtUtc: '2026-01-01T00:00:00Z'
    });
  });

  it('renders published canvas stage', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(wrapper.find('[data-testid="goview-preview-stage"]').exists()).toBe(true);
  });
});
