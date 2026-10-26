import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, h, reactive } from 'vue';
import { ElMessageBox } from 'element-plus';
import Dialog from './HostMessagingShadowCaptureDialog.vue';
import { getMessagingShadowCapture, startMessagingShadowCapture, closeMessagingShadowCapture, type MessagingDeliveryStream } from '../api/messaging-delivery';
const permissions = reactive(new Set<string>());
vi.mock('../auth/permission', () => ({ usePermission: () => ({ can: (value: string) => permissions.has(value) }) }));
vi.mock('../i18n/adminI18n', () => ({ useAdminI18n: () => ({ t: (key: string) => key }) }));
vi.mock('../feedback/fullNetMessage', () => ({ showError: vi.fn(), showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
vi.mock('../api/messaging-delivery', () => ({ getMessagingShadowCapture: vi.fn(), startMessagingShadowCapture: vi.fn(), closeMessagingShadowCapture: vi.fn() }));
vi.mock('element-plus', async original => ({ ...await original<object>(), ElMessageBox: { confirm: vi.fn().mockResolvedValue('confirm') } }));
const stream = { eventType: 'fullnet.test', schemaVersion: 1, deliveryOwner: 0, blockingReasons: [] } as unknown as MessagingDeliveryStream;
const policy = { enabled: true, eventType: stream.eventType, schemaVersion: 1, maximumEvents: 10, maximumPayloadBytes: 100, maximumWindowSeconds: 60 };
const closed = { generation: '01929d50-8640-7000-8000-000000000001', eventType: stream.eventType, schemaVersion: 1,
  state: 'closed' as const, maxEvents: 10, maxPayloadBytes: 100, capturedCount: 1, capturedBytes: 2, missingMetadataCount: 0,
  startedAtUtc: '2026-10-25T00:00:00Z', expiresAtUtc: '2026-10-25T00:01:00Z', closedAtUtc: '2026-10-25T00:01:00Z', endReason: 'message_budget' };
function render() { return mount(Dialog, { props: { stream, policy }, global: { stubs: { ArtFormDialog: defineComponent({ setup(_, { slots }) { return () => h('div', slots.default?.()); } }) } } }); }
const button = (wrapper: ReturnType<typeof render>, key: string) => wrapper.findAll('button').find(item => item.text() === key)!;
describe('Shadow capture control permissions and retry', () => {
  beforeEach(() => { vi.clearAllMocks(); permissions.clear(); permissions.add('messaging.operations.read'); permissions.add('messaging.events.read'); vi.mocked(getMessagingShadowCapture).mockResolvedValue(closed); });
  it('allows readers to inspect without mutation controls', async () => {
    const wrapper = render(); await flushPromises(); expect(wrapper.text()).toContain(closed.generation);
    expect(wrapper.text()).not.toContain('messagingOps.capture.start'); wrapper.unmount();
  });
  it('rechecks write permission after confirmation and never sends revoked actions', async () => {
    permissions.add('messaging.delivery.cutover'); const wrapper = render(); await flushPromises();
    await wrapper.find('textarea').setValue('controlled capture');
    vi.mocked(ElMessageBox.confirm).mockImplementationOnce(async () => { permissions.delete('messaging.delivery.cutover'); return undefined as never; });
    await button(wrapper, 'messagingOps.capture.start').trigger('click'); await flushPromises();
    expect(startMessagingShadowCapture).not.toHaveBeenCalled(); expect(closeMessagingShadowCapture).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('keeps the same request key on an uncertain creation retry', async () => {
    permissions.add('messaging.delivery.cutover'); const wrapper = render(); await flushPromises();
    await wrapper.find('textarea').setValue('controlled capture'); vi.mocked(startMessagingShadowCapture).mockRejectedValue(new Error('response lost'));
    await button(wrapper, 'messagingOps.capture.start').trigger('click'); await flushPromises();
    await button(wrapper, 'messagingOps.capture.start').trigger('click'); await flushPromises();
    expect(startMessagingShadowCapture).toHaveBeenCalledTimes(2);
    expect(vi.mocked(startMessagingShadowCapture).mock.calls[0]![0]).toEqual(vi.mocked(startMessagingShadowCapture).mock.calls[1]![0]); wrapper.unmount();
  });
});
