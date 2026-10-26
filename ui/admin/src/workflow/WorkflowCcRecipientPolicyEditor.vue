<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue';
import { ElAlert, ElButton, ElInput, ElInputNumber, ElOption, ElPagination, ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listWorkflowRecipientCandidates, listWorkflowRoleCandidates, listWorkflowOrganizationUnitCandidates, listWorkflowPositionCandidates } from '../api/workflow-definitions';
import { readCcRecipientPolicy } from './workflow-vue3-adapter';
const props = defineProps<{ modelValue: { sources: Record<string, unknown>[] }; fields: readonly WorkflowFormField[]; previousNodes: readonly { key: string; label: string }[]; disabled: boolean }>();
const emit = defineEmits<{ save: [policy: { sources: Record<string, unknown>[] }]; cancel: [] }>();
const { t } = useAdminI18n(); const session = useSessionStore(); const sources = ref(JSON.parse(JSON.stringify(props.modelValue.sources)) as Record<string, unknown>[]);
const message = ref(''); const closed = ref(false); const tenant = computed(() => session.currentUser?.actorScope === 'tenant');
const kinds = [['specified_users', 'specifiedUsers'], ['role_members', 'roleMembers'], ['initiator', 'initiator'], ['flow_administrators', 'flowAdministrators'],
  ['organization_unit_leader', 'unitLeader'], ['organization_unit_members', 'unitMembers'], ['position_members', 'positionMembers'], ['initiator_primary_unit_leader', 'primaryUnitLeader'],
  ['initiator_direct_manager', 'directManager'], ['initiator_ancestor_unit_leader', 'initiatorAncestorUnitLeader'], ['initiator_leader_chain', 'leaderChain'],
  ['form_users', 'formUsers'], ['form_units', 'formUnits'], ['previous_node_actors', 'previousActors'], ['starter_selected_users', 'starterSelected']] as const;
const tenantKinds = new Set(['organization_unit_leader', 'organization_unit_members', 'position_members', 'initiator_primary_unit_leader', 'initiator_direct_manager', 'initiator_ancestor_unit_leader', 'initiator_leader_chain', 'form_units']);
type Category = 'users' | 'roles' | 'units' | 'positions';
const pages = reactive<Record<Category, { items: { id: string; name: string }[]; page: number; total: number; loading: boolean }>>({
  users: { items: [], page: 1, total: 0, loading: false }, roles: { items: [], page: 1, total: 0, loading: false },
  units: { items: [], page: 1, total: 0, loading: false }, positions: { items: [], page: 1, total: 0, loading: false } });
let epoch = 0; const controllers = new Map<Category, AbortController>();
const identity = computed(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId, ...(session.currentUser?.permissions ?? [])].join('|'));
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);
const selection = (source: Record<string, unknown>) => object(source.selectionPolicy) ? source.selectionPolicy : {};
const range = (source: Record<string, unknown>) => object(selection(source).range) ? selection(source).range as Record<string, unknown> : {};
function category(source: Record<string, unknown>): Category | undefined {
  const kind = source.resolverKindKey;
  return kind === 'specified_users' || kind === 'starter_selected_users' && range(source).kindKey === 'specified_users' ? 'users'
    : kind === 'role_members' || kind === 'starter_selected_users' && range(source).kindKey === 'role_members' ? 'roles'
    : kind === 'organization_unit_leader' || kind === 'organization_unit_members' ? 'units' : kind === 'position_members' ? 'positions' : undefined;
}
function cancel(): void { epoch++; for (const controller of controllers.values()) controller.abort(); controllers.clear(); }
function close(): void { if (closed.value) return; closed.value = true; cancel(); sources.value = []; for (const page of Object.values(pages)) page.items = []; emit('cancel'); }
async function load(kind: Category, page = 1): Promise<void> {
  if (closed.value || props.disabled || pages[kind].loading) return;
  const ticket = epoch; controllers.get(kind)?.abort(); const controller = new AbortController(); controllers.set(kind, controller); pages[kind].loading = true;
  try {
    const result = kind === 'users' ? await listWorkflowRecipientCandidates(page, 20, controller.signal)
      : kind === 'roles' ? await listWorkflowRoleCandidates(page, 20, controller.signal)
      : kind === 'units' ? await listWorkflowOrganizationUnitCandidates(page, 20, controller.signal) : await listWorkflowPositionCandidates(page, 20, controller.signal);
    if (ticket !== epoch) return;
    pages[kind].items = result.items.map(person => ({ id: person.id, name: 'displayName' in person ? person.displayName || person.username : person.name }));
    pages[kind].page = result.page; pages[kind].total = result.total;
  } catch { if (ticket === epoch) message.value = t('workflowDesigner.assignee.candidatesUnavailable'); }
  finally { if (ticket === epoch) pages[kind].loading = false; }
}
function changeKind(index: number, kind: string): void {
  const value: Record<string, unknown> = { resolverKindKey: kind };
  if (kind === 'specified_users') value.userIds = []; else if (kind === 'role_members') value.roleIds = []; else if (kind === 'position_members') value.positionIds = [];
  else if (kind === 'organization_unit_leader' || kind === 'organization_unit_members') value.unitId = '';
  else if (kind === 'initiator_ancestor_unit_leader') value.ancestorLevel = 1; else if (kind === 'initiator_leader_chain') value.maximumLevels = 3;
  else if (kind === 'form_users' || kind === 'form_units') value.fieldKey = ''; else if (kind === 'previous_node_actors') value.nodeKey = '';
  else if (kind === 'starter_selected_users') value.selectionPolicy = { selectionKey: '', modeKey: 'single', maximumUsers: 1, range: { kindKey: 'all' } };
  sources.value[index] = value; const target = category(value); if (target) void load(target);
}
function ids(source: Record<string, unknown>): string[] {
  const key = category(source) === 'users' ? 'userIds' : category(source) === 'roles' ? 'roleIds' : 'positionIds';
  const value = source.resolverKindKey === 'starter_selected_users' ? range(source)[key] : source[key];
  return Array.isArray(value) ? value.filter((id): id is string => typeof id === 'string') : [];
}
function selected(source: Record<string, unknown>): string[] | string { return category(source) === 'units' ? String(source.unitId ?? '') : ids(source); }
function choose(index: number, value: unknown): void {
  const source = sources.value[index]!; const target = category(source);
  if (target === 'units') sources.value[index] = { ...source, unitId: value };
  else {
    const key = target === 'users' ? 'userIds' : target === 'roles' ? 'roleIds' : 'positionIds';
    sources.value[index] = source.resolverKindKey === 'starter_selected_users' ? { ...source, selectionPolicy: { ...selection(source), range: { ...range(source), [key]: value } } } : { ...source, [key]: value };
  }
}
function editSelection(index: number, key: string, value: unknown): void { const source = sources.value[index]!; const next = { ...selection(source), [key]: value };
  if (key === 'modeKey') next.maximumUsers = value === 'single' ? 1 : 20; sources.value[index] = { ...source, selectionPolicy: next }; }
function editRange(index: number, kind: string): void { const source = sources.value[index]!; sources.value[index] = { ...source, selectionPolicy: { ...selection(source), range: { kindKey: kind, ...(kind === 'specified_users' ? { userIds: [] } : kind === 'role_members' ? { roleIds: [] } : {}) } } }; const target = category(sources.value[index]!); if (target) void load(target); }
function save(): void {
  if (closed.value || props.disabled) return;
  try {
    const policy = readCcRecipientPolicy({ sources: sources.value });
    if (policy.sources.some(source => tenantKinds.has(String(source.resolverKindKey)) && !tenant.value || source.resolverKindKey === 'previous_node_actors' && !props.previousNodes.some(node => node.key === source.nodeKey) ||
      ['form_users', 'form_units'].includes(String(source.resolverKindKey)) && !props.fields.some(field => field.fieldKey === source.fieldKey))) throw new Error();
    emit('save', policy);
  } catch { message.value = t('workflowDesigner.cc.invalid'); }
}
watch(identity, close, { flush: 'sync' });
for (const source of sources.value) { const target = category(source); if (target) void load(target); }
onBeforeUnmount(cancel);
</script>
<template>
  <section v-if="!closed" class="cc-policy" data-testid="workflow-cc-policy-editor">
    <p>{{ t('workflowDesigner.cc.help') }}</p><ElAlert v-if="message" :title="message" type="error" :closable="false" />
    <fieldset v-for="(source, index) in sources" :key="index">
      <legend>{{ t('workflowDesigner.cc.source', { number: index + 1 }) }}</legend>
      <ElSelect :model-value="String(source.resolverKindKey)" :disabled="disabled" :aria-label="t('workflowDesigner.assignee.sources')" @update:model-value="changeKind(index, String($event))">
        <ElOption v-for="[kind, label] in kinds" :key="kind" :value="kind" :label="t(`workflowDesigner.assignee.${label}`)" :disabled="tenantKinds.has(kind) && !tenant" />
      </ElSelect>
      <template v-if="source.resolverKindKey === 'starter_selected_users'">
        <ElInput :model-value="String(selection(source).selectionKey ?? '')" :disabled="disabled" :aria-label="t('workflowDesigner.assignee.starterSelected')" @update:model-value="editSelection(index, 'selectionKey', $event)" />
        <ElSelect :model-value="String(selection(source).modeKey)" :disabled="disabled" @update:model-value="editSelection(index, 'modeKey', $event)"><ElOption value="single" :label="t('workflowDesigner.cc.single')" /><ElOption value="multiple" :label="t('workflowDesigner.cc.multiple')" /></ElSelect>
        <ElInputNumber :model-value="Number(selection(source).maximumUsers)" :min="1" :max="20" :disabled="disabled || selection(source).modeKey === 'single'" @update:model-value="editSelection(index, 'maximumUsers', $event)" />
        <ElSelect :model-value="String(range(source).kindKey)" :disabled="disabled" @update:model-value="editRange(index, String($event))"><ElOption v-for="kind in ['all', 'specified_users', 'role_members']" :key="kind" :value="kind" :label="t(`workflowDesigner.cc.range.${kind}` as Parameters<typeof t>[0])" /></ElSelect>
      </template>
      <template v-if="category(source)">
        <ElSelect :model-value="selected(source)" :multiple="category(source) !== 'units'" :multiple-limit="category(source) === 'users' ? 20 : 5" filterable :disabled="disabled || pages[category(source)!].loading" :aria-label="t('workflowDesigner.handling.members')" @update:model-value="choose(index, $event)">
          <ElOption v-for="id in (category(source) === 'units' ? [String(source.unitId ?? '')] : ids(source)).filter(id => id && !pages[category(source)!].items.some(person => person.id === id))" :key="id" :value="id" :label="id" />
          <ElOption v-for="person in pages[category(source)!].items" :key="person.id" :value="person.id" :label="person.name" />
        </ElSelect>
        <ElPagination v-if="pages[category(source)!].total > 20" :current-page="pages[category(source)!].page" :page-size="20" :total="pages[category(source)!].total" :disabled="disabled || pages[category(source)!].loading" layout="prev, pager, next" @current-change="load(category(source)!, $event)" />
      </template>
      <ElInputNumber v-if="source.resolverKindKey === 'initiator_ancestor_unit_leader' || source.resolverKindKey === 'initiator_leader_chain'" :model-value="Number(source.ancestorLevel ?? source.maximumLevels)" :min="1" :max="20" :disabled="disabled" @update:model-value="sources[index] = { ...source, [source.resolverKindKey === 'initiator_leader_chain' ? 'maximumLevels' : 'ancestorLevel']: $event }" />
      <ElSelect v-if="source.resolverKindKey === 'form_users' || source.resolverKindKey === 'form_units'" :model-value="String(source.fieldKey ?? '')" :disabled="disabled" :aria-label="t('workflowDesigner.assignee.formUsers')" @update:model-value="sources[index] = { ...source, fieldKey: $event }"><ElOption v-for="field in fields" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" /></ElSelect>
      <ElSelect v-if="source.resolverKindKey === 'previous_node_actors'" :model-value="String(source.nodeKey ?? '')" :disabled="disabled" :aria-label="t('workflowDesigner.assignee.previousActors')" @update:model-value="sources[index] = { ...source, nodeKey: $event }"><ElOption v-for="node in previousNodes" :key="node.key" :value="node.key" :label="node.label" /></ElSelect>
      <ElButton :disabled="disabled || sources.length <= 1" @click="sources.splice(index, 1)">{{ t('workflowDesigner.assignee.removeSource') }}</ElButton>
    </fieldset>
    <ElButton :disabled="disabled || sources.length >= 8" @click="sources.push({ resolverKindKey: 'initiator' })">{{ t('workflowDesigner.assignee.addSource') }}</ElButton>
    <ElButton :disabled="disabled" data-testid="workflow-cc-policy-save" type="primary" @click="save">{{ t('workflowDesigner.cc.save') }}</ElButton>
  </section>
</template>
<style scoped>
.cc-policy, .cc-policy fieldset { display: grid; gap: .75rem; }
.cc-policy p { margin: 0; line-height: 1.6; }
.cc-policy fieldset { border: 1px solid var(--el-border-color); padding: .75rem; min-width: 0; }
</style>
