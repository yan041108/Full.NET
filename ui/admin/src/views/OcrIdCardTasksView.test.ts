import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import OcrIdCardTasksView from './OcrIdCardTasksView.vue';
import { listOcrIdCardTasks } from '../api/ocr';

vi.mock('../api/ocr', () => ({
  listOcrIdCardTasks: vi.fn(),
  createOcrIdCardTask: vi.fn(),
  confirmOcrIdCardTask: vi.fn(),
  rejectOcrIdCardTask: vi.fn()
}));

vi.mock('../api/host-files', () => ({
  uploadHostFile: vi.fn()
}));

const listMock = vi.mocked(listOcrIdCardTasks);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(OcrIdCardTasksView, { global: { plugins: [pinia] } });
}

describe('OcrIdCardTasksView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  });

  it('hides upload action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="ocr-id-card-upload"]').exists()).toBe(false);
  });
});
