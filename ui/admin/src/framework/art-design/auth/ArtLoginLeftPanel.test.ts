import { afterEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { downloadCurrentTenantBrandingLogoContent } from '@fullnet/client-contracts';
import ArtLoginLeftPanel from './ArtLoginLeftPanel.vue';

vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(),
  getRuntimeTenantBranding: vi.fn().mockResolvedValue({ hasLogo: true, systemTitle: 'Full.NET' }),
  downloadCurrentTenantBrandingLogoContent: vi.fn()
}));

afterEach(() => vi.restoreAllMocks());

describe('登录页品牌图片生命周期', () => {
  it('卸载后完成的下载不得留下 Blob URL', async () => {
    const pending = Promise.withResolvers<Blob>();
    vi.mocked(downloadCurrentTenantBrandingLogoContent).mockReturnValue(pending.promise);
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:late');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const wrapper = mount(ArtLoginLeftPanel);
    await flushPromises();
    wrapper.unmount();
    pending.resolve(new Blob(['logo']));
    await flushPromises();
    expect(create.mock.calls.length - revoke.mock.calls.length).toBe(0);
  });
});
