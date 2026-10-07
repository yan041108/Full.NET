<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput, ElOption, ElSelect } from 'element-plus';
import type { AiKnowledgeRoleCandidateResponse, AiKnowledgeOrganizationCandidateResponse } from '@fullnet/client-contracts';
import * as api from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
const props = defineProps<{ open: boolean; knowledgeBaseId: string; documentId?: string }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; saved: [] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
type Kind = 'users' | 'roles' | 'organizations';
const kind = ref<Kind>('users'); const ids = ref<string[]>([]); const userText = ref(''); const version = ref(0);
const candidates = ref<(AiKnowledgeRoleCandidateResponse | AiKnowledgeOrganizationCandidateResponse)[]>([]);
const page = ref(0); const total = ref(0); const loading = ref(false); const candidateLoading = ref(false); const saving = ref(false);
let epoch = 0; let controller: AbortController | undefined;
const prefix = computed(() => props.documentId ? 'ai.knowledge_documents' : 'ai.knowledge_bases');
const readable = computed(() => !session.switching && session.can('ai.knowledge_bases.read') && (!props.documentId || session.can('ai.knowledge_documents.read')));
const permissionKind = (value: Kind) => value === 'users' ? 'members' : value;
const allowedKinds = computed(() => (['users', 'roles', 'organizations'] as const).filter(value => readable.value && session.can(`${prefix.value}.${permissionKind(value)}_read`)));
const canSave = computed(() => allowedKinds.value.includes(kind.value) && session.can(`${prefix.value}.${permissionKind(kind.value)}_update`));
const canCandidates = computed(() => readable.value && kind.value !== 'users' && session.can(`${prefix.value}.${kind.value === 'roles' ? 'role' : 'organization'}_candidates_read`));
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const options = computed(() => [...candidates.value.map(item => ({ value: item.id, label: `${item.name} · ${item.code}` })),
  ...ids.value.filter(id => !candidates.value.some(item => item.id === id)).map(id => ({ value: id, label: id }))]);
function reset() {
  ++epoch; controller?.abort(); ids.value = []; userText.value = ''; version.value = 0; candidates.value = []; page.value = 0; total.value = 0;
  loading.value = false; candidateLoading.value = false; saving.value = false;
}
async function loadCandidates(scope = epoch) {
  if (!props.open || !canCandidates.value || !version.value || candidateLoading.value || page.value >= 10) return;
  const next = page.value + 1; const parameters = { knowledgeBaseId: props.knowledgeBaseId, page: next, pageSize: 100 }; const docId = props.documentId;
  candidateLoading.value = true;
  try {
    const result = kind.value === 'roles'
      ? await (docId ? api.listKnowledgeDocumentRoleCandidates({ ...parameters, documentId: docId }, controller?.signal) : api.listKnowledgeRoleCandidates(parameters, controller?.signal))
      : await (docId ? api.listKnowledgeDocumentOrganizationCandidates({ ...parameters, documentId: docId }, controller?.signal) : api.listKnowledgeOrganizationCandidates(parameters, controller?.signal));
    if (scope !== epoch) return;
    candidates.value = [...candidates.value, ...result.items.filter(item => !candidates.value.some(existing => existing.id === item.id))]; page.value = next; total.value = result.total;
  } catch (error) { if (scope === epoch) showProblem(error, t('common.loadFailed')); }
  finally { if (scope === epoch) candidateLoading.value = false; }
}
async function load() {
  reset(); if (!props.open || !allowedKinds.value.length) return;
  if (!allowedKinds.value.includes(kind.value)) { kind.value = allowedKinds.value[0]!; return; }
  const parameters = { knowledgeBaseId: props.knowledgeBaseId }; const docId = props.documentId; const scope = epoch;
  controller = new AbortController(); loading.value = true;
  try {
    const result = kind.value === 'users'
      ? await (docId ? api.getKnowledgeDocumentMembers({ ...parameters, documentId: docId }, controller.signal) : api.getKnowledgeMembers(parameters, controller.signal))
      : kind.value === 'roles'
        ? await (docId ? api.getKnowledgeDocumentRoles({ ...parameters, documentId: docId }, controller.signal) : api.getKnowledgeRoles(parameters, controller.signal))
        : await (docId ? api.getKnowledgeDocumentOrganizations({ ...parameters, documentId: docId }, controller.signal) : api.getKnowledgeOrganizations(parameters, controller.signal));
    if (scope !== epoch) return;
    const resourceId = 'resourceId' in result ? result.resourceId : 'documentId' in result ? result.documentId : result.knowledgeBaseId;
    if (resourceId !== (docId || props.knowledgeBaseId)) throw new Error('client.invalid_knowledge_resource');
    ids.value = 'userIds' in result ? result.userIds : 'roleIds' in result ? result.roleIds : result.organizationUnitIds;
    userText.value = ids.value.join('\n'); version.value = result.version;
    // 授权读取成功后才查询同一资源的候选；候选权限不扩大集合读取权限。
    await loadCandidates(scope);
  } catch (error) { if (scope === epoch) showProblem(error, t('common.loadFailed')); }
  finally { if (scope === epoch) loading.value = false; }
}
async function save() {
  if (!props.open || !canSave.value || !version.value || loading.value || saving.value) return;
  const selected = kind.value === 'users' ? userText.value.split(/[\s,;]+/).filter(Boolean).map(id => id.toLowerCase()) : ids.value.map(id => id.toLowerCase());
  if (selected.length > 100 || new Set(selected).size !== selected.length || selected.some(id => !guid.test(id) || id === '00000000-0000-0000-0000-000000000000')) { showWarning(t('knowledge.invalidIdentifiers')); return; }
  const parameters = { knowledgeBaseId: props.knowledgeBaseId }; const docId = props.documentId; const scope = epoch;
  const grantVersion = version.value; saving.value = true;
  try {
    if (kind.value === 'users') {
      const body = { userIds: selected, version: grantVersion };
      await (docId ? api.setKnowledgeDocumentMembers({ ...parameters, documentId: docId, body }) : api.setKnowledgeMembers({ ...parameters, body }));
    } else if (kind.value === 'roles') {
      const body = { roleIds: selected, version: grantVersion };
      await (docId ? api.setKnowledgeDocumentRoles({ ...parameters, documentId: docId, body }) : api.setKnowledgeRoles({ ...parameters, body }));
    } else {
      const body = { organizationUnitIds: selected, version: grantVersion };
      await (docId ? api.setKnowledgeDocumentOrganizations({ ...parameters, documentId: docId, body }) : api.setKnowledgeOrganizations({ ...parameters, body }));
    }
    if (scope !== epoch) return; showSuccess(t('common.saved')); emit('saved'); emit('update:open', false);
  } catch (error) { if (scope === epoch) showProblem(error, t('common.saveFailed')); }
  finally { if (scope === epoch) saving.value = false; }
}
watch(() => [props.open, props.knowledgeBaseId, props.documentId, kind.value, session.currentUser, session.switching], () => void load(), { deep: true, immediate: true });
onUnmounted(reset);
</script>
<template>
  <ArtFormDialog :open="open" :title="t('knowledge.grants')" :saving="saving" :show-confirm="canSave && version > 0 && !loading" :confirm-label="t('knowledge.save')" :cancel-label="t('common.cancel')" confirm-test-id="knowledge-grants-save" @confirm="save" @update:open="emit('update:open', $event)">
    <el-alert v-if="!allowedKinds.length" :title="t('knowledge.noPermission')" type="warning" :closable="false" />
    <template v-else>
      <p>{{ t('knowledge.grantHint') }}</p>
      <el-form label-position="top" @submit.prevent="save">
        <el-form-item :label="t('knowledge.grantKind')"><el-select v-model="kind" :disabled="saving"><el-option v-for="value in allowedKinds" :key="value" :value="value" :label="t(`knowledge.${value}`)" /></el-select></el-form-item>
        <el-form-item v-if="kind === 'users'" :label="t('knowledge.userIdentifiers')">
          <el-input v-model="userText" type="textarea" :rows="7" :disabled="!canSave || !version || loading || saving" data-testid="knowledge-user-ids" :aria-label="t('knowledge.userIdentifiers')" /><p>{{ t('knowledge.userIdentifierHint') }}</p>
        </el-form-item>
        <el-form-item v-else :label="t(`knowledge.${kind}`)"><el-select v-model="ids" multiple filterable :multiple-limit="100" :disabled="!canSave || !version || loading || saving"><el-option v-for="item in options" :key="item.value" :value="item.value" :label="item.label" /></el-select></el-form-item>
      </el-form>
      <template v-if="canCandidates && version">
        <p>{{ t('knowledge.candidateCount') }} {{ candidates.length }} / {{ total }}</p>
        <el-button v-if="page === 0 || (candidates.length < total && page < 10)" :loading="candidateLoading" :disabled="loading || saving || candidateLoading" data-testid="knowledge-candidates-more" @click="loadCandidates()">{{ t('knowledge.loadMore') }}</el-button>
        <p v-if="page >= 10 && candidates.length < total">{{ t('knowledge.candidateLimit') }}</p>
      </template>
    </template>
  </ArtFormDialog>
</template>
