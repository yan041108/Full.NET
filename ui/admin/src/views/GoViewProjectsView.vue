<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ElAlert, ElButton, ElCard, ElForm, ElFormItem, ElInput, ElSwitch, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, GoViewProject } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { useGoViewScope } from '../composables/useGoViewScope';
import { showSuccess, showWarning } from '../feedback/fullNetMessage';
import { createGoViewProject, listGoViewProjects } from '../api/goview-projects';
import { goViewTrim, validGoViewProject } from '../guards/goview-guards';
defineOptions({ name: 'GoViewProjectsView' });
const router = useRouter(), { t } = useAdminI18n();
const items = ref<GoViewProject[]>([]), loading = ref(false), creating = ref(false), catalogReady = ref(false), recovering = ref(false), problem = ref<FullNetProblemDetails>();
const createDialogVisible = ref(false), createForm = ref({ projectKey: '', name: '', isEnabled: true }), createBlocked = ref(false), owner = ref(0), formOwner = ref(0), formWindow = ref(0);
const createReceipt = ref<{ outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed'; id?: string }>();
const floors = new Map<string, number>(), recovery = ref<{ resolve: (value: boolean) => void }>();
function finishRecovery(value: boolean): void { const pending = recovery.value; recovery.value = undefined; pending?.resolve(value); }
const context = useGoViewScope('goview-projects', reset, async () => { await load(); }), { session, canRead } = context;
type Lease = NonNullable<ReturnType<typeof context.begin>>; let listRequest: Lease | undefined, createRequest: Lease | undefined, recoveryRequest: Lease | undefined;
function resetForm(): void { createForm.value = { projectKey: '', name: '', isEnabled: true }; }
function reset(): void { listRequest = undefined; createRequest = undefined; recoveryRequest = undefined; items.value = []; loading.value = false; creating.value = false; recovering.value = false; catalogReady.value = false; problem.value = undefined; createDialogVisible.value = false; createReceipt.value = undefined; createBlocked.value = false; floors.clear(); owner.value++; formOwner.value++; finishRecovery(false); resetForm(); }
function cancelCreate(): void { if (createRequest?.current()) { createBlocked.value = true; createReceipt.value = { outcome: 'unconfirmed' }; } createRequest?.cancel(); createRequest = undefined; creating.value = false; }
watch(createDialogVisible, open => { formOwner.value++; if (!open) { cancelCreate(); finishRecovery(false); resetForm(); } }, { flush: 'sync' });
watch(createForm, () => { formOwner.value++; cancelCreate(); finishRecovery(false); }, { flush: 'sync', deep: true });
function toProblem(error: unknown): FullNetProblemDetails { return isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.request_failed', title: t('goviewProjects.loadFailed') }; }
async function load(parent?: Lease): Promise<void> {
  if (!canRead.value || parent && !parent.current() || !parent && (creating.value || recovering.value)) return;
  listRequest?.cancel(); const request = context.begin('goview.projects.read'); if (!request) return; listRequest = request; loading.value = true; catalogReady.value = false; problem.value = undefined; owner.value++;
  const cancel = () => request.cancel(); parent?.signal.addEventListener('abort', cancel, { once: true });
  try { const rows = await listGoViewProjects(undefined, request.signal); if (!request.current() || listRequest !== request) return;
    if (!rows.every(validGoViewProject) || new Set(rows.map(row => row.id.toLowerCase())).size !== rows.length || rows.some(row => row.version < (floors.get(row.id.toLowerCase()) ?? 0))) throw Error('client.invalid_goview_project_list');
    for (const row of rows) floors.set(row.id.toLowerCase(), row.version); items.value = rows; catalogReady.value = true;
  } catch (error) { if (request.current() && listRequest === request) problem.value = toProblem(error); }
  finally { parent?.signal.removeEventListener('abort', cancel); if (listRequest === request) { listRequest = undefined; loading.value = false; owner.value++; } request.finish(); }
}
function openCreate(): void { if (!canRead.value || !session.can('goview.projects.create') || creating.value || loading.value || recovering.value) return; resetForm(); formWindow.value++; createDialogVisible.value = true; }
async function submitCreate(): Promise<void> {
  if (!canRead.value || !createDialogVisible.value || createBlocked.value || creating.value || loading.value || recovering.value) return;
  const body = { ...createForm.value }; if (!goViewTrim(body.projectKey) || !goViewTrim(body.name)) { showWarning(t('goviewProjects.createFailed')); return; }
  const request = context.begin('goview.projects.create'); if (!request) return; const window = formWindow.value; let saved = false; createRequest = request; creating.value = true; formOwner.value++; createReceipt.value = { outcome: 'processing' };
  try { const created = await createGoViewProject(body, request.signal); if (!request.current() || createRequest !== request) return;
    if (!validGoViewProject(created) || created.version !== 1 || created.latestPublishedVersionNumber !== 0 || created.projectKey !== goViewTrim(body.projectKey) || created.name !== goViewTrim(body.name) || created.isEnabled !== body.isEnabled || items.value.some(row => row.id.toLowerCase() === created.id.toLowerCase())) throw Error('client.invalid_goview_project');
    saved = true; createRequest = undefined; creating.value = false; createReceipt.value = { outcome: 'succeeded', id: created.id }; floors.set(created.id.toLowerCase(), created.version); createDialogVisible.value = false; showSuccess(t('goviewProjects.createSuccess'));
    await load(request); if (request.current() && session.can('goview.projects.update')) await router.push({ name: 'goview-editor', params: { projectId: created.id } });
  } catch (error) { if (request.current() && formWindow.value === window) { if (!saved && createRequest === request) { const unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408; createBlocked.value = unknown; createReceipt.value = { outcome: unknown ? 'unconfirmed' : 'failed' }; } problem.value = toProblem(error); } }
  finally { if (createRequest === request) { createRequest = undefined; creating.value = false; formOwner.value++; } request.finish(); }
}
function current(row: GoViewProject): boolean { return items.value.some(value => value.id.toLowerCase() === row.id.toLowerCase() && value.version === row.version && value.projectKey === row.projectKey && value.latestPublishedVersionNumber === row.latestPublishedVersionNumber && value.isEnabled === row.isEnabled); }
async function navigate(row: GoViewProject, preview: boolean): Promise<void> {
  if (!canRead.value || !catalogReady.value || loading.value || creating.value || recovering.value || !current(row) || preview && (!row.isEnabled || row.latestPublishedVersionNumber < 1)) return;
  const request = context.begin(preview ? 'goview.projects.preview' : 'goview.projects.update'); if (!request) return;
  try { await router.push({ name: preview ? 'goview-preview' : 'goview-editor', params: { projectId: row.id }, ...(preview ? { query: { versionNumber: String(row.latestPublishedVersionNumber) } } : {}) }); } catch (error) { if (request.current()) problem.value = toProblem(error); } finally { request.finish(); }
}
async function openEditor(row: GoViewProject): Promise<void> { await navigate({ ...row }, false); }
async function openPreview(row: GoViewProject): Promise<void> { await navigate({ ...row }, true); }
async function recoverCreate(): Promise<void> {
  if (!canRead.value || !createBlocked.value || !createDialogVisible.value || creating.value || loading.value || recovering.value) return;
  const request = context.begin('goview.projects.create'); if (!request) return; const ticket = formOwner.value; recoveryRequest = request; recovering.value = true;
  try { const confirmed = await new Promise<boolean>(resolve => { recovery.value = { resolve }; }); if (confirmed && request.current() && ticket === formOwner.value && createDialogVisible.value) createBlocked.value = false; }
  finally { if (recoveryRequest === request) { recoveryRequest = undefined; recovering.value = false; formOwner.value++; } request.finish(); }
}
const recoveryHandlers = computed(() => { const pending = recovery.value, finish = (value: boolean) => { if (pending && recovery.value === pending) finishRecovery(value); }; return { confirm: () => finish(true), update: (value: boolean) => { if (!value) finish(false); } }; });
const controls = computed(() => { const ticket = owner.value, owned = () => canRead.value && ticket === owner.value; return { create: () => { if (owned()) openCreate(); }, refresh: () => { if (owned()) void load(); } }; });
const rowControls = computed(() => { const ticket = owner.value; return new Map(items.value.map(row => { const value = { ...row }, owned = () => canRead.value && ticket === owner.value && current(value); return [row.id.toLowerCase(), { edit: () => { if (owned()) void openEditor(value); }, preview: () => { if (owned()) void openPreview(value); } }]; })); });
const createControls = computed(() => { const ticket = formOwner.value, window = formWindow.value, owned = () => canRead.value && createDialogVisible.value && ticket === formOwner.value && window === formWindow.value;
  const input = (key: keyof typeof createForm.value, value: string | boolean) => { if (owned() && !creating.value && !recovering.value) { if (key === 'isEnabled') createForm.value.isEnabled = Boolean(value); else createForm.value[key] = String(value); } };
  return { update: (open: boolean) => { if (!open && owned()) createDialogVisible.value = false; }, submit: () => { if (owned()) void submitCreate(); }, key: (value: string) => input('projectKey', value), name: (value: string) => input('name', value), enabled: (value: boolean) => input('isEnabled', value), recover: () => { if (owned()) void recoverCreate(); } }; });
</script>

<template>
  <div v-if="canRead" class="goview-projects-view">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('goviewProjects.title') }}</h1>
    <ArtTableHeader :loading="loading" v-on="{ refresh: controls.refresh }">
      <template #left>
        <PermissionGate code="goview.projects.create">
          <ElButton
            data-testid="goview-project-create"
            type="primary"
            :icon="Plus"
            :disabled="loading || creating || recovering" v-on="{ click: controls.create }"
          >
            {{ t('goviewProjects.addProject') }}
          </ElButton>
        </PermissionGate>
      </template>
    </ArtTableHeader>

    <ElAlert
      v-if="problem"
      type="error"
      :title="problem.title"
      show-icon
      class="mb-4"
    />

    <p v-if="createReceipt" role="status" data-testid="goview-create-receipt">{{ t(createReceipt.outcome === 'succeeded' ? 'goviewProjects.createSuccess' : createReceipt.outcome === 'unconfirmed' ? 'goviewCommon.unconfirmed' : createReceipt.outcome === 'processing' ? 'goviewCommon.processing' : 'goviewProjects.createFailed') }} <code v-if="createReceipt.id" translate="no">{{ createReceipt.id }}</code></p>
    <ElCard shadow="never">
      <ElTable v-loading="loading" :data="items" row-key="id">
        <ElTableColumn prop="name" :label="t('goviewProjects.fieldName')" min-width="160" />
        <ElTableColumn prop="projectKey" :label="t('goviewProjects.fieldProjectKey')" min-width="160" />
        <ElTableColumn :label="t('goviewProjects.fieldPublishedVersion')" width="140">
          <template #default="{ row }">
            <ElTag :type="row.latestPublishedVersionNumber > 0 ? 'success' : 'info'">
              v{{ row.latestPublishedVersionNumber }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('goviewProjects.fieldEnabled')" width="100">
          <template #default="{ row }">
            <ElTag :type="row.isEnabled ? 'success' : 'info'">
              {{ row.isEnabled ? t('goviewProjects.enabledYes') : t('goviewProjects.enabledNo') }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('goviewProjects.actions')" width="220" fixed="right">
          <template #default="{ row }">
            <ArtTableActionGroup>
              <PermissionGate code="goview.projects.update">
                <ArtTableActionButton type="edit"
                  test-id="goview-project-edit"
                  :disabled="!catalogReady || loading || creating || recovering" v-on="{ click: rowControls.get(row.id.toLowerCase())?.edit }"
                >
                  {{ t('goviewProjects.editCanvas') }}
                </ArtTableActionButton>
              </PermissionGate>
              <PermissionGate code="goview.projects.preview">
                <ArtTableActionButton type="delete"
                  test-id="goview-project-preview"
                  :disabled="!catalogReady || loading || creating || recovering || !row.isEnabled || row.latestPublishedVersionNumber <= 0"
                  v-on="{ click: rowControls.get(row.id.toLowerCase())?.preview }"
                >
                  {{ t('goviewProjects.preview') }}
                </ArtTableActionButton>
              </PermissionGate>
            </ArtTableActionGroup>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <ArtFormDialog
      :key="formWindow" :open="createDialogVisible" v-on="{ 'update:open': createControls.update, confirm: createControls.submit }"
      :title="t('goviewProjects.createTitle')"
      :saving="creating" :show-confirm="!createBlocked && !recovering"
    >
      <ElAlert v-if="createBlocked" type="warning" :title="t('goviewCommon.recoveryHint')" :closable="false" />
      <ElButton v-if="createBlocked" :disabled="creating || recovering" data-testid="goview-create-recover" v-on="{ click: createControls.recover }">{{ t('goviewCommon.recoveryTitle') }}</ElButton>
      <ElForm label-width="120px" :disabled="creating || recovering">
        <ElFormItem :label="t('goviewProjects.fieldProjectKey')" required>
          <ElInput :model-value="createForm.projectKey" v-on="{ 'update:modelValue': createControls.key }" data-testid="goview-project-key-input" />
        </ElFormItem>
        <ElFormItem :label="t('goviewProjects.fieldName')" required>
          <ElInput :model-value="createForm.name" v-on="{ 'update:modelValue': createControls.name }" data-testid="goview-project-name-input" />
        </ElFormItem>
        <ElFormItem :label="t('goviewProjects.fieldEnabled')">
          <ElSwitch :model-value="createForm.isEnabled" v-on="{ 'update:modelValue': createControls.enabled }" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>
    <ArtFormDialog v-if="recovery" :open="true" :title="t('goviewCommon.recoveryTitle')" :confirm-label="t('users.confirm')" :cancel-label="t('users.cancel')" v-on="{ confirm: recoveryHandlers.confirm, 'update:open': recoveryHandlers.update }"><p>{{ t('goviewCommon.recoveryHint') }}</p></ArtFormDialog>
  </div>
</template>
