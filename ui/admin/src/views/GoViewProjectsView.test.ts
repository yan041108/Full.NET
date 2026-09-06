import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import GoViewProjectsView from './GoViewProjectsView.vue';
import { listGoViewProjects } from '../api/goview-projects';

vi.mock('../api/goview-projects', () => ({
  listGoViewProjects: vi.fn(),
  createGoViewProject: vi.fn()
}));

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() })
}));

const listMock = vi.mocked(listGoViewProjects);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(GoViewProjectsView, { global: { plugins: [pinia] } });
}

describe('GoViewProjectsView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue([]);
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="goview-project-create"]').exists()).toBe(false);
  });
});
