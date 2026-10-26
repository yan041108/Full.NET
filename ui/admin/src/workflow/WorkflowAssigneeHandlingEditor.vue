<script setup lang="ts">
import { ElButton, ElInputNumber, ElOption, ElSelect } from 'element-plus';
import { useAdminI18n } from '../i18n/adminI18n';
import type { WorkflowAssigneeHandling, WorkflowAssigneeHandlingAction } from './workflow-vue3-adapter';

const props = defineProps<{ modelValue?: WorkflowAssigneeHandling;
  users: readonly { id: string; displayName: string; username: string }[];
  roles: readonly { id: string; name: string }[]; units: readonly { id: string; name: string }[];
  positions: readonly { id: string; name: string }[]; vote?: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [value: WorkflowAssigneeHandling | undefined] }>();
const { t } = useAdminI18n();
type Trigger = 'whenEmpty' | 'whenInitiator';
const triggers = ['whenEmpty', 'whenInitiator'] as const;
const defaults = { whenEmpty: 'error', whenInitiator: 'keep' };
const kinds = [
  ['specified_users', 'specifiedUsers'], ['role_members', 'roleMembers'], ['flow_administrators', 'flowAdministrators'], ['organization_unit_leader', 'unitLeader'],
  ['initiator_primary_unit_leader', 'primaryUnitLeader'], ['initiator_direct_manager', 'directManager'], ['initiator_ancestor_unit_leader', 'initiatorAncestorUnitLeader'],
  ['position_members', 'positionMembers'], ['organization_unit_members', 'unitMembers'], ['initiator_leader_chain', 'leaderChain'], ['initiator', 'initiator']
] as const;
const sourceKeys: Record<string, string> = { specified_users: 'userIds', role_members: 'roleIds', position_members: 'positionIds' };
const actions = (trigger: Trigger) => trigger === 'whenEmpty' ? ['error', 'suspend', 'auto_pass', 'auto_reject', 'assign'] : ['keep', 'skip', 'assign'];
const action = (trigger: Trigger): WorkflowAssigneeHandlingAction => props.modelValue?.[trigger] ?? { actionKey: defaults[trigger] };
function edit(trigger: Trigger, update: (value: WorkflowAssigneeHandlingAction) => void): void {
  const value = JSON.parse(JSON.stringify(props.modelValue ?? {})) as WorkflowAssigneeHandling;
  value[trigger] ??= { actionKey: defaults[trigger] }; update(value[trigger]!);
  emit('update:modelValue', triggers.every(key => value[key] === undefined || value[key]?.actionKey === defaults[key]) ? undefined : value);
}
function setAction(trigger: Trigger, key: string): void {
  edit(trigger, value => { value.actionKey = key;
    if (key === 'assign') value.fallbackPolicies ??= [{ sources: [{ resolverKindKey: 'specified_users', userIds: [] }] }];
    else delete value.fallbackPolicies;
  });
}
function setSource(trigger: Trigger, attempt: number, index: number, source: Record<string, unknown>): void {
  edit(trigger, value => { value.fallbackPolicies![attempt]!.sources[index] = source; });
}
function changeKind(trigger: Trigger, attempt: number, index: number, kind: string): void {
  const source: Record<string, unknown> = { resolverKindKey: kind };
  if (sourceKeys[kind]) source[sourceKeys[kind]!] = [];
  else if (kind === 'organization_unit_leader' || kind === 'organization_unit_members') source.unitId = '';
  else if (kind === 'initiator_ancestor_unit_leader') source.ancestorLevel = 1;
  else if (kind === 'initiator_leader_chain') source.maximumLevels = 3;
  setSource(trigger, attempt, index, source);
}
function options(kind: unknown) {
  return kind === 'specified_users' ? props.users.map(user => ({ id: user.id, name: user.displayName || user.username }))
    : kind === 'role_members' ? props.roles : props.positions;
}
function selectedIds(source: Record<string, unknown>): string[] {
  const value = source[sourceKeys[String(source.resolverKindKey)]!];
  return Array.isArray(value) ? value.filter((id): id is string => typeof id === 'string') : [];
}
</script>

<template>
  <section class="handling" data-testid="workflow-assignee-handling" :aria-label="t('workflowDesigner.handling.title')">
    <h3>{{ t('workflowDesigner.handling.title') }}</h3>
    <p>{{ t('workflowDesigner.handling.help') }}</p>
    <p v-if="vote" role="note">{{ t('workflowDesigner.handling.voteHint') }}</p>
    <fieldset v-for="trigger in triggers" :key="trigger">
      <legend>{{ t(`workflowDesigner.handling.${trigger}`) }}</legend>
      <ElSelect :model-value="action(trigger).actionKey" :data-testid="`handling-${trigger}`" :aria-label="t(`workflowDesigner.handling.${trigger}`)"
        @update:model-value="setAction(trigger, String($event))">
        <ElOption v-for="key in actions(trigger)" :key="key" :value="key" :label="t(`workflowDesigner.handling.${key}` as Parameters<typeof t>[0])" />
      </ElSelect>
      <article v-for="(fallback, attempt) in action(trigger).fallbackPolicies ?? []" :key="attempt" class="handling__fallback">
        <h4>{{ t('workflowDesigner.handling.attempt', { number: attempt + 1 }) }}</h4>
        <div v-for="(source, index) in fallback.sources" :key="index" class="handling__source">
          <ElSelect :model-value="String(source.resolverKindKey)" :aria-label="t('workflowDesigner.assignee.sources')"
            @update:model-value="changeKind(trigger, attempt, index, String($event))">
            <ElOption v-for="[kind, label] in kinds" :key="kind" :value="kind" :label="t(`workflowDesigner.assignee.${label}`)" />
          </ElSelect>
          <ElSelect v-if="sourceKeys[String(source.resolverKindKey)]" :model-value="selectedIds(source)"
            multiple filterable :multiple-limit="source.resolverKindKey === 'specified_users' ? 20 : 5" :aria-label="t('workflowDesigner.handling.members')"
            @update:model-value="setSource(trigger, attempt, index, { ...source, [sourceKeys[String(source.resolverKindKey)]!]: $event })">
            <ElOption v-for="option in options(source.resolverKindKey)" :key="option.id" :value="option.id" :label="option.name" />
          </ElSelect>
          <ElSelect v-if="source.resolverKindKey === 'organization_unit_leader' || source.resolverKindKey === 'organization_unit_members'"
            :model-value="String(source.unitId ?? '')" filterable :aria-label="t('workflowDesigner.handling.unit')"
            @update:model-value="setSource(trigger, attempt, index, { ...source, unitId: $event })">
            <ElOption v-for="unit in units" :key="unit.id" :value="unit.id" :label="unit.name" />
          </ElSelect>
          <ElInputNumber v-if="source.resolverKindKey === 'initiator_ancestor_unit_leader' || source.resolverKindKey === 'initiator_leader_chain'"
            :model-value="Number(source.ancestorLevel ?? source.maximumLevels)" :min="1" :max="20" :aria-label="t('workflowDesigner.assignee.ancestorLevel')"
            @update:model-value="setSource(trigger, attempt, index, { ...source, [source.resolverKindKey === 'initiator_leader_chain' ? 'maximumLevels' : 'ancestorLevel']: $event })" />
          <ElButton :disabled="fallback.sources.length <= 1" @click="edit(trigger, value => value.fallbackPolicies![attempt]!.sources.splice(index, 1))">{{ t('workflowDesigner.assignee.removeSource') }}</ElButton>
        </div>
        <ElButton :disabled="fallback.sources.length >= 8" @click="edit(trigger, value => value.fallbackPolicies![attempt]!.sources.push({ resolverKindKey: 'specified_users', userIds: [] }))">{{ t('workflowDesigner.assignee.addSource') }}</ElButton>
        <ElButton :disabled="(action(trigger).fallbackPolicies?.length ?? 0) <= 1" @click="edit(trigger, value => value.fallbackPolicies!.splice(attempt, 1))">{{ t('workflowDesigner.handling.removeAttempt') }}</ElButton>
      </article>
      <ElButton v-if="action(trigger).actionKey === 'assign'" :disabled="(action(trigger).fallbackPolicies?.length ?? 0) >= 3"
        :data-testid="`handling-add-${trigger}`" @click="edit(trigger, value => value.fallbackPolicies!.push({ sources: [{ resolverKindKey: 'specified_users', userIds: [] }] }))">{{ t('workflowDesigner.handling.addAttempt') }}</ElButton>
    </fieldset>
  </section>
</template>

<style scoped>
.handling { display: grid; gap: .75rem; padding: 1rem; border: 1px solid var(--el-border-color); border-radius: 8px; }
.handling h3, .handling h4, .handling p { margin: 0; }
.handling fieldset { display: grid; gap: .75rem; min-width: 0; padding: .75rem; border: 1px solid var(--el-border-color-lighter); }
.handling__fallback, .handling__source { display: grid; gap: .5rem; }
.handling__fallback { padding-top: .75rem; border-top: 1px solid var(--el-border-color-lighter); }
</style>
