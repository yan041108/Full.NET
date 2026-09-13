import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AiMcpRemoteConnectionsView from './AiMcpRemoteConnectionsView.vue';
import { listAiMcpRemoteConnections } from '../api/ai-mcp-remote-connections';

vi.mock('../api/ai-mcp-remote-connections', () => ({
  listAiMcpRemoteConnections: vi.fn(),
  createAiMcpRemoteConnection: vi.fn(),
  discoverAiMcpRemoteTools: vi.fn(),
  approveAiMcpRemoteTool: vi.fn()
}));

const listMock = vi.mocked(listAiMcpRemoteConnections);

function mountView() {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(AiMcpRemoteConnectionsView, { global: { plugins: [pinia] } });
}

describe('AiMcpRemoteConnectionsView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue([]);
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="ai-mcp-remote-create"]').exists()).toBe(false);
  });
});
