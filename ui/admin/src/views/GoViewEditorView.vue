<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ElAlert, ElButton, ElCard, ElForm, ElFormItem, ElInput, ElSwitch } from 'element-plus';
import type { FullNetProblemDetails, GoViewProject, UpdateGoViewProjectRequest } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { useGoViewScope } from '../composables/useGoViewScope';
import { showSuccess, showWarning } from '../feedback/fullNetMessage';
import { getGoViewProject, publishGoViewProject, updateGoViewProject } from '../api/goview-projects';
import { DEFAULT_GOVIEW_CANVAS, goViewTrim, validGoViewCanvas, validGoViewId, validGoViewProject, validGoViewPublishedVersion, validGoViewUpdate, validGoViewWriteVersion } from '../api/goview-guards';
defineOptions({ name: 'GoViewEditorView' });
const router = useRouter(), { t } = useAdminI18n();
const project = ref<GoViewProject>(), loading = ref(false), saving = ref(false), publishing = ref(false), ready = ref(false), recovering = ref(false), problem = ref<FullNetProblemDetails>();
const editorForm = ref({ name: '', canvasJson: DEFAULT_GOVIEW_CANVAS, isEnabled: true, version: 0 }), owner = ref(0);
type Receipt = { outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed'; projectId: string; version: number; versionNumber?: number };
const saveReceipt = ref<Receipt>(), publicationReceipt = ref<Receipt>(), block = ref<{ kind: 'save' | 'publish'; version: number }>();
const recovery = ref<{ resolve: (value: boolean) => void }>();
function finishRecovery(value: boolean): void { const pending = recovery.value; recovery.value = undefined; pending?.resolve(value); }
const context = useGoViewScope('goview-editor', reset, async () => { await load(); }), { session, route, canRead } = context;
type Lease = NonNullable<ReturnType<typeof context.begin>>;
type Write = { request: Lease; child?: Lease; target: GoViewProject; phase: 'save' | 'publish'; sent: boolean };
let readRequest: Lease | undefined, write: Write | undefined, recoveryRequest: Lease | undefined, applying = false, known: GoViewProject | undefined;
const projectId = computed(() => typeof route?.params.projectId === 'string' ? route.params.projectId : '');
function apply(value: GoViewProject, preserve = false): void { project.value = { ...value }; if (!preserve) { applying = true; try { editorForm.value = { name: value.name, canvasJson: value.canvasJson, isEnabled: value.isEnabled, version: value.version }; } finally { applying = false; } } owner.value++; }
function reset(): void { write = undefined; readRequest = undefined; recoveryRequest = undefined; loading.value = false; saving.value = false; publishing.value = false; ready.value = false; recovering.value = false; project.value = undefined; known = undefined; saveReceipt.value = undefined; publicationReceipt.value = undefined; block.value = undefined; problem.value = undefined; finishRecovery(false); applying = true; editorForm.value = { name: '', canvasJson: DEFAULT_GOVIEW_CANVAS, isEnabled: true, version: 0 }; applying = false; owner.value++; }
function markUnknown(value: Write): void { block.value = { kind: value.phase, version: value.target.version }; ready.value = false; const receipt: Receipt = { outcome: 'unconfirmed', projectId: value.target.id, version: value.target.version }; if (value.phase === 'save') saveReceipt.value = receipt; else publicationReceipt.value = receipt; }
function cancelWrite(): void { const current = write; if (current?.sent && current.request.current()) markUnknown(current); current?.child?.cancel(); current?.request.cancel(); write = undefined; saving.value = false; publishing.value = false; }
watch(editorForm, () => { if (!applying) { owner.value++; finishRecovery(false); cancelWrite(); } }, { flush: 'sync', deep: true });
function same(left: GoViewProject, right: GoViewProject): boolean { return left.id.toLowerCase() === right.id.toLowerCase() && left.projectKey === right.projectKey && left.version === right.version && left.name === right.name && left.canvasJson === right.canvasJson && left.isEnabled === right.isEnabled && left.latestPublishedVersionNumber === right.latestPublishedVersionNumber; }
function toProblem(error: unknown, key: 'goviewEditor.loadFailed' | 'goviewEditor.saveFailed' | 'goviewEditor.publishFailed' = 'goviewEditor.loadFailed'): FullNetProblemDetails { return isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.request_failed', title: t(key) }; }
async function load(): Promise<void> {
  if (!canRead.value || saving.value || publishing.value || recovering.value) return;
  readRequest?.cancel(); const request = context.begin('goview.projects.read'); if (!request) return; readRequest = request; loading.value = true; ready.value = false; problem.value = undefined; owner.value++; const id = projectId.value;
  try {
    if (!validGoViewId(id)) throw Error('client.invalid_goview_project'); const value = await getGoViewProject(id, request.signal); if (!request.current() || readRequest !== request || projectId.value !== id) return;
    if (!validGoViewProject(value) || value.id.toLowerCase() !== id.toLowerCase() || known && (value.version < known.version || value.latestPublishedVersionNumber < known.latestPublishedVersionNumber || value.version === known.version && !same(value, known))) throw Error('client.invalid_goview_project');
    const preserve = !!project.value && same(value, project.value); known = { ...value }; apply(value, preserve); ready.value = true;
    if (block.value && value.version > block.value.version) block.value = undefined;
  } catch (error) { if (request.current() && readRequest === request) problem.value = toProblem(error); }
  finally { if (readRequest === request) { readRequest = undefined; loading.value = false; owner.value++; } request.finish(); }
}
const dirty = computed(() => !!project.value && (goViewTrim(editorForm.value.name) !== project.value.name || goViewTrim(editorForm.value.canvasJson) !== project.value.canvasJson || editorForm.value.isEnabled !== project.value.isEnabled));
const busy = computed(() => loading.value || saving.value || publishing.value || recovering.value);
function writable(): boolean { return canRead.value && ready.value && !!project.value && !busy.value && !block.value && editorForm.value.version === project.value.version && validGoViewWriteVersion(project.value.version); }
const componentCount = computed(() => { try { const value: unknown = JSON.parse(editorForm.value.canvasJson); return value && typeof value === 'object' && !Array.isArray(value) && Array.isArray((value as Record<string, unknown>).components) ? ((value as Record<string, unknown>).components as unknown[]).length : 0; } catch { return 0; } });
async function execute(publishAfter: boolean): Promise<void> {
  if (!writable() || !project.value) return;
  const input = { ...editorForm.value }, target = { ...project.value }, needsSave = !publishAfter || dirty.value;
  if (!goViewTrim(input.name) || !validGoViewCanvas(input.canvasJson)) { showWarning(t('goviewEditor.saveFailed')); return; }
  if (needsSave && !session.can('goview.projects.update')) { showWarning(t('goviewCommon.unsaved')); return; }
  if (publishAfter && (!target.isEnabled || !input.isEnabled || target.latestPublishedVersionNumber >= 2147483647)) return;
  const request = context.begin(publishAfter ? 'goview.projects.publish' : 'goview.projects.update'); if (!request) return;
  const current: Write = { request, target, phase: needsSave ? 'save' : 'publish', sent: false }; write = current; saving.value = needsSave; publishing.value = publishAfter; owner.value++; problem.value = undefined;
  try {
    if (needsSave) {
      const body: UpdateGoViewProjectRequest = { ...input, version: target.version }; let signal = request.signal;
      if (publishAfter) { const child = context.begin('goview.projects.update'); if (!child) return; current.child = child; signal = child.signal; }
      current.sent = true; saveReceipt.value = { outcome: 'processing', projectId: target.id, version: target.version };
      const value = await updateGoViewProject(target.id, body, signal); if (!request.current() || write !== current || current.child && !current.child.current()) return;
      if (!validGoViewUpdate(value, target.id, body) || value.projectKey !== target.projectKey || value.latestPublishedVersionNumber !== target.latestPublishedVersionNumber) throw Error('client.invalid_goview_project');
      current.child?.finish(); current.child = undefined; current.sent = false; current.target = { ...value }; known = { ...value }; apply(value); saveReceipt.value = { outcome: 'succeeded', projectId: value.id, version: value.version }; saving.value = false;
      if (!publishAfter) { showSuccess(t('goviewEditor.saveSuccess')); return; }
    }
    if (!request.current() || write !== current || !validGoViewWriteVersion(current.target.version)) return;
    current.phase = 'publish'; current.sent = true; publicationReceipt.value = { outcome: 'processing', projectId: current.target.id, version: current.target.version };
    const value = await publishGoViewProject(current.target.id, { version: current.target.version }, request.signal); if (!request.current() || write !== current) return;
    if (!validGoViewPublishedVersion(value) || value.projectId.toLowerCase() !== current.target.id.toLowerCase() || value.canvasJson !== current.target.canvasJson || value.versionNumber !== current.target.latestPublishedVersionNumber + 1 || value.changeNote !== null || value.publishedByUserId.toLowerCase() !== session.currentUser?.id.toLowerCase()) throw Error('client.invalid_goview_project_version');
    current.sent = false; const published = { ...current.target, version: current.target.version + 1, latestPublishedVersionNumber: value.versionNumber, updatedAtUtc: value.publishedAtUtc }; known = { ...published }; apply(published); publicationReceipt.value = { outcome: 'succeeded', projectId: published.id, version: published.version, versionNumber: value.versionNumber }; showSuccess(t('goviewEditor.publishSuccess'));
  } catch (error) {
    if (request.current() && write === current) {
      const unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408;
      if (unknown && current.sent) markUnknown(current); else { ready.value = false; const receipt: Receipt = { outcome: 'failed', projectId: current.target.id, version: current.target.version }; if (current.phase === 'save') saveReceipt.value = receipt; else publicationReceipt.value = receipt; }
      problem.value = toProblem(error, current.phase === 'save' ? 'goviewEditor.saveFailed' : 'goviewEditor.publishFailed');
    }
  } finally { current.child?.finish(); if (write === current) { write = undefined; saving.value = false; publishing.value = false; owner.value++; } request.finish(); }
}
async function saveDraft(): Promise<void> { await execute(false); }
async function publish(): Promise<void> { await execute(true); }
async function openPreview(): Promise<void> {
  const target = project.value ? { ...project.value } : undefined; if (!canRead.value || !ready.value || busy.value || !target?.isEnabled || target.latestPublishedVersionNumber < 1) return;
  const request = context.begin('goview.projects.preview'); if (!request) return;
  try { await router.push({ name: 'goview-preview', params: { projectId: target.id }, query: { versionNumber: String(target.latestPublishedVersionNumber) } }); } catch (error) { if (request.current()) problem.value = toProblem(error); } finally { request.finish(); }
}
async function acknowledgeRecovery(): Promise<void> {
  if (!canRead.value || !ready.value || busy.value || !block.value) return; const previous = block.value, ticket = owner.value, request = context.begin(previous.kind === 'save' ? 'goview.projects.update' : 'goview.projects.publish'); if (!request) return; recoveryRequest = request; recovering.value = true;
  try { const confirmed = await new Promise<boolean>(resolve => { recovery.value = { resolve }; }); if (confirmed && request.current() && ticket === owner.value && block.value === previous) block.value = undefined; }
  finally { if (recoveryRequest === request) { recoveryRequest = undefined; recovering.value = false; owner.value++; } request.finish(); }
}
const recoveryHandlers = computed(() => { const pending = recovery.value, finish = (value: boolean) => { if (pending && recovery.value === pending) finishRecovery(value); }; return { confirm: () => finish(true), update: (value: boolean) => { if (!value) finish(false); } }; });
const controls = computed(() => {
  const ticket = owner.value, id = projectId.value, owned = () => canRead.value && ticket === owner.value && projectId.value === id;
  const input = (key: 'name' | 'canvasJson' | 'isEnabled', value: string | boolean) => { if (owned() && ready.value && !busy.value && session.can('goview.projects.update')) { if (key === 'isEnabled') editorForm.value.isEnabled = Boolean(value); else editorForm.value[key] = String(value); } };
  return { refresh: () => { if (owned()) void load(); }, save: () => { if (owned()) void saveDraft(); }, publish: () => { if (owned()) void publish(); }, preview: () => { if (owned()) void openPreview(); }, recover: () => { if (owned()) void acknowledgeRecovery(); }, name: (value: string) => input('name', value), canvas: (value: string) => input('canvasJson', value), enabled: (value: boolean) => input('isEnabled', value) };
});
</script>

<template>
  <div v-if="canRead" class="goview-editor-view">
    <ArtTableHeader :title="t('goviewEditor.title')" :loading="loading" v-on="{ refresh: controls.refresh }">
      <PermissionGate code="goview.projects.preview">
        <ElButton
          data-testid="goview-editor-open-preview"
          :disabled="!ready || busy || !project?.isEnabled || project.latestPublishedVersionNumber <= 0"
          v-on="{ click: controls.preview }"
        >
          {{ t('goviewEditor.openPreview') }}
        </ElButton>
      </PermissionGate>
      <PermissionGate code="goview.projects.update">
        <ElButton
          data-testid="goview-editor-save"
          type="primary"
          :loading="saving"
          :disabled="!writable()" v-on="{ click: controls.save }"
        >
          {{ t('goviewEditor.saveDraft') }}
        </ElButton>
      </PermissionGate>
      <PermissionGate code="goview.projects.publish">
        <ElButton
          v-if="session.can('goview.projects.publish')"
          data-testid="goview-editor-publish"
          type="success"
          :loading="publishing"
          :disabled="!writable() || !project?.isEnabled || !editorForm.isEnabled" v-on="{ click: controls.publish }"
        >
          {{ t('goviewEditor.publish') }}
        </ElButton>
      </PermissionGate>
    </ArtTableHeader>

    <ElAlert
      v-if="problem"
      type="error"
      :title="problem.title"
      show-icon
      class="mb-4"
    />

    <p v-if="saveReceipt" role="status" data-testid="goview-save-receipt">{{ t(saveReceipt.outcome === 'succeeded' ? 'goviewEditor.saveSuccess' : saveReceipt.outcome === 'unconfirmed' ? 'goviewCommon.unconfirmed' : saveReceipt.outcome === 'processing' ? 'goviewCommon.processing' : 'goviewEditor.saveFailed') }} · {{ saveReceipt.version }}</p>
    <p v-if="publicationReceipt" role="status" data-testid="goview-publish-receipt">{{ t(publicationReceipt.outcome === 'succeeded' ? 'goviewEditor.publishSuccess' : publicationReceipt.outcome === 'unconfirmed' ? 'goviewCommon.unconfirmed' : publicationReceipt.outcome === 'processing' ? 'goviewCommon.processing' : 'goviewEditor.publishFailed') }} · {{ publicationReceipt.versionNumber ?? publicationReceipt.version }}</p>
    <ElButton v-if="block && session.can(block.kind === 'save' ? 'goview.projects.update' : 'goview.projects.publish')" :disabled="!ready || busy" data-testid="goview-editor-recover" v-on="{ click: controls.recover }">{{ t('goviewCommon.recoveryTitle') }}</ElButton>
    <ElCard v-loading="loading" shadow="never">
      <ElForm label-width="120px" :disabled="!ready || busy || !session.can('goview.projects.update')">
        <ElFormItem :label="t('goviewProjects.fieldName')" required>
          <ElInput :model-value="editorForm.name" v-on="{ 'update:modelValue': controls.name }" data-testid="goview-editor-name" />
        </ElFormItem>
        <ElFormItem :label="t('goviewProjects.fieldEnabled')">
          <ElSwitch :model-value="editorForm.isEnabled" v-on="{ 'update:modelValue': controls.enabled }" />
        </ElFormItem>
        <ElFormItem :label="t('goviewEditor.componentCount')">
          <span data-testid="goview-editor-component-count">{{ componentCount }}</span>
        </ElFormItem>
        <ElFormItem :label="t('goviewEditor.canvasJson')" required>
          <ElInput
            :model-value="editorForm.canvasJson" v-on="{ 'update:modelValue': controls.canvas }"
            type="textarea"
            :rows="18"
            data-testid="goview-editor-canvas-json"
          />
        </ElFormItem>
      </ElForm>
    </ElCard>
    <ArtFormDialog v-if="recovery" :open="true" :title="t('goviewCommon.recoveryTitle')" :confirm-label="t('users.confirm')" :cancel-label="t('users.cancel')" v-on="{ confirm: recoveryHandlers.confirm, 'update:open': recoveryHandlers.update }"><p>{{ t('goviewCommon.recoveryHint') }}</p></ArtFormDialog>
  </div>
</template>
