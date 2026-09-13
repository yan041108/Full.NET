import { afterEach, expect, it, vi } from 'vitest';
import { flushPromises, shallowMount } from '@vue/test-utils';
import type { TenantBrandingResponse } from '@fullnet/client-contracts';
import TenantsView from './TenantsView.vue';
import { fetchTenantBrandingLogoBlob, uploadTenantBrandingLogo } from '../api/tenant-branding';

vi.mock('../auth/session', () => ({ useSessionStore: () => ({ can: () => true }) }));
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock('../api/tenants', async importOriginal => ({
  ...await importOriginal<typeof import('../api/tenants')>(),
  listHostTenants: vi.fn().mockResolvedValue({ items: [], total: 0 })
}));
vi.mock('../api/tenant-packages', () => ({ listHostTenantPackages: vi.fn().mockResolvedValue({ items: [] }) }));
vi.mock('../api/tenant-branding', async importOriginal => ({
  ...await importOriginal<typeof import('../api/tenant-branding')>(),
  uploadTenantBrandingLogo: vi.fn(),
  fetchTenantBrandingLogoBlob: vi.fn().mockResolvedValue(new Blob(['logo']))
}));
afterEach(() => { vi.restoreAllMocks(); vi.clearAllMocks(); });

it.each(['close', 'switch', 'reopen'])('上传过程中 %s 后不启动旧租户预览', async mode => {
  const upload = Promise.withResolvers<TenantBrandingResponse>();
  vi.mocked(uploadTenantBrandingLogo).mockReturnValue(upload.promise);
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:late');
  vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
  const wrapper = shallowMount(TenantsView, { global: { directives: { loading: () => undefined } } });
  await flushPromises();
  const vm = wrapper.vm as unknown as {
    brandingTenant: { id: string }; brandingVisible: boolean; brandingVersion: number;
    handleBrandingLogoSelected(event: Event): Promise<void>;
  };
  vm.brandingTenant = { id: 'tenant-a' };
  vm.brandingVisible = true;
  vm.brandingVersion = 1;
  const input = document.createElement('input');
  Object.defineProperty(input, 'files', { value: [new File(['logo'], 'logo.png')] });
  const pending = vm.handleBrandingLogoSelected({ target: input } as unknown as Event);
  vm.brandingVisible = false;
  if (mode !== 'close') {
    vm.brandingTenant = { id: mode === 'switch' ? 'tenant-b' : 'tenant-a' };
    vm.brandingVisible = true;
  }
  upload.resolve({ tenantId: 'tenant-a', logoFileId: 'logo-a', version: 2,
    systemTitle: null, contactPhone: null, contactEmail: null, contactAddress: null, copyright: null });
  await pending;
  expect(fetchTenantBrandingLogoBlob).not.toHaveBeenCalled();
  expect(vm.brandingVersion).toBe(1);
  wrapper.unmount();
});
