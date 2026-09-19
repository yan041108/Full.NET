import { afterEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { listTenantMembers, createTenantInvitation } from './tenant-members';
import { listTenantSubscriptions } from './tenant-subscriptions';
import { getEntitlementEnforcementPhase } from './tenancy-entitlements';
import { registerAccount, confirmRecoverPassword } from './public-auth';

vi.mock('./http', () => ({ request: vi.fn(), apiBaseUrl: '' }));
afterEach(() => { vi.clearAllMocks(); vi.unstubAllGlobals(); });

describe('基础模块成功响应契约', () => {
  it('拒绝缺少权限阶段字段的成功响应', async () => {
    vi.mocked(request).mockResolvedValue({});
    await expect(getEntitlementEnforcementPhase()).rejects.toThrow('client.invalid_entitlement_enforcement');
  });
  it('拒绝成员分页中畸形的行', async () => {
    vi.mocked(request).mockResolvedValue({ items: [{}], page: 1, pageSize: 20, total: 1 });
    await expect(listTenantMembers()).rejects.toThrow('client.invalid_tenant_member_page');
  });
  it('拒绝不含邀请凭据的成功响应', async () => {
    vi.mocked(request).mockResolvedValue({ invitation: {} });
    await expect(createTenantInvitation({ targetEmail: 'a@example.com', memberRole: 'Member' }))
      .rejects.toThrow('client.invalid_tenant_invitation_result');
  });
  it('拒绝订阅列表中畸形的行', async () => {
    vi.mocked(request).mockResolvedValue([{}]);
    await expect(listTenantSubscriptions('tenant')).rejects.toThrow('client.invalid_tenant_subscription_list');
  });
  it('匿名注册也必须校验成功响应', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 200 })));
    await expect(registerAccount({ email: 'a@example.com', displayName: 'A', password: 'test', challengeId: 'id', challengeCode: '123' }))
      .rejects.toThrow('client.invalid_register_account_response');
  });
  it('恢复密码接受无响应体的 204', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    await expect(confirmRecoverPassword({ challengeId: 'id', challengeCode: '123', newPassword: 'test' })).resolves.toBeUndefined();
  });
});
