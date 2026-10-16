<script setup lang="ts">
import { computed, inject, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import { ElButton, ElCard, ElDialog, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { createWorkflowFormDraft, isFullNetProblemDetails, type FullNetProblemDetails, type WorkflowFormComponentCatalogResponse, type WorkflowFormSchema } from '@fullnet/client-contracts';
import { createWorkflowForm, deleteWorkflowFormVersion, getWorkflowForm, getWorkflowFormComponentCatalog, listWorkflowFormVersions, listWorkflowForms, publishWorkflowForm, setWorkflowFormStatus, updateWorkflowFormDraft, type WorkflowFormResponse, type WorkflowFormVersionResponse } from '../api/workflow-forms';
import { normalizeWorkflowFormKey, sameWorkflowFormDraft, validWorkflowForm, validWorkflowFormCatalog, validWorkflowFormVersion, workflowFormIdValid, workflowFormKeyValid, workflowFormValueKey } from '../api/workflow-form-guards';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import PermissionGate from '../components/PermissionGate.vue';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';
import VForm3WorkflowDesigner from '../workflow/VForm3WorkflowDesigner.vue';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
interface VForm3WorkflowDesignerInstance { readSchema: () => WorkflowFormSchema; }
type Kind = 'create' | 'save' | 'publish' | 'status' | 'delete';
type Status = 'active' | 'disabled' | 'archived';
const permissions: Record<Kind, string> = { create: 'workflow.forms.create', save: 'workflow.forms.update', publish: 'workflow.forms.publish', status: 'workflow.forms.manage_status', delete: 'workflow.forms.delete_version' };
const { t, locale } = useAdminI18n(), session = useSessionStore(), route = inject(routeLocationKey, undefined), active = ref(false);
onMounted(() => { active.value = true; }); onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; });
const canRead = computed(() => { const user = session.currentUser; return active.value && (!route || route.name === 'workflow-forms') && session.can('workflow.forms.read') && !!user && (user.scope === 'host' && user.tenantId === null || workflowFormIdValid(user.tenantId) && user.scope === 'tenant:' + user.tenantId.replaceAll('-', '').toLowerCase()); });
const forms = ref<WorkflowFormResponse[]>([]), versions = ref<WorkflowFormVersionResponse[]>([]), catalogReady = ref(false), editorReady = ref(false), versionsReady = ref(false);
const selectedFormId = ref<string>(), selectedForm = ref<WorkflowFormResponse>(), selected = ref<WorkflowFormResponse>(), editorId = ref<string>(), localDraft = ref<WorkflowFormSchema>(), catalog = ref<WorkflowFormComponentCatalogResponse>();
const formKey = ref(''), creating = ref(false), createBlocked = ref(false), designer = ref<VForm3WorkflowDesignerInstance>(), problem = ref<FullNetProblemDetails>(), problemIsRead = ref(true);
const listLoading = ref(false), editorLoading = ref(false), versionsLoading = ref(false), writing = ref(false);
const busy = computed(() => listLoading.value || editorLoading.value || versionsLoading.value || writing.value || !!recovery.value);
const { tableMainRef, tableHeight, updateTableHeight, watchLoading } = useArtCrudTableLayout({ bottomOffset: 8 });
const owner = ref(0), createOwner = ref(0), editorOwner = ref(0), versionOwner = ref(0), editorWindow = ref(0);
const receipt = ref<{ kind: Kind; id?: string; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed' }>();
interface Block { kind: Kind; formId?: string; id?: string; revision: number; version: number; }
const blocks = ref(new Map<string, Block>()), floors = new Map<string, { revision: number; version: number; key: string; status: string; latest: string | null }>(), deleted = new Set<string>(), knownVersions = new Map<string, { parent: string; number: number; hash: string }>();
const scope = useAuthorizedViewScope(session, reset, async () => { if (canRead.value) await loadForms(); });
type Lease = NonNullable<ReturnType<typeof scope.begin>>;
let listRequest: Lease | undefined, editorRequest: Lease | undefined, versionsRequest: Lease | undefined;
let write: { lease: Lease; block: Block; key: string } | undefined, applyingDraft = false;
const recovery = ref<{ key: string; block: Block; lease: Lease; ticket: number }>();
const deletion = ref<{ row: WorkflowFormVersionResponse; ticket: number }>();
watchLoading(busy);
watch(() => JSON.stringify([route?.name, route?.params, route?.query]), () => { scope.invalidate(); if (canRead.value) void loadForms(); }, { flush: 'sync' });
function begin(permission: string): Lease | undefined { return canRead.value ? scope.begin(permission) : undefined; }
function reset(): void {
  listRequest = editorRequest = versionsRequest = undefined; write = undefined; recovery.value?.lease.cancel(); recovery.value = undefined; deletion.value = undefined;
  forms.value = []; versions.value = []; selected.value = undefined; selectedForm.value = undefined; selectedFormId.value = editorId.value = undefined; localDraft.value = undefined; catalog.value = undefined; creating.value = false; formKey.value = ''; createBlocked.value = false; receipt.value = undefined; problem.value = undefined;
  listLoading.value = editorLoading.value = versionsLoading.value = writing.value = false; catalogReady.value = editorReady.value = versionsReady.value = false; blocks.value.clear(); floors.clear(); deleted.clear(); knownVersions.clear(); owner.value++; createOwner.value++; editorOwner.value++; versionOwner.value++; editorWindow.value++;
}
const idKey = (id: string) => id.toLowerCase();
const copyDraft = (value: WorkflowFormSchema): WorkflowFormSchema => JSON.parse(JSON.stringify(value)) as WorkflowFormSchema;
function failure(error: unknown, load = true): void { const value: FullNetProblemDetails = isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.workflow_form_failed', title: t(load ? 'workflowForms.loadFailed' : 'workflowForms.operationFailed') }; problem.value = value; problemIsRead.value = load; if (!load) showProblem(value, t('workflowForms.operationFailed')); }
function assertForm(value: unknown, id?: string): asserts value is WorkflowFormResponse {
  if (!validWorkflowForm(value) || id && idKey(value.id) !== idKey(id)) throw Error('client.invalid_workflow_form');
  if (value.latestPublishedVersionId && deleted.has(idKey(value.latestPublishedVersionId))) throw Error('client.stale_workflow_form');
  const floor = floors.get(idKey(value.id)), previous = forms.value.find(row => idKey(row.id) === idKey(value.id));
  if (floor && (value.draftRevision < floor.revision || value.version < floor.version || value.formKey !== floor.key || value.version === floor.version && (value.statusKey !== floor.status || value.latestPublishedVersionId?.toLowerCase() !== floor.latest?.toLowerCase())) || previous && value.draftRevision === previous.draftRevision && !sameWorkflowFormDraft(value.draft, previous.draft)) throw Error('client.stale_workflow_form');
}
function observe(value: WorkflowFormResponse): void {
  floors.set(idKey(value.id), { revision: value.draftRevision, version: value.version, key: value.formKey, status: value.statusKey, latest: value.latestPublishedVersionId });
  for (const [key, block] of blocks.value) if (block.formId && idKey(block.formId) === idKey(value.id) && (block.kind === 'save' || block.kind === 'publish' ? value.draftRevision > block.revision : block.kind === 'status' && value.version > block.version)) blocks.value.delete(key);
}
function replaceForm(value: WorkflowFormResponse): void { assertForm(value); observe(value); const index = forms.value.findIndex(row => idKey(row.id) === idKey(value.id)); forms.value = index < 0 ? [...forms.value, value] : forms.value.map((row, i) => i === index ? value : row); if (selectedFormId.value && idKey(selectedFormId.value) === idKey(value.id)) selectedForm.value = value; }
function current(row: WorkflowFormResponse): boolean { return forms.value.some(value => idKey(value.id) === idKey(row.id) && value.draftRevision === row.draftRevision && value.version === row.version && value.statusKey === row.statusKey && value.latestPublishedVersionId === row.latestPublishedVersionId); }
function blockKey(row: WorkflowFormResponse): string | undefined { return [...blocks.value].find(([, block]) => block.formId && idKey(block.formId) === idKey(row.id))?.[0]; }
function rowBlocked(row: WorkflowFormResponse): boolean { return blockKey(row) !== undefined; }
async function loadForms(): Promise<void> {
  if (!canRead.value || writing.value || recovery.value) return; listRequest?.cancel(); const request = begin('workflow.forms.read'); if (!request) return; listRequest = request; listLoading.value = true; catalogReady.value = false; problem.value = undefined; owner.value++;
  try { const rows = await listWorkflowForms(request.signal); if (!request.current() || listRequest !== request) return; if (!Array.isArray(rows) || new Set(rows.map(row => idKey(row.id))).size !== rows.length) throw Error('client.invalid_workflow_form_list'); for (const row of rows) assertForm(row); for (const row of rows) observe(row); forms.value = rows; catalogReady.value = true;
    if (selected.value) { const value = rows.find(row => idKey(row.id) === idKey(selected.value!.id)); if (!value || value.draftRevision !== selected.value.draftRevision || value.version !== selected.value.version) editorReady.value = false; }
  } catch (error) { if (request.current() && listRequest === request) failure(error); }
  finally { if (listRequest === request) { listRequest = undefined; listLoading.value = false; owner.value++; } request.finish(); void nextTick(updateTableHeight); }
}
function markUnknown(operation: NonNullable<typeof write>): void { if (operation.block.kind === 'create') createBlocked.value = true; else blocks.value.set(operation.key, operation.block); receipt.value = { kind: operation.block.kind, id: operation.block.id, outcome: 'unconfirmed' }; catalogReady.value = false; if (operation.block.kind === 'save') editorReady.value = false; if (operation.block.kind === 'delete') versionsReady.value = false; }
function cancelWrite(kind: Kind): void { if (write?.block.kind !== kind) return; markUnknown(write); write.lease.cancel(); write = undefined; writing.value = false; owner.value++; }
function openCreate(): void { if (!canRead.value || !session.can(permissions.create) || busy.value) return; formKey.value = ''; creating.value = true; createOwner.value++; }
function closeCreate(): void { cancelWrite('create'); creating.value = false; formKey.value = ''; createOwner.value++; finishRecovery(false); }
watch(formKey, () => { createOwner.value++; cancelWrite('create'); finishRecovery(false); }, { flush: 'sync' });
watch(localDraft, () => { if (!applyingDraft) { editorOwner.value++; cancelWrite('save'); finishRecovery(false); } }, { deep: true, flush: 'sync' });
function assignDraft(value: WorkflowFormSchema): void { applyingDraft = true; try { localDraft.value = copyDraft(value); } finally { applyingDraft = false; } }
async function runWrite<T>(kind: Kind, row: WorkflowFormResponse | undefined, invoke: (signal: AbortSignal) => Promise<T>, check: (value: T) => void, commit: (value: T) => void, versionId?: string): Promise<void> {
  if (!canRead.value || busy.value || kind !== 'create' && (!row || rowBlocked(row))) return; const lease = begin(permissions[kind]); if (!lease) return;
  const operation = { lease, key: kind === 'create' ? 'create' : versionId ? 'delete:' + idKey(versionId) : idKey(row!.id), block: { kind, id: versionId ?? row?.id, formId: row?.id, revision: row?.draftRevision ?? 0, version: row?.version ?? 0 } }; write = operation; writing.value = true; owner.value++; createOwner.value++; editorOwner.value++; versionOwner.value++; receipt.value = { kind, id: operation.block.id, outcome: 'processing' }; problem.value = undefined;
  try { const result = await invoke(lease.signal); if (!lease.current() || write !== operation) return; check(result);
    // 先固定成功回执并解除请求归属，再关闭窗口或应用草稿，避免自身 watcher 将已确认写入改成未知。
    write = undefined; writing.value = false; receipt.value = { kind, id: operation.block.id, outcome: 'succeeded' }; commit(result); owner.value++; createOwner.value++; editorOwner.value++; versionOwner.value++; showSuccess(t(kind === 'delete' ? 'workflowForms.deleteVersionSuccess' : 'workflowForms.recoveryCompleted'));
    if (kind !== 'save') { catalogReady.value = false; if (kind === 'delete') await loadVersions(); await loadForms(); }
  } catch (error) { if (lease.current() && write === operation) { const unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408; if (unknown) markUnknown(operation); else receipt.value = { kind, id: operation.block.id, outcome: 'failed' }; catalogReady.value = false; if (kind === 'save') editorReady.value = false; if (kind === 'delete') versionsReady.value = false; failure(error, false); } }
  finally { if (write === operation) { write = undefined; writing.value = false; owner.value++; createOwner.value++; editorOwner.value++; versionOwner.value++; } lease.finish(); void nextTick(updateTableHeight); }
}
async function submitCreate(): Promise<void> {
  if (!creating.value || createBlocked.value) return; const key = normalizeWorkflowFormKey(formKey.value); if (!workflowFormKeyValid(key)) { showWarning(t('workflowForms.operationFailed')); return; } const draft = createWorkflowFormDraft();
  await runWrite('create', undefined, signal => createWorkflowForm(key, draft, signal), value => { assertForm(value); if (floors.has(idKey(value.id)) || value.formKey !== key || value.draftRevision !== 1 || value.version !== 1 || value.latestPublishedVersionId !== null || value.statusKey !== 'active' || !sameWorkflowFormDraft(value.draft, draft)) throw Error('client.invalid_workflow_form_create'); }, value => { replaceForm(value); closeCreate(); });
}
async function openEditor(row: WorkflowFormResponse): Promise<void> {
  if (!canRead.value || !session.can(permissions.save) || !catalogReady.value || busy.value || !current(row) || formStatus(row) === 'archived') return; closeEditor(); editorId.value = row.id; editorWindow.value++; await reloadEditor();
}
async function reloadEditor(): Promise<void> {
  if (!editorId.value || writing.value || recovery.value) return; const id = editorId.value; editorRequest?.cancel(); const request = begin(permissions.save); if (!request) return; editorRequest = request; editorLoading.value = true; editorReady.value = false; editorOwner.value++; problem.value = undefined;
  try { const [value, components] = await Promise.all([getWorkflowForm(id, request.signal), getWorkflowFormComponentCatalog(request.signal)]); if (!request.current() || editorRequest !== request || editorId.value !== id) return; assertForm(value, id); if (!validWorkflowFormCatalog(components)) throw Error('client.invalid_workflow_form_catalog');
    const keep = selected.value?.id === value.id && selected.value.draftRevision === value.draftRevision && localDraft.value !== undefined; replaceForm(value); selected.value = value; if (!keep) { assignDraft(value.draft); editorWindow.value++; } catalog.value = components; editorReady.value = true;
  } catch (error) { if (request.current() && editorRequest === request) failure(error); }
  finally { if (editorRequest === request) { editorRequest = undefined; editorLoading.value = false; editorOwner.value++; } request.finish(); }
}
function closeEditor(): void { cancelWrite('save'); editorRequest?.cancel(); editorRequest = undefined; editorLoading.value = false; editorReady.value = false; editorId.value = undefined; selected.value = undefined; localDraft.value = undefined; catalog.value = undefined; editorOwner.value++; editorWindow.value++; finishRecovery(false); }
async function saveDraft(): Promise<void> {
  const currentForm = selected.value; if (!editorReady.value || !currentForm || !editorId.value || !localDraft.value || formStatus(currentForm) === 'archived' || !current(currentForm) || busy.value || !session.can(permissions.save)) return; let draft: WorkflowFormSchema;
  try { if (!designer.value?.readSchema) throw Error('client.workflow_designer_not_ready'); draft = copyDraft(designer.value.readSchema()); } catch { showDesignerError('client.invalid_workflow_form_draft'); return; }
  await runWrite('save', { ...currentForm }, signal => updateWorkflowFormDraft(currentForm.id, currentForm.draftRevision, draft, signal), value => { assertForm(value, currentForm.id); if (value.draftRevision !== currentForm.draftRevision + 1 || !sameWorkflowFormDraft(value.draft, draft)) throw Error('client.invalid_workflow_form_save'); }, value => { replaceForm(value); selected.value = value; assignDraft(value.draft); editorReady.value = true; editorWindow.value++; });
}
async function publish(row: WorkflowFormResponse): Promise<void> {
  if (!catalogReady.value || !current(row) || editorId.value || formStatus(row) !== 'active') return; const target = { ...row, draft: copyDraft(row.draft) };
  const publishedForm = (value: WorkflowFormVersionResponse): WorkflowFormResponse => ({ ...target, draftRevision: target.draftRevision + 1, version: target.version + 1, latestPublishedVersionId: value.id, updatedAtUtc: value.publishedAtUtc });
  await runWrite('publish', target, signal => publishWorkflowForm(target.id, { expectedRevision: target.draftRevision }, signal), value => { if (!validWorkflowFormVersion(value, target.id) || knownVersions.has(idKey(value.id)) || deleted.has(idKey(value.id)) || idKey(value.publishedById) !== idKey(session.currentUser!.id) || !sameWorkflowFormDraft(JSON.parse(value.formSchemaJson), target.draft)) throw Error('client.invalid_workflow_form_publication'); assertForm(publishedForm(value)); }, value => { knownVersions.set(idKey(value.id), { parent: target.id, number: value.versionNumber, hash: value.contentHash }); replaceForm(publishedForm(value)); if (selectedFormId.value === target.id) { versionsReady.value = false; } });
}
async function changeFormStatus(row: WorkflowFormResponse, statusKey: Status): Promise<void> {
  if (!catalogReady.value || !current(row) || editorId.value || formStatus(row) === 'archived') return; const target = { ...row };
  await runWrite('status', target, signal => setWorkflowFormStatus(target.id, statusKey, target.version, signal), value => { assertForm(value, target.id); if (value.statusKey !== statusKey || value.version <= target.version) throw Error('client.invalid_workflow_form_status'); }, replaceForm);
}
async function openVersions(row: WorkflowFormResponse): Promise<void> { if (!catalogReady.value || !current(row) || busy.value) return; closeVersions(); selectedFormId.value = row.id; selectedForm.value = { ...row }; await loadVersions(); }
async function loadVersions(): Promise<void> {
  if (!selectedFormId.value || writing.value || recovery.value) return; const id = selectedFormId.value; versionsRequest?.cancel(); const request = begin('workflow.forms.read'); if (!request) return; versionsRequest = request; versionsLoading.value = true; versionsReady.value = false; versionOwner.value++; problem.value = undefined;
  try { const [parent, rows] = await Promise.all([getWorkflowForm(id, request.signal), listWorkflowFormVersions(id, request.signal)]); if (!request.current() || versionsRequest !== request || selectedFormId.value !== id) return; assertForm(parent, id);
    if (!rows.every(row => validWorkflowFormVersion(row, id)) || new Set(rows.map(row => idKey(row.id))).size !== rows.length || new Set(rows.map(row => row.versionNumber)).size !== rows.length || parent.latestPublishedVersionId && !rows.some(row => idKey(row.id) === idKey(parent.latestPublishedVersionId!))) throw Error('client.invalid_workflow_form_versions');
    for (const row of rows) { const known = knownVersions.get(idKey(row.id)), old = versions.value.find(value => idKey(value.id) === idKey(row.id)); if (deleted.has(idKey(row.id)) || known && (idKey(known.parent) !== idKey(id) || known.number !== row.versionNumber || known.hash !== row.contentHash) || old && workflowFormValueKey(old) !== workflowFormValueKey(row)) throw Error('client.stale_workflow_form_version'); }
    replaceForm(parent); versions.value = rows; selectedForm.value = parent; for (const row of rows) knownVersions.set(idKey(row.id), { parent: id, number: row.versionNumber, hash: row.contentHash });
    // 删除非最新版本不会推进父记录版本；权威缺席确认后也必须记住不可复活的版本标识。
    for (const [key, block] of blocks.value) if (block.kind === 'delete' && block.formId === id && !rows.some(row => idKey(row.id) === idKey(block.id!))) { deleted.add(idKey(block.id!)); blocks.value.delete(key); } versionsReady.value = true;
  } catch (error) { if (request.current() && versionsRequest === request) failure(error); }
  finally { if (versionsRequest === request) { versionsRequest = undefined; versionsLoading.value = false; versionOwner.value++; } request.finish(); }
}
function closeVersions(): void { cancelWrite('delete'); versionsRequest?.cancel(); versionsRequest = undefined; versionsLoading.value = false; versionsReady.value = false; versions.value = []; selectedFormId.value = undefined; selectedForm.value = undefined; deletion.value = undefined; versionOwner.value++; finishRecovery(false); }
async function removeFormVersion(row: WorkflowFormVersionResponse): Promise<void> {
  const parent = selectedForm.value; if (!versionsReady.value || !parent || !selectedFormId.value || idKey(row.formDefinitionId) !== idKey(parent.id) || !versions.value.some(value => workflowFormValueKey(value) === workflowFormValueKey(row))) return; const target = { ...row };
  await runWrite('delete', { ...parent }, signal => deleteWorkflowFormVersion(target.id, signal), () => {}, () => { deleted.add(idKey(target.id)); versions.value = versions.value.filter(value => idKey(value.id) !== idKey(target.id)); versionsReady.value = false; }, target.id);
}
function recover(key: string): void {
  if (!canRead.value || busy.value) return; const block = key === 'create' && createBlocked.value && creating.value ? { kind: 'create' as const, revision: 0, version: 0 } : blocks.value.get(key); if (!block) return; const lease = begin(permissions[block.kind]); if (!lease) return; recovery.value = { key, block, lease, ticket: owner.value }; deletion.value = undefined;
}
function finishRecovery(yes: boolean): void { const pending = recovery.value; if (!pending) return; recovery.value = undefined; if (yes && pending.lease.current() && pending.ticket === owner.value) { if (pending.key === 'create' && creating.value) createBlocked.value = false; else if (blocks.value.get(pending.key) === pending.block) blocks.value.delete(pending.key); } pending.lease.finish(); owner.value++; createOwner.value++; editorOwner.value++; versionOwner.value++; }
function showDesignerError(code: string): void { problem.value = { status: 400, code, title: t('workflowForms.operationFailed') }; problemIsRead.value = false; showWarning(t('workflowForms.operationFailed')); }
function formStatus(value: WorkflowFormResponse): Status { return value.statusKey as Status; }
function formStatusTagType(status: Status): 'success' | 'warning' | 'info' { return status === 'active' ? 'success' : status === 'disabled' ? 'warning' : 'info'; }
function formatDateTime(value: string | null | undefined): string { return formatAdminDateTime(locale.value, value); }
const controls = computed(() => { const ticket = owner.value, owned = () => canRead.value && ticket === owner.value; return { refresh: () => { if (owned()) void loadForms(); }, create: () => { if (owned()) openCreate(); } }; });
const rowControls = computed(() => { const ticket = owner.value; return new Map(forms.value.map(row => { const target = { ...row }, owned = () => canRead.value && ticket === owner.value && current(target), status = (value: Status) => { if (owned()) void changeFormStatus(target, value); }; return [row.id, { edit: () => { if (owned()) void openEditor(target); }, versions: () => { if (owned()) void openVersions(target); }, publish: () => { if (owned()) void publish(target); }, disable: () => status('disabled'), enable: () => status('active'), archive: () => status('archived'), recover: () => { const key = blockKey(target); if (owned() && key) recover(key); } }]; })); });
const createControls = computed(() => { const ticket = createOwner.value, owned = () => canRead.value && creating.value && ticket === createOwner.value; return { input: (event: Event) => { if (owned() && !busy.value) formKey.value = (event.target as HTMLInputElement).value; }, close: () => { if (owned()) closeCreate(); }, submit: () => { if (owned()) void submitCreate(); }, recover: () => { if (owned()) recover('create'); } }; });
const editorControls = computed(() => { const ticket = editorOwner.value, window = editorWindow.value, owned = () => canRead.value && !!editorId.value && ticket === editorOwner.value && window === editorWindow.value; return { input: (value: WorkflowFormSchema) => { if (owned() && editorReady.value && !busy.value) localDraft.value = copyDraft(value); }, error: (code: string) => { if (owned()) showDesignerError(code); }, close: () => { if (owned()) closeEditor(); }, reload: () => { if (owned()) void reloadEditor(); }, save: () => { if (owned()) void saveDraft(); }, recover: () => { if (owned() && selected.value) { const key = blockKey(selected.value); if (key) recover(key); } } }; });
const versionControls = computed(() => { const ticket = versionOwner.value, id = selectedFormId.value, owned = () => canRead.value && ticket === versionOwner.value && id === selectedFormId.value; return { close: () => { if (owned()) closeVersions(); }, reload: () => { if (owned()) void loadVersions(); }, rows: new Map(versions.value.map(row => [row.id, () => { if (owned() && !busy.value && session.can(permissions.delete)) deletion.value = { row: { ...row }, ticket }; }])) }; });
const recoveryControls = computed(() => { const pending = recovery.value, finish = (yes: boolean) => { if (pending && recovery.value === pending) finishRecovery(yes); }; return { confirm: () => finish(true), cancel: () => finish(false), close: (open: boolean) => { if (!open) finish(false); } }; });
const deletionControls = computed(() => { const pending = deletion.value, cancel = () => { if (deletion.value === pending) deletion.value = undefined; }; return { cancel, close: (open: boolean) => { if (!open) cancel(); }, confirm: () => { if (!pending || deletion.value !== pending) return; deletion.value = undefined; if (canRead.value && pending.ticket === versionOwner.value) void removeFormVersion(pending.row); } }; });
</script>

<template>
  <section v-if="canRead" class="workflow-forms art-page-stack art-full-height" :aria-busy="busy">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowForms.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowForms.caption') }}</p>

    <div v-if="problem && problemIsRead && !editorId" class="art-inline-alert" role="alert"><span>{{ problem.title }}</span></div>
    <p v-if="receipt" role="status" data-testid="workflow-form-receipt">{{ t('workflowForms.recoveryAction.' + receipt.kind) }}：{{ t('workflowForms.recovery.' + receipt.outcome) }}</p>
    <el-button data-testid="workflow-form-refresh" :disabled="busy" v-on="{ click: controls.refresh }">{{ t('workflowForms.recoveryRetry') }}</el-button>
    <div class="workflow-forms__layout art-split-layout">
      <el-card class="workflow-forms__list art-table-card" shadow="never">
        <template #header>
          <div class="workflow-forms__list-header">
            <h2>{{ t('workflowForms.listTitle') }}</h2>
            <PermissionGate code="workflow.forms.create">
              <el-button type="primary" size="small" data-testid="workflow-form-create" :disabled="busy" v-on="{ click: controls.create }">
                {{ t('workflowForms.create') }}
              </el-button>
            </PermissionGate>
          </div>
        </template>
        <div ref="tableMainRef" class="art-crud-table-main">
        <el-table
          v-loading="busy"
          :data="forms"
          :height="tableHeight"
          row-key="id"
          highlight-current-row
          empty-text=""
          :current-row-key="selectedFormId"
          class="workflow-forms__table"
        >
          <el-table-column :label="t('workflowForms.formKey')" min-width="140" show-overflow-tooltip>
            <template #default="{ row }"><code translate="no">{{ row.formKey }}</code></template>
          </el-table-column>
          <el-table-column :label="t('workflowForms.status')" width="96" align="center">
            <template #default="{ row }">
              <el-tag size="small" :type="formStatusTagType(formStatus(row as WorkflowFormResponse))">
                {{ t(`workflowForms.statusLabel.${formStatus(row as WorkflowFormResponse)}`) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('workflowForms.revision')" width="110">
            <template #default="{ row }">Revision {{ row.draftRevision }}</template>
          </el-table-column>
          <el-table-column :label="t('workflowForms.publishedVersion')" min-width="120" show-overflow-tooltip>
            <template #default="{ row }"><span translate="no">{{ row.latestPublishedVersionId ?? '—' }}</span></template>
          </el-table-column>
          <el-table-column :label="t('workflowForms.actions')" min-width="360" fixed="right">
            <template #default="{ row }">
              <div class="workflow-forms__actions">
                <PermissionGate code="workflow.forms.update">
                  <el-button size="small" data-testid="workflow-form-edit" :disabled="busy || !catalogReady || formStatus(row as WorkflowFormResponse) === 'archived'" v-on="{ click: rowControls.get(row.id)?.edit }">
                    {{ t('workflowForms.edit') }}
                  </el-button>
                </PermissionGate>
                <el-button size="small" data-testid="workflow-form-versions" :disabled="busy || !catalogReady" v-on="{ click: rowControls.get(row.id)?.versions }">
                  {{ t('workflowForms.versions') }}
                </el-button>
                <PermissionGate code="workflow.forms.publish">
                  <el-button type="primary" plain size="small" data-testid="workflow-form-publish" :disabled="busy || !catalogReady || !!editorId || rowBlocked(row) || formStatus(row as WorkflowFormResponse) !== 'active'" v-on="{ click: rowControls.get(row.id)?.publish }">
                    {{ t('workflowForms.publish') }}
                  </el-button>
                </PermissionGate>
                <PermissionGate v-if="formStatus(row as WorkflowFormResponse) === 'active'" code="workflow.forms.manage_status">
                  <el-button size="small" data-testid="workflow-form-disable" :disabled="busy || !catalogReady || !!editorId || rowBlocked(row)" v-on="{ click: rowControls.get(row.id)?.disable }">
                    {{ t('workflowForms.disable') }}
                  </el-button>
                </PermissionGate>
                <PermissionGate v-if="formStatus(row as WorkflowFormResponse) === 'disabled'" code="workflow.forms.manage_status">
                  <el-button size="small" data-testid="workflow-form-enable" :disabled="busy || !catalogReady || !!editorId || rowBlocked(row)" v-on="{ click: rowControls.get(row.id)?.enable }">
                    {{ t('workflowForms.enable') }}
                  </el-button>
                </PermissionGate>
                <PermissionGate v-if="formStatus(row as WorkflowFormResponse) !== 'archived'" code="workflow.forms.manage_status">
                  <el-button size="small" data-testid="workflow-form-archive" :disabled="busy || !catalogReady || !!editorId || rowBlocked(row)" v-on="{ click: rowControls.get(row.id)?.archive }">
                    {{ t('workflowForms.archive') }}
                  </el-button>
                </PermissionGate>
                <PermissionGate v-if="blockKey(row)" :code="permissions[blocks.get(blockKey(row)!)!.kind]"><el-button size="small" :disabled="busy" v-on="{ click: rowControls.get(row.id)?.recover }">{{ t('workflowForms.recoveryUnlock') }}</el-button></PermissionGate>
              </div>
            </template>
          </el-table-column>
          <template #empty>
            <p v-if="!busy" class="workflow-forms__empty">{{ t('workflowForms.empty') }}</p>
          </template>
        </el-table>
        </div>
      </el-card>

      <el-card class="workflow-forms__versions-panel art-form-card" shadow="never">
        <template #header><h2>{{ t('workflowForms.versionsTitle') }}</h2></template>
        <template v-if="selectedFormId"><el-button data-testid="workflow-form-reload-versions" :disabled="busy" v-on="{ click: versionControls.reload }">{{ t('workflowForms.recoveryRetry') }}</el-button><el-button v-on="{ click: versionControls.close }">{{ t('workflowForms.close') }}</el-button></template>
        <p v-if="!selectedFormId" class="workflow-forms__empty">{{ t('workflowForms.selectVersions') }}</p>
        <div v-else-if="versions.length === 0" class="workflow-forms__empty">{{ t('workflowForms.noVersions') }}</div>
        <ul v-else class="workflow-forms__versions">
          <li v-for="version in versions" :key="version.id">
            <span>{{ t('workflowForms.version') }} {{ version.versionNumber }}</span>
            <time :datetime="version.publishedAtUtc">{{ formatDateTime(version.publishedAtUtc) }}</time>
            <PermissionGate code="workflow.forms.delete_version">
              <el-button type="danger" plain size="small" data-testid="workflow-form-delete-version" :disabled="busy || !versionsReady || !!selectedForm && rowBlocked(selectedForm)" v-on="{ click: versionControls.rows.get(version.id) }">
                {{ t('workflowForms.deleteVersion') }}
              </el-button>
            </PermissionGate>
          </li>
        </ul>
      </el-card>
    </div>

    <aside v-if="creating" class="workflow-forms__panel" aria-modal="true" role="dialog">
      <h2>{{ t('workflowForms.createTitle') }}</h2>
      <label>
        <span>{{ t('workflowForms.formKey') }}</span>
        <input :value="formKey" :disabled="busy" data-testid="workflow-form-key" autocomplete="off" required v-on="{ input: createControls.input }" />
      </label>
      <el-button v-if="createBlocked" :disabled="busy" v-on="{ click: createControls.recover }">{{ t('workflowForms.recoveryUnlock') }}</el-button>
      <div class="workflow-forms__decision-bar">
        <el-button v-on="{ click: createControls.close }">{{ t('workflowForms.close') }}</el-button>
        <el-button type="primary" data-testid="workflow-form-create-submit" :disabled="busy || createBlocked || !workflowFormKeyValid(normalizeWorkflowFormKey(formKey))" :loading="writing" v-on="{ click: createControls.submit }">
          {{ t('workflowForms.create') }}
        </el-button>
      </div>
    </aside>

    <aside v-if="editorId" class="workflow-forms__panel workflow-forms__panel--designer" aria-modal="true" role="dialog">
      <div class="workflow-forms__editor-heading">
        <div>
          <h2 translate="no">{{ selected?.formKey }}</h2>
          <span>Revision {{ selected?.draftRevision }}</span>
        </div>
        <el-button data-testid="workflow-form-close-editor" v-on="{ click: editorControls.close }">
          {{ t('workflowForms.close') }}
        </el-button>
      </div>
      <p v-if="problem && problemIsRead" class="art-inline-alert" role="alert">{{ problem.title }}</p>
      <el-button data-testid="workflow-form-reload-editor" :disabled="busy" v-on="{ click: editorControls.reload }">{{ t('workflowForms.recoveryRetry') }}</el-button>
      <el-button v-if="selected && rowBlocked(selected)" :disabled="busy" v-on="{ click: editorControls.recover }">{{ t('workflowForms.recoveryUnlock') }}</el-button>
      <VForm3WorkflowDesigner v-if="selected && localDraft && catalog" :key="editorWindow"
        ref="designer"
        :schema="localDraft"
        :catalog="catalog"
        :disabled="busy || !editorReady || selected.statusKey === 'archived'"
        v-on="{ 'update:schema': editorControls.input, 'validation-error': editorControls.error }"
      />
      <div class="workflow-forms__decision-bar">
        <PermissionGate code="workflow.forms.update">
          <el-button type="primary" data-testid="workflow-form-save" :disabled="busy || !editorReady || !selected || rowBlocked(selected) || selected.statusKey === 'archived'" :loading="writing" v-on="{ click: editorControls.save }">
            {{ t('workflowForms.save') }}
          </el-button>
        </PermissionGate>
      </div>
    </aside>
    <el-dialog :model-value="!!recovery" :title="t('workflowForms.recoveryTitle')" :close-on-click-modal="false" v-on="{ 'update:modelValue': recoveryControls.close }">
      <p>{{ t('workflowForms.recoveryHint') }}</p><template #footer><el-button v-on="{ click: recoveryControls.cancel }">{{ t('workflowForms.close') }}</el-button><el-button type="warning" v-on="{ click: recoveryControls.confirm }">{{ t('workflowForms.recoveryUnlock') }}</el-button></template>
    </el-dialog>
    <el-dialog :model-value="!!deletion" :title="t('workflowForms.deleteVersion')" :close-on-click-modal="false" v-on="{ 'update:modelValue': deletionControls.close }">
      <p>{{ t('workflowForms.deleteVersionConfirm') }}</p><template #footer><el-button v-on="{ click: deletionControls.cancel }">{{ t('workflowForms.close') }}</el-button><el-button type="danger" v-on="{ click: deletionControls.confirm }">{{ t('workflowForms.deleteVersion') }}</el-button></template>
    </el-dialog>
  </section>
</template>

<style scoped>
.workflow-forms { display: grid; gap: 1rem; min-height: 0; }
.workflow-forms__layout { display: flex; flex: 1; gap: 12px; min-height: 0; }
.workflow-forms__list { flex: 1 1 0; min-width: 0; min-height: 0; }
.workflow-forms__list :deep(.el-card__body) { padding-top: 0; }
.workflow-forms__list :deep(.el-card__header) { padding: 12px 16px; }
.workflow-forms__list-header { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.workflow-forms__list-header h2 { margin: 0; font-size: 16px; font-weight: 600; }
.workflow-forms__versions-panel { flex: 0 0 300px; max-width: 360px; overflow: auto; }
.workflow-forms__versions-panel :deep(.el-card__header) { padding: 12px 16px; }
.workflow-forms__versions-panel :deep(.el-card__header) h2 { margin: 0; font-size: 16px; font-weight: 600; }
.workflow-forms__panel h2 { margin: 0; color: var(--el-text-color-primary); }
.workflow-forms__actions { display: flex; flex-wrap: wrap; gap: 0.35rem; }
.workflow-forms__versions { display: grid; gap: 0.75rem; margin: 0; padding: 0; list-style: none; }
.workflow-forms__versions li { display: flex; flex-wrap: wrap; align-items: center; gap: 0.75rem; }
.workflow-forms__empty { padding: 2.5rem 1rem; color: var(--el-text-color-secondary); text-align: center; }
.workflow-forms__panel { display: grid; gap: 1rem; padding: 1rem; border: 1px solid var(--el-border-color); border-top: 4px solid var(--el-color-primary); background: var(--el-bg-color); box-shadow: var(--el-box-shadow-light); }
.workflow-forms__panel--designer { position: fixed; z-index: 2000; inset: 4vh 3vw; overflow: auto; }
.workflow-forms__panel label { display: grid; gap: 0.4rem; }
.workflow-forms__panel input { min-height: 38px; padding: 0.5rem 0.7rem; border: 1px solid var(--el-border-color); border-radius: 8px; color: var(--el-text-color-primary); background: var(--el-bg-color); font: inherit; }
.workflow-forms__editor-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
.workflow-forms__editor-heading div { display: grid; gap: 0.25rem; }
.workflow-forms__editor-heading span { color: var(--el-text-color-secondary); font-family: var(--art-font-mono, monospace); }
.workflow-forms__decision-bar { display: flex; justify-content: flex-end; gap: 0.65rem; padding-top: 1rem; border-top: 1px solid var(--el-border-color-lighter); }
@media (max-width: 960px) {
  .workflow-forms__layout { flex-direction: column; }
  .workflow-forms__versions-panel { flex: none; max-width: none; width: 100%; }
}
@media (max-width: 720px) {
  .workflow-forms__panel--designer { inset: 1rem; }
}
</style>
