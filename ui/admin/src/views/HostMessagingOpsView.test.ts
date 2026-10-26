import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, h, inject, provide, reactive, toRef, type Ref } from 'vue';
import HostMessagingOpsView from './HostMessagingOpsView.vue';
import { listOperationalDeadLetters, recoverOperationalDeadLetter } from '../api/messaging-operations';
import { getMessagingDeliveryOverview, listMessagingReplayOperations } from '../api/messaging-delivery';
import { ElMessageBox } from 'element-plus';

const permissions = reactive(new Set<string>());
vi.mock('../auth/permission', () => ({ usePermission: () => ({ can: (code: string) => permissions.has(code) }) }));
vi.mock('../i18n/adminI18n', () => ({ useAdminI18n: () => ({ t: (key: string) => key }) }));
vi.mock('../api/messaging-operations', () => ({ listOperationalDeadLetters: vi.fn(), getOperationalDeadLetter: vi.fn(), recoverOperationalDeadLetter: vi.fn() }));
vi.mock('../api/messaging-delivery', () => ({ getMessagingDeliveryOverview: vi.fn(), listMessagingReplayOperations: vi.fn(), getMessagingReplayOperation: vi.fn(),
  createMessagingReplayOperation: vi.fn(), executeMessagingReplayOperation: vi.fn(), cancelMessagingReplayOperation: vi.fn(),
  verifyMessagingCutoverEvidence: vi.fn(), changeMessagingDeliveryOwner: vi.fn(), getLatestMessagingRollback: vi.fn(), recoverMessagingRollback: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showError: vi.fn(), showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn() }));
vi.mock('element-plus', async (original) => ({ ...await original<object>(), ElMessageBox: { confirm: vi.fn().mockResolvedValue('confirm') } }));

const row = {
  id: '01929d50-8640-7000-8000-000000000001', source: 'legacy' as const, consumerName: null,
  messageId: '01929d50-8640-7000-8000-000000000001', messageType: 'fullnet.test.failed',
  schemaVersion: 1, tenantId: null, attempts: 3, errorCode: 'outbox.max_attempts_exceeded',
  firstFailedAtUtc: '2026-10-24T00:00:00Z', lastFailedAtUtc: '2026-10-24T00:00:00Z',
  sourceTopic: null, sourcePartition: null, sourceOffset: null, dlqTopic: null, dlqPartition: null,
  dlqOffset: null, canReplay: true, replayedAtUtc: null
};

const Table = defineComponent({ props: ['data'], setup(props, { slots }) {
  provide('rows', toRef(props, 'data')); return () => h('div', slots.default?.());
} });
const Column = defineComponent({ props: ['prop'], setup(props, { slots }) {
  const rows = inject<Ref<Record<string, unknown>[]>>('rows');
  return () => h('div', rows?.value?.map(item => h('div', slots.default ? slots.default({ row: item }) : String(item[props.prop] ?? ''))));
} });
const Dialog = defineComponent({ name: 'ArtFormDialog', props: ['open', 'showConfirm'], emits: ['confirm', 'update:open'],
  setup(props, { slots, emit }) { return () => props.open ? h('div', [slots.default?.(), props.showConfirm === false ? null
    : h('button', { 'data-recover-confirm': '', onClick: () => emit('confirm') }, 'confirm')]) : null; }
});

function render() {
  return mount(HostMessagingOpsView, { global: { stubs: { ElTable: Table, ElTableColumn: Column,
    ArtFormDialog: Dialog, ElTabs: { name: 'ElTabs', template: '<div><slot /></div>' }, ElTabPane: true }, directives: { loading: () => {} } } });
}

describe('Host messaging dead-letter operations', () => {
  beforeEach(() => {
    vi.clearAllMocks(); permissions.clear(); permissions.add('messaging.operations.read');
    vi.mocked(listOperationalDeadLetters).mockResolvedValue({ items: [row], page: 1, pageSize: 20, total: 1 });
    vi.mocked(recoverOperationalDeadLetter).mockResolvedValue('requeued');
    vi.mocked(getMessagingDeliveryOverview).mockResolvedValue({ backlog: null, streams: [], observationState: 'partial', hasMoreStreams: false,
      unattributedKafkaDeadLetters: null, cutoverEnabled: false, replayEnabled: false, replayMaximumSynchronousMessages: 100,
      replayExecutionTimeoutSeconds: 30, controlAdapterConfigured: false, shadowCapturePolicy: { enabled: false, eventType: null, schemaVersion: null, maximumEvents: null, maximumPayloadBytes: null, maximumWindowSeconds: null }, targetRuntimeState: 'not_observed' });
    vi.mocked(listMessagingReplayOperations).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  });

  it('does not fetch or expose content without read permission', async () => {
    const wrapper = render(); await flushPromises();
    expect(listOperationalDeadLetters).not.toHaveBeenCalled(); expect(wrapper.text()).not.toContain(row.messageId);
    wrapper.unmount();
  });

  it('defaults to Legacy and independently guards the recovery action', async () => {
    permissions.add('messaging.dead_letters.read');
    const wrapper = render(); await flushPromises();
    expect(listOperationalDeadLetters).toHaveBeenCalledWith('legacy', expect.objectContaining({ page: 1 }), expect.any(AbortSignal));
    expect(wrapper.text()).toContain(row.messageId);
    expect(wrapper.find('[title="messagingOps.requeue"]').exists()).toBe(false);
    wrapper.unmount();
  });

  it('requires a reason and confirms an authorized single-message recovery', async () => {
    permissions.add('messaging.dead_letters.read'); permissions.add('messaging.outbox_dead_letters.requeue');
    const wrapper = render(); await flushPromises();
    await wrapper.get('[title="messagingOps.requeue"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-recover-confirm]').trigger('click'); await flushPromises();
    expect(recoverOperationalDeadLetter).not.toHaveBeenCalled();
    await wrapper.get('textarea').setValue('repaired dependency');
    await wrapper.get('[data-recover-confirm]').trigger('click'); await flushPromises();
    expect(recoverOperationalDeadLetter).toHaveBeenCalledWith('legacy', row.id, 'repaired dependency');
    wrapper.unmount();
  });

  it('switches to Kafka without retaining stale Legacy results', async () => {
    permissions.add('messaging.dead_letters.read');
    const wrapper = render(); await flushPromises();
    vi.mocked(listOperationalDeadLetters).mockImplementationOnce(() => new Promise(() => {}));
    const tabs = wrapper.findComponent({ name: 'ElTabs' });
    tabs.vm.$emit('update:modelValue', 'kafka'); tabs.vm.$emit('tab-change', 'kafka'); await flushPromises();
    expect(listOperationalDeadLetters).toHaveBeenLastCalledWith('kafka', expect.anything(), expect.any(AbortSignal));
    expect(wrapper.text()).not.toContain(row.messageId);
    wrapper.unmount();
  });

  it('requires page entry as well as area permission before reading any source', async () => {
    permissions.clear(); permissions.add('messaging.dead_letters.read'); permissions.add('messaging.events.read');
    const wrapper = render(); await flushPromises();
    expect(listOperationalDeadLetters).not.toHaveBeenCalled(); expect(getMessagingDeliveryOverview).not.toHaveBeenCalled();
    expect(wrapper.text()).not.toContain(row.messageId); wrapper.unmount();
  });

  it('defaults event readers to overview and renders unknown counters distinctly', async () => {
    permissions.add('messaging.events.read');
    const wrapper = render(); await flushPromises();
    expect(getMessagingDeliveryOverview).toHaveBeenCalledOnce(); expect(listOperationalDeadLetters).not.toHaveBeenCalled();
    expect(wrapper.text()).toContain('messagingOps.unknown'); expect(wrapper.text()).toContain('messagingOps.notObserved');
    expect(wrapper.text()).toContain('messagingOps.partialObservation'); wrapper.unmount();
  });

  it('read-only operators can open records without seeing create or execution controls', async () => {
    permissions.add('messaging.events.read');
    const wrapper = render(); await flushPromises();
    wrapper.findComponent({ name: 'ElTabs' }).vm.$emit('update:modelValue', 'operations'); await flushPromises();
    expect(listMessagingReplayOperations).toHaveBeenCalledOnce(); expect(wrapper.text()).not.toContain('messagingOps.createReplay');
    expect(listOperationalDeadLetters).not.toHaveBeenCalled(); wrapper.unmount();
  });

  it('revoking page permission aborts reads and prevents a late response restoring protected content', async () => {
    permissions.add('messaging.dead_letters.read');
    let resolve!: (value: { items: typeof row[]; page: number; pageSize: number; total: number }) => void;
    vi.mocked(listOperationalDeadLetters).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = render(); await flushPromises();
    const signal = vi.mocked(listOperationalDeadLetters).mock.calls[0]![2]!;
    permissions.delete('messaging.operations.read'); await flushPromises();
    expect(signal.aborted).toBe(true);
    resolve({ items: [row], page: 1, pageSize: 20, total: 1 }); await flushPromises();
    expect(wrapper.text()).not.toContain(row.messageId); wrapper.unmount();
  });

  it('rechecks authorization after confirmation before performing recovery', async () => {
    permissions.add('messaging.dead_letters.read'); permissions.add('messaging.outbox_dead_letters.requeue');
    let accept!: () => void;
    vi.mocked(ElMessageBox.confirm).mockImplementationOnce(() => new Promise(resolve => {
      accept = () => resolve(Object.assign('confirm' as const, { value: '', action: 'confirm' as const }));
    }));
    const wrapper = render(); await flushPromises();
    await wrapper.get('[title="messagingOps.requeue"]').trigger('click'); await flushPromises();
    await wrapper.get('textarea').setValue('verified repair'); await wrapper.get('[data-recover-confirm]').trigger('click'); await flushPromises();
    permissions.delete('messaging.operations.read'); await flushPromises(); accept(); await flushPromises();
    expect(recoverOperationalDeadLetter).not.toHaveBeenCalled(); wrapper.unmount();
  });
});
