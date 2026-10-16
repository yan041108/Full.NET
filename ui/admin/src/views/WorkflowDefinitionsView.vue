<script setup lang="ts">
import { computed, inject, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import { ElButton, ElCard, ElDialog, ElDrawer, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails, type WorkflowDefinitionDraft, type WorkflowDefinitionResponse, type WorkflowDefinitionVersionResponse, type WorkflowNodeTypeCatalogResponse, type WorkflowFormSchema, type WorkflowFormField, type WorkflowFieldPolicies, type WorkflowSubmission } from '@fullnet/client-contracts';
import { createWorkflowDefinition, deleteWorkflowDefinitionVersion, getWorkflowDefinition, getWorkflowNodeTypeCatalog, publishWorkflowDefinition, setWorkflowDefinitionStatus, updateWorkflowDefinitionDraft } from '../api/workflow-definitions';
import { listWorkflowDefinitions, listWorkflowDefinitionVersions, getWorkflowStartForm, startWorkflowInstance } from '../api/workflow-runtime';
import { listWorkflowForms, type WorkflowFormResponse } from '../api/workflow-forms';
import { normalizeWorkflowFormKey, workflowFormIdValid, workflowFormKeyValid, workflowFormValueKey, validWorkflowForm, validWorkflowFormVersion } from '../api/workflow-form-guards';
import { validWorkflowDefinition, validWorkflowDefinitionVersion, validWorkflowNodeCatalog, workflowDefinitionDraftKey, workflowPublicationMatches, workflowDefinitionTrim, normalizeWorkflowTitleTemplate } from '../api/workflow-definition-guards';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import PermissionGate from '../components/PermissionGate.vue';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';
import WorkflowVue3Designer from '../workflow/WorkflowVue3Designer.vue';
import WorkflowFormRenderer from '../workflow/WorkflowFormRenderer.vue';
import { toWorkflowVue3Tree, type WorkflowVue3Node } from '../workflow/workflow-vue3-adapter';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
const sameDefinitionDraft = (a: WorkflowDefinitionDraft, b: WorkflowDefinitionDraft) => workflowDefinitionDraftKey(a) === workflowDefinitionDraftKey(b);
function createDefaultDefinitionDraft(): WorkflowDefinitionDraft { return { schemaVersion: 1, nodes: [{ nodeKey: 'start', nodeTypeKey: 'start', nodeSchemaVersion: 1, config: { nextNodeKeys: ['approval'] } }, { nodeKey: 'approval', nodeTypeKey: 'human.approval', nodeSchemaVersion: 1, config: { nodeName: '审批人', nextNodeKeys: ['end'] } }, { nodeKey: 'end', nodeTypeKey: 'end', nodeSchemaVersion: 1, config: { nextNodeKeys: [] } }] }; }
interface WorkflowVue3DesignerInstance { readDraft: () => WorkflowDefinitionDraft; }
type Kind = 'create' | 'save' | 'publish' | 'status' | 'delete' | 'start';
type Status = 'active' | 'disabled' | 'archived';
const permissions: Record<Kind, string> = { create: 'workflow.definitions.create', save: 'workflow.definitions.update', publish: 'workflow.definitions.publish', status: 'workflow.definitions.manage_status', delete: 'workflow.definitions.delete_version', start: 'workflow.instances.start' };
const { t, locale } = useAdminI18n(), session = useSessionStore(), route = inject(routeLocationKey, undefined), active = ref(false);
onMounted(() => { active.value = true; }); onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; });
const canRead = computed(() => { const user = session.currentUser; return active.value && (!route || route.name === 'workflow-definitions') && session.can('workflow.definitions.read') && !!user && (user.scope === 'host' && user.tenantId === null || workflowFormIdValid(user.tenantId) && user.scope === 'tenant:' + user.tenantId.replaceAll('-', '').toLowerCase()); });
const definitions = ref<WorkflowDefinitionResponse[]>([]), versions = ref<WorkflowDefinitionVersionResponse[]>([]), catalogReady = ref(false), editorReady = ref(false), versionsReady = ref(false);
const selectedDefinitionId = ref<string>(), selectedDefinition = ref<WorkflowDefinitionResponse>(), editingDefinition = ref<WorkflowDefinitionResponse>(), editorId = ref<string>(), localDraft = ref<WorkflowDefinitionDraft>(), catalog = ref<WorkflowNodeTypeCatalogResponse>();
const definitionKey = ref(''), creating = ref(false), createBlocked = ref(false), designer = ref<WorkflowVue3DesignerInstance>(), problem = ref<FullNetProblemDetails>(), problemIsRead = ref(true);
const listLoading = ref(false), editorLoading = ref(false), versionsLoading = ref(false), writing = ref(false);
const busy = computed(() => listLoading.value || editorLoading.value || versionsLoading.value || writing.value || formsLoading.value || gatewayLoading.value || startLoading.value || !!recovery.value);
const { tableMainRef, tableHeight, updateTableHeight, watchLoading } = useArtCrudTableLayout({ bottomOffset: 8 });
const owner = ref(0), createOwner = ref(0), editorOwner = ref(0), versionOwner = ref(0), editorWindow = ref(0);
const receipt = ref<{ kind: Kind; id?: string; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed' }>();
interface Block { kind: Kind; definitionId?: string; id?: string; revision: number; version: number; }
const blocks = ref(new Map<string, Block>()), floors = new Map<string, { revision: number; version: number; key: string; status: string; latest: string | null }>(), deleted = new Set<string>(), knownVersions = new Map<string, { parent: string; number: number; hash: string }>();
const scope = useAuthorizedViewScope(session, reset, async () => { if (canRead.value) await loadDefinitions(); });
type Lease = NonNullable<ReturnType<typeof scope.begin>>;
let listRequest: Lease | undefined, editorRequest: Lease | undefined, versionsRequest: Lease | undefined;
let write: { lease: Lease; block: Block; key: string } | undefined, applyingDraft = false;
const recovery = ref<{ key: string; block: Block; lease: Lease; ticket: number }>();
const deletion = ref<{ row: WorkflowDefinitionVersionResponse; ticket: number }>();
const workflowTree = ref<WorkflowVue3Node>(), businessTitleTemplate = ref(''), publishedForms = ref<WorkflowFormResponse[]>([]), publishFormVersionId = ref(''), gatewayFields = ref<readonly WorkflowFormField[]>([]);
const formsReady = ref(false), gatewayReady = ref(false), formsLoading = ref(false), gatewayLoading = ref(false), formsProblem = ref<FullNetProblemDetails>();
const enabledNodeTypes = computed(() => catalog.value?.nodeTypes.filter(node => node.designable && node.publishable && node.executable).map(node => node.nodeTypeKey) ?? []);
const canLoadPublishForms = computed(() => canRead.value && session.can('workflow.definitions.publish') && session.can('workflow.forms.read'));
let formsRequest: Lease | undefined, gatewayRequest: Lease | undefined, startRequest: Lease | undefined;
const selectedVersion = ref<WorkflowDefinitionVersionResponse>(), startSchema = ref<WorkflowFormSchema>(), startReady = ref(false), startLoading = ref(false), businessType = ref(''), businessId = ref(''), initialValues = ref<WorkflowSubmission>({}), startOwner = ref(0), startWindow = ref(0);
const emptySubmission: WorkflowSubmission = Object.freeze({});
let startIntent: { versionId: string; type: string; businessId: string; values: WorkflowSubmission; key: string } | undefined;
const startPolicies = computed<WorkflowFieldPolicies>(() => Object.fromEntries((startSchema.value?.sections.flatMap(section => section.fields) ?? []).map(field => [field.fieldKey, writing.value || !startReady.value ? 'readOnly' : field.required ? 'required' : 'editable'])));
const canSubmit = computed(() => startReady.value && !!workflowDefinitionTrim(businessType.value) && workflowDefinitionTrim(businessType.value).length <= 64 && !!workflowDefinitionTrim(businessId.value) && workflowDefinitionTrim(businessId.value).length <= 128 && (startSchema.value?.sections.flatMap(section => section.fields).every(field => !field.required || initialValues.value[field.fieldKey] !== undefined && initialValues.value[field.fieldKey] !== null && String(initialValues.value[field.fieldKey]).trim() !== '') ?? false));
watchLoading(busy);
watch(() => JSON.stringify([route?.name, route?.params, route?.query]), () => { scope.invalidate(); if (canRead.value) void loadDefinitions(); }, { flush: 'sync' });
function begin(permission: string): Lease | undefined { return canRead.value ? scope.begin(permission) : undefined; }
function reset(): void {
  clearEditorAux(); clearStartState(); workflowTree.value = undefined; businessTitleTemplate.value = '';
  listRequest = editorRequest = versionsRequest = undefined; write = undefined; recovery.value?.lease.cancel(); recovery.value = undefined; deletion.value = undefined;
  definitions.value = []; versions.value = []; editingDefinition.value = undefined; selectedDefinition.value = undefined; selectedDefinitionId.value = editorId.value = undefined; localDraft.value = undefined; catalog.value = undefined; creating.value = false; definitionKey.value = ''; createBlocked.value = false; receipt.value = undefined; problem.value = undefined;
  listLoading.value = editorLoading.value = versionsLoading.value = writing.value = false; catalogReady.value = editorReady.value = versionsReady.value = false; blocks.value.clear(); floors.clear(); deleted.clear(); knownVersions.clear(); owner.value++; createOwner.value++; editorOwner.value++; versionOwner.value++; startOwner.value++; editorWindow.value++;
}
const idKey = (id: string) => id.toLowerCase();
const copyDraft = (value: WorkflowDefinitionDraft): WorkflowDefinitionDraft => JSON.parse(JSON.stringify(value)) as WorkflowDefinitionDraft;
function failure(error: unknown, load = true): void { const value: FullNetProblemDetails = isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.workflow_failed', title: t(load ? 'workflowDefinitions.loadFailed' : 'workflowDefinitions.operationFailed') }; problem.value = value; problemIsRead.value = load; if (!load) showProblem(value, t('workflowDefinitions.operationFailed')); }
function assertDefinition(value: unknown, id?: string): asserts value is WorkflowDefinitionResponse {
  if (!validWorkflowDefinition(value) || id && idKey(value.id) !== idKey(id)) throw Error('client.invalid_workflow_definition');
  if (value.latestPublishedVersionId && deleted.has(idKey(value.latestPublishedVersionId))) throw Error('client.stale_workflow_definition');
  const floor = floors.get(idKey(value.id)), previous = definitions.value.find(row => idKey(row.id) === idKey(value.id));
  if (floor && (value.draftRevision < floor.revision || value.version < floor.version || value.definitionKey !== floor.key || value.version === floor.version && (value.statusKey !== floor.status || value.latestPublishedVersionId?.toLowerCase() !== floor.latest?.toLowerCase())) || previous && value.draftRevision === previous.draftRevision && (!sameDefinitionDraft(value.draft, previous.draft) || value.businessTitleTemplate !== previous.businessTitleTemplate)) throw Error('client.stale_workflow_definition');
}
function observe(value: WorkflowDefinitionResponse): void {
  floors.set(idKey(value.id), { revision: value.draftRevision, version: value.version, key: value.definitionKey, status: value.statusKey, latest: value.latestPublishedVersionId });
  for (const [key, block] of blocks.value) if (block.definitionId && idKey(block.definitionId) === idKey(value.id) && (block.kind === 'save' || block.kind === 'publish' ? value.draftRevision > block.revision : block.kind === 'status' && value.version > block.version)) blocks.value.delete(key);
}
function replaceDefinition(value: WorkflowDefinitionResponse): void { assertDefinition(value); observe(value); const index = definitions.value.findIndex(row => idKey(row.id) === idKey(value.id)); definitions.value = index < 0 ? [...definitions.value, value] : definitions.value.map((row, i) => i === index ? value : row); if (selectedDefinitionId.value && idKey(selectedDefinitionId.value) === idKey(value.id)) selectedDefinition.value = value; }
function current(row: WorkflowDefinitionResponse): boolean { return definitions.value.some(value => idKey(value.id) === idKey(row.id) && value.draftRevision === row.draftRevision && value.version === row.version && value.statusKey === row.statusKey && value.latestPublishedVersionId === row.latestPublishedVersionId); }
function blockKey(row: WorkflowDefinitionResponse): string | undefined { return [...blocks.value].find(([, block]) => block.kind !== 'start' && block.definitionId && idKey(block.definitionId) === idKey(row.id))?.[0]; }
function rowBlocked(row: WorkflowDefinitionResponse): boolean { return blockKey(row) !== undefined; }
async function loadDefinitions(): Promise<void> {
  if (!canRead.value || writing.value || recovery.value) return; listRequest?.cancel(); const request = begin('workflow.definitions.read'); if (!request) return; listRequest = request; listLoading.value = true; catalogReady.value = false; problem.value = undefined; owner.value++;
  try { const rows = await listWorkflowDefinitions(request.signal); if (!request.current() || listRequest !== request) return; if (!Array.isArray(rows) || new Set(rows.map(row => idKey(row.id))).size !== rows.length) throw Error('client.invalid_workflow_definition_list'); for (const row of rows) assertDefinition(row); for (const row of rows) observe(row); definitions.value = rows; catalogReady.value = true;
    if (editingDefinition.value) { const value = rows.find(row => idKey(row.id) === idKey(editingDefinition.value!.id)); if (!value || value.draftRevision !== editingDefinition.value.draftRevision || value.version !== editingDefinition.value.version) editorReady.value = false; }
  } catch (error) { if (request.current() && listRequest === request) failure(error); }
  finally { if (listRequest === request) { listRequest = undefined; listLoading.value = false; owner.value++; } request.finish(); void nextTick(updateTableHeight); }
}
function markUnknown(operation: NonNullable<typeof write>): void { if (operation.block.kind === 'create') createBlocked.value = true; else blocks.value.set(operation.key, operation.block); receipt.value = { kind: operation.block.kind, id: operation.block.id, outcome: 'unconfirmed' }; if (operation.block.kind !== 'start') catalogReady.value = false; if (operation.block.kind === 'save' || operation.block.kind === 'publish') editorReady.value = false; if (operation.block.kind === 'delete') versionsReady.value = false; }
function cancelWrite(kind: Kind): void { if (write?.block.kind !== kind) return; markUnknown(write); write.lease.cancel(); write = undefined; writing.value = false; owner.value++; }
function openCreate(): void { if (!canRead.value || !session.can(permissions.create) || busy.value) return; definitionKey.value = ''; creating.value = true; createOwner.value++; }
function closeCreate(): void { cancelWrite('create'); creating.value = false; definitionKey.value = ''; createOwner.value++; finishRecovery(false); }
watch(definitionKey, () => { createOwner.value++; cancelWrite('create'); finishRecovery(false); }, { flush: 'sync' });
watch([workflowTree, businessTitleTemplate, publishFormVersionId], () => { if (!applyingDraft) { editorOwner.value++; cancelWrite('save'); cancelWrite('publish'); finishRecovery(false); } }, { deep: true, flush: 'sync' });
function assignDraft(value: WorkflowDefinitionDraft): void { applyingDraft = true; try { const tree = toWorkflowVue3Tree(value); localDraft.value = copyDraft(value); workflowTree.value = tree; } finally { applyingDraft = false; } }
async function runWrite<T>(kind: Kind, row: WorkflowDefinitionResponse | undefined, invoke: (signal: AbortSignal) => Promise<T>, check: (value: T) => void, commit: (value: T) => void, versionId?: string): Promise<void> {
  if (!canRead.value || busy.value || kind !== 'create' && (!row || rowBlocked(row))) return; const lease = begin(permissions[kind]); if (!lease) return;
  const operation = { lease, key: kind === 'create' ? 'create' : versionId ? (kind === 'start' ? 'start:' : 'delete:') + idKey(versionId) : idKey(row!.id), block: { kind, id: versionId ?? row?.id, definitionId: row?.id, revision: row?.draftRevision ?? 0, version: row?.version ?? 0 } }; write = operation; writing.value = true; owner.value++; createOwner.value++; editorOwner.value++; versionOwner.value++; startOwner.value++; receipt.value = { kind, id: operation.block.id, outcome: 'processing' }; problem.value = undefined;
  try { const result = await invoke(lease.signal); if (!lease.current() || write !== operation) return; check(result);
    // 先固定成功回执并解除请求归属，再关闭窗口或应用草稿，避免自身 watcher 将已确认写入改成未知。
    write = undefined; writing.value = false; receipt.value = { kind, id: operation.block.id, outcome: 'succeeded' }; commit(result); owner.value++; createOwner.value++; editorOwner.value++; versionOwner.value++; startOwner.value++; showSuccess(t(kind === 'delete' ? 'workflowDefinitions.deleteVersionSuccess' : 'workflowDefinitions.recoveryCompleted'));
    if (kind !== 'save' && kind !== 'start') { catalogReady.value = false; if (kind === 'delete') await loadVersions(); await loadDefinitions(); }
  } catch (error) { if (lease.current() && write === operation) { const unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408; if (unknown) markUnknown(operation); else receipt.value = { kind, id: operation.block.id, outcome: 'failed' }; if (kind !== 'start') catalogReady.value = false; if (kind === 'save' || kind === 'publish') editorReady.value = false; if (kind === 'delete') versionsReady.value = false; failure(error, false); } }
  finally { if (write === operation) { write = undefined; writing.value = false; owner.value++; createOwner.value++; editorOwner.value++; versionOwner.value++; startOwner.value++; } lease.finish(); void nextTick(updateTableHeight); }
}
async function submitCreate(): Promise<void> {
  if (!creating.value || createBlocked.value) return; const key = normalizeWorkflowFormKey(definitionKey.value); if (!workflowFormKeyValid(key)) { showWarning(t('workflowDefinitions.operationFailed')); return; } const draft = createDefaultDefinitionDraft();
  await runWrite('create', undefined, signal => createWorkflowDefinition(key, draft, null, signal), value => { assertDefinition(value); if (floors.has(idKey(value.id)) || value.definitionKey !== key || value.draftRevision !== 1 || value.version !== 1 || value.latestPublishedVersionId !== null || value.statusKey !== 'active' || value.businessTitleTemplate !== null || !sameDefinitionDraft(value.draft, draft)) throw Error('client.invalid_workflow_definition_create'); }, value => { replaceDefinition(value); closeCreate(); });
}
async function openEditor(row: WorkflowDefinitionResponse): Promise<void> {
  if (!canRead.value || !session.can(permissions.save) || !catalogReady.value || busy.value || !current(row) || definitionStatus(row) === 'archived') return; closeEditor(); editorId.value = row.id; editorWindow.value++; await reloadEditor();
}
async function reloadEditor(): Promise<void> {
  if (!editorId.value || writing.value || recovery.value) return; const id = editorId.value; editorRequest?.cancel(); const request = begin(permissions.save); if (!request) return; editorRequest = request; editorLoading.value = true; editorReady.value = false; editorOwner.value++; problem.value = undefined;
  try { const [value, components] = await Promise.all([getWorkflowDefinition(id, request.signal), getWorkflowNodeTypeCatalog(request.signal)]); if (!request.current() || editorRequest !== request || editorId.value !== id) return; assertDefinition(value, id); if (!validWorkflowNodeCatalog(components) || value.draft.nodes.some(node => !components.nodeTypes.some(type => type.nodeTypeKey === node.nodeTypeKey && type.nodeSchemaVersion === node.nodeSchemaVersion && type.designable && type.publishable && type.executable))) throw Error('client.unsupported_workflow_node');
    const keep = editingDefinition.value?.id === value.id && editingDefinition.value.draftRevision === value.draftRevision && workflowTree.value !== undefined; if (!keep) { assignDraft(value.draft); applyingDraft = true; try { businessTitleTemplate.value = value.businessTitleTemplate ?? ''; } finally { applyingDraft = false; } editorWindow.value++; }
    replaceDefinition(value); editingDefinition.value = value; catalog.value = components; editorReady.value = true; await loadPublishForms();
  } catch (error) { if (request.current() && editorRequest === request) failure(error); }
  finally { if (editorRequest === request) { editorRequest = undefined; editorLoading.value = false; editorOwner.value++; } request.finish(); }
}
function closeEditor(): void { cancelWrite('save'); cancelWrite('publish'); editorRequest?.cancel(); editorRequest = undefined; editorLoading.value = false; editorReady.value = false; editorId.value = undefined; editingDefinition.value = undefined; localDraft.value = undefined; clearEditorAux(); applyingDraft = true; try { workflowTree.value = undefined; businessTitleTemplate.value = ''; } finally { applyingDraft = false; } catalog.value = undefined; editorOwner.value++; editorWindow.value++; finishRecovery(false); }
async function saveDefinitionDraft(): Promise<void> {
  const target = editingDefinition.value; if (!editorReady.value || !target || !editorId.value || !workflowTree.value || definitionStatus(target) === 'archived' || !current(target) || busy.value || !session.can(permissions.save)) return; let draft: WorkflowDefinitionDraft;
  try { if (!designer.value?.readDraft) throw Error('client.workflow_designer_not_ready'); draft = copyDraft(designer.value.readDraft()); } catch { showDesignerError('client.workflow_designer_not_ready'); return; }
  const title = businessTitleTemplate.value;
  await runWrite('save', { ...target }, signal => updateWorkflowDefinitionDraft(target.id, target.draftRevision, draft, title, signal), value => { assertDefinition(value, target.id); if (value.draftRevision !== target.draftRevision + 1 || value.businessTitleTemplate !== normalizeWorkflowTitleTemplate(title) || !sameDefinitionDraft(value.draft, draft)) throw Error('client.invalid_workflow_definition_save'); }, value => { replaceDefinition(value); editingDefinition.value = value; assignDraft(value.draft); applyingDraft = true; try { businessTitleTemplate.value = value.businessTitleTemplate ?? ''; } finally { applyingDraft = false; } editorReady.value = true; editorWindow.value++; });
}
async function publishDefinition(): Promise<void> {
  const target = editingDefinition.value, components = catalog.value, formVersionId = publishFormVersionId.value;
  if (!target || !components || !editorReady.value || !formsReady.value || !gatewayReady.value || !canLoadPublishForms.value || !current(target) || busy.value || definitionStatus(target) !== 'active' || !publishedForms.value.some(form => form.latestPublishedVersionId === formVersionId)) return;
  let draft: WorkflowDefinitionDraft; try { if (!designer.value?.readDraft) throw Error(); draft = copyDraft(designer.value.readDraft()); } catch { showDesignerError('client.workflow_designer_not_ready'); return; }
  if (!sameDefinitionDraft(draft, target.draft) || normalizeWorkflowTitleTemplate(businessTitleTemplate.value) !== target.businessTitleTemplate) { showWarning(t('workflowDefinitions.recoveryUnsaved')); return; }
  const fields = [...gatewayFields.value], publishedDefinition = (value: WorkflowDefinitionVersionResponse): WorkflowDefinitionResponse => ({ ...target, draftRevision: target.draftRevision + 1, version: target.version + 1, latestPublishedVersionId: value.id, updatedAtUtc: value.publishedAtUtc });
  await runWrite('publish', { ...target }, signal => publishWorkflowDefinition(target.id, target.draftRevision, formVersionId, signal), value => { if (!validWorkflowDefinitionVersion(value, target.id) || value.formVersionId.toLowerCase() !== formVersionId.toLowerCase() || knownVersions.has(idKey(value.id)) || deleted.has(idKey(value.id)) || value.businessTitleTemplate !== target.businessTitleTemplate || idKey(value.publishedById) !== idKey(session.currentUser!.id) || !workflowPublicationMatches(value, target.draft, components, fields)) throw Error('client.invalid_workflow_definition_publication'); assertDefinition(publishedDefinition(value)); }, value => { knownVersions.set(idKey(value.id), { parent: target.id, number: value.versionNumber, hash: value.contentHash }); const row = publishedDefinition(value); replaceDefinition(row); editingDefinition.value = row; if (selectedDefinitionId.value === target.id) versionsReady.value = false; });
}
async function changeDefinitionStatus(row: WorkflowDefinitionResponse, statusKey: Status): Promise<void> {
  if (!catalogReady.value || !current(row) || editorId.value || definitionStatus(row) === 'archived') return; const target = { ...row };
  await runWrite('status', target, signal => setWorkflowDefinitionStatus(target.id, statusKey, target.version, signal), value => { assertDefinition(value, target.id); if (value.statusKey !== statusKey || value.version <= target.version) throw Error('client.invalid_workflow_definition_status'); }, replaceDefinition);
}
async function openVersions(row: WorkflowDefinitionResponse): Promise<void> { if (!catalogReady.value || !current(row) || busy.value) return; closeVersions(); selectedDefinitionId.value = row.id; selectedDefinition.value = { ...row }; await loadVersions(); }
async function loadVersions(): Promise<void> {
  if (!selectedDefinitionId.value || writing.value || recovery.value) return; const id = selectedDefinitionId.value; versionsRequest?.cancel(); const request = begin('workflow.definitions.read'); if (!request) return; versionsRequest = request; versionsLoading.value = true; versionsReady.value = false; versionOwner.value++; problem.value = undefined;
  try { const [parent, rows] = await Promise.all([getWorkflowDefinition(id, request.signal), listWorkflowDefinitionVersions(id, request.signal)]); if (!request.current() || versionsRequest !== request || selectedDefinitionId.value !== id) return; assertDefinition(parent, id);
    if (!rows.every(row => validWorkflowDefinitionVersion(row, id)) || new Set(rows.map(row => idKey(row.id))).size !== rows.length || new Set(rows.map(row => row.versionNumber)).size !== rows.length || parent.latestPublishedVersionId && !rows.some(row => idKey(row.id) === idKey(parent.latestPublishedVersionId!))) throw Error('client.invalid_workflow_definition_versions');
    for (const row of rows) { const known = knownVersions.get(idKey(row.id)), old = versions.value.find(value => idKey(value.id) === idKey(row.id)); if (deleted.has(idKey(row.id)) || known && (idKey(known.parent) !== idKey(id) || known.number !== row.versionNumber || known.hash !== row.contentHash) || old && workflowFormValueKey(old) !== workflowFormValueKey(row)) throw Error('client.stale_workflow_definition_version'); }
    replaceDefinition(parent); const present = new Set(rows.map(row => idKey(row.id)));
    // 人工解锁只允许再次操作，不抹去既有版本事实；完整目录确认缺席后永久拒绝旧响应复活该标识。
    for (const [key, known] of knownVersions) if (idKey(known.parent) === idKey(id) && !present.has(key)) deleted.add(key);
    versions.value = rows; selectedDefinition.value = parent; for (const row of rows) knownVersions.set(idKey(row.id), { parent: id, number: row.versionNumber, hash: row.contentHash });
    // 删除非最新版本不会推进父记录版本；权威缺席确认后也必须记住不可复活的版本标识。
    for (const [key, block] of blocks.value) if (block.kind === 'delete' && block.definitionId === id && !rows.some(row => idKey(row.id) === idKey(block.id!))) { deleted.add(idKey(block.id!)); blocks.value.delete(key); } versionsReady.value = true;
  } catch (error) { if (request.current() && versionsRequest === request) failure(error); }
  finally { if (versionsRequest === request) { versionsRequest = undefined; versionsLoading.value = false; versionOwner.value++; } request.finish(); }
}
function closeVersions(): void { closeStart(); cancelWrite('delete'); versionsRequest?.cancel(); versionsRequest = undefined; versionsLoading.value = false; versionsReady.value = false; versions.value = []; selectedDefinitionId.value = undefined; selectedDefinition.value = undefined; deletion.value = undefined; versionOwner.value++; finishRecovery(false); }
async function removeDefinitionVersion(row: WorkflowDefinitionVersionResponse): Promise<void> {
  const parent = selectedDefinition.value; if (!versionsReady.value || !parent || !selectedDefinitionId.value || idKey(row.definitionId) !== idKey(parent.id) || !versions.value.some(value => workflowFormValueKey(value) === workflowFormValueKey(row))) return; const target = { ...row };
  await runWrite('delete', { ...parent }, signal => deleteWorkflowDefinitionVersion(target.id, signal), () => {}, () => { deleted.add(idKey(target.id)); versions.value = versions.value.filter(value => idKey(value.id) !== idKey(target.id)); versionsReady.value = false; }, target.id);
}
function recover(key: string): void {
  if (!canRead.value || busy.value) return; const block = key === 'create' && createBlocked.value && creating.value ? { kind: 'create' as const, revision: 0, version: 0 } : blocks.value.get(key); if (!block) return; const lease = begin(permissions[block.kind]); if (!lease) return; recovery.value = { key, block, lease, ticket: owner.value }; deletion.value = undefined;
}
function finishRecovery(yes: boolean): void { const pending = recovery.value; if (!pending) return; recovery.value = undefined; if (yes && pending.lease.current() && pending.ticket === owner.value) { if (pending.key === 'create' && creating.value) createBlocked.value = false; else if (blocks.value.get(pending.key) === pending.block) blocks.value.delete(pending.key); } pending.lease.finish(); owner.value++; createOwner.value++; editorOwner.value++; versionOwner.value++; startOwner.value++; }
function showDesignerError(code: string): void { problem.value = { status: 400, code, title: t('workflowDefinitions.operationFailed') }; problemIsRead.value = false; showWarning(t('workflowDefinitions.operationFailed')); }
function definitionStatus(value: WorkflowDefinitionResponse): Status { return value.statusKey as Status; }
function definitionStatusTagType(status: Status): 'success' | 'warning' | 'info' { return status === 'active' ? 'success' : status === 'disabled' ? 'warning' : 'info'; }
function formatDateTime(value: string | null | undefined): string { return formatAdminDateTime(locale.value, value); }
const controls = computed(() => { const ticket = owner.value, owned = () => canRead.value && ticket === owner.value; return { refresh: () => { if (owned()) void loadDefinitions(); }, create: () => { if (owned()) openCreate(); } }; });
const rowControls = computed(() => { const ticket = owner.value; return new Map(definitions.value.map(row => { const target = { ...row }, owned = () => canRead.value && ticket === owner.value && current(target), status = (value: Status) => { if (owned()) void changeDefinitionStatus(target, value); }; return [row.id, { edit: () => { if (owned()) void openEditor(target); }, versions: () => { if (owned()) void openVersions(target); }, disable: () => status('disabled'), enable: () => status('active'), archive: () => status('archived'), recover: () => { const key = blockKey(target); if (owned() && key) recover(key); } }]; })); });
const createControls = computed(() => { const ticket = createOwner.value, owned = () => canRead.value && creating.value && ticket === createOwner.value; return { input: (event: Event) => { if (owned() && !busy.value) definitionKey.value = (event.target as HTMLInputElement).value; }, close: () => { if (owned()) closeCreate(); }, submit: () => { if (owned()) void submitCreate(); }, recover: () => { if (owned()) recover('create'); } }; });
const editorControls = computed(() => { const ticket = editorOwner.value, window = editorWindow.value, owned = () => canRead.value && !!editorId.value && ticket === editorOwner.value && window === editorWindow.value; return { input: (value: WorkflowVue3Node) => { if (owned() && editorReady.value && !busy.value) workflowTree.value = JSON.parse(JSON.stringify(value)) as WorkflowVue3Node; }, title: (event: Event) => { if (owned() && !busy.value) businessTitleTemplate.value = (event.target as HTMLInputElement).value; }, form: (event: Event) => { if (owned() && !busy.value) { publishFormVersionId.value = (event.target as HTMLSelectElement).value; void loadGatewayFields(); } }, fields: () => { if (owned()) void loadGatewayFields(); }, forms: () => { if (owned()) void loadPublishForms(); }, error: (code: string) => { if (owned()) showDesignerError(code); }, close: () => { if (owned()) closeEditor(); }, reload: () => { if (owned()) void reloadEditor(); }, save: () => { if (owned()) void saveDefinitionDraft(); }, publish: () => { if (owned()) void publishDefinition(); }, recover: () => { if (owned() && editingDefinition.value) { const key = blockKey(editingDefinition.value); if (key) recover(key); } } }; });
const versionControls = computed(() => { const ticket = versionOwner.value, id = selectedDefinitionId.value, owned = () => canRead.value && ticket === versionOwner.value && id === selectedDefinitionId.value; return { close: () => { if (owned()) closeVersions(); }, reload: () => { if (owned()) void loadVersions(); }, rows: new Map(versions.value.map(row => { const target = { ...row }; return [row.id, { start: () => { if (owned()) void openStart(target); }, delete: () => { if (owned() && !busy.value && session.can(permissions.delete)) deletion.value = { row: target, ticket }; } }]; })) }; });
const recoveryControls = computed(() => { const pending = recovery.value, finish = (yes: boolean) => { if (pending && recovery.value === pending) finishRecovery(yes); }; return { confirm: () => finish(true), cancel: () => finish(false), close: (open: boolean) => { if (!open) finish(false); } }; });
const deletionControls = computed(() => { const pending = deletion.value, cancel = () => { if (deletion.value === pending) deletion.value = undefined; }; return { cancel, close: (open: boolean) => { if (!open) cancel(); }, confirm: () => { if (!pending || deletion.value !== pending) return; deletion.value = undefined; if (canRead.value && pending.ticket === versionOwner.value) void removeDefinitionVersion(pending.row); } }; });

function clearEditorAux(): void { formsRequest?.cancel(); gatewayRequest?.cancel(); formsRequest = gatewayRequest = undefined; formsLoading.value = gatewayLoading.value = false; formsReady.value = gatewayReady.value = false; formsProblem.value = undefined; publishedForms.value = []; gatewayFields.value = []; applyingDraft = true; try { publishFormVersionId.value = ''; } finally { applyingDraft = false; } }
async function loadPublishForms(): Promise<void> {
  if (!editorId.value || !editorReady.value || !canLoadPublishForms.value || writing.value || recovery.value) return; const id = editorId.value; formsRequest?.cancel(); const request = begin('workflow.forms.read'); if (!request) return; formsRequest = request; formsLoading.value = true; formsReady.value = false; formsProblem.value = undefined; editorOwner.value++;
  try { const rows = await listWorkflowForms(request.signal); if (!request.current() || formsRequest !== request || editorId.value !== id) return; if (!rows.every(validWorkflowForm) || new Set(rows.map(row => idKey(row.id))).size !== rows.length) throw Error(); publishedForms.value = rows.filter(row => row.statusKey === 'active' && row.latestPublishedVersionId !== null); formsReady.value = true;
    applyingDraft = true; try { if (!publishedForms.value.some(row => row.latestPublishedVersionId === publishFormVersionId.value)) publishFormVersionId.value = publishedForms.value[0]?.latestPublishedVersionId ?? ''; } finally { applyingDraft = false; } await loadGatewayFields();
  } catch (error) { if (request.current() && formsRequest === request) formsProblem.value = isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.workflow_failed', title: t('workflowDefinitions.loadFailed') }; }
  finally { if (formsRequest === request) { formsRequest = undefined; formsLoading.value = false; editorOwner.value++; } request.finish(); }
}
async function loadGatewayFields(): Promise<void> {
  gatewayRequest?.cancel(); gatewayRequest = undefined; gatewayReady.value = false; gatewayFields.value = []; gatewayLoading.value = false; const id = editorId.value, versionId = publishFormVersionId.value, source = publishedForms.value.find(row => row.latestPublishedVersionId === versionId);
  if (!id || !source || !formsReady.value || !canLoadPublishForms.value || writing.value || recovery.value) return; const request = begin('workflow.forms.read'); if (!request) return; gatewayRequest = request; gatewayLoading.value = true; editorOwner.value++;
  try { const result = await getWorkflowStartForm(versionId, request.signal); if (!request.current() || gatewayRequest !== request || editorId.value !== id || publishFormVersionId.value !== versionId) return; if (!validWorkflowFormVersion(result.version, source.id) || idKey(result.version.id) !== idKey(versionId) || workflowFormValueKey(result.schema) !== workflowFormValueKey(JSON.parse(result.version.formSchemaJson))) throw Error(); gatewayFields.value = result.schema.sections.flatMap(section => section.fields); gatewayReady.value = true; formsProblem.value = undefined;
  } catch (error) { if (request.current() && gatewayRequest === request) formsProblem.value = isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.workflow_failed', title: t('workflowDefinitions.loadFailed') }; }
  finally { if (gatewayRequest === request) { gatewayRequest = undefined; gatewayLoading.value = false; editorOwner.value++; } request.finish(); }
}
function clearStartState(): void { startRequest?.cancel(); startRequest = undefined; selectedVersion.value = undefined; startSchema.value = undefined; startReady.value = startLoading.value = false; businessType.value = businessId.value = ''; initialValues.value = {}; startIntent = undefined; startOwner.value++; startWindow.value++; }
function closeStart(): void { cancelWrite('start'); clearStartState(); finishRecovery(false); }
async function openStart(row: WorkflowDefinitionVersionResponse): Promise<void> { if (!canRead.value || busy.value || !versionsReady.value || !session.can(permissions.start) || !session.can('workflow.forms.read') || selectedDefinition.value?.statusKey !== 'active' || !versions.value.some(value => workflowFormValueKey(value) === workflowFormValueKey(row))) return; closeStart(); versionOwner.value++; selectedVersion.value = { ...row }; await reloadStart(); }
async function reloadStart(): Promise<void> {
  const target = selectedVersion.value; if (!target || writing.value || recovery.value || !session.can('workflow.forms.read')) return; startRequest?.cancel(); const request = begin(permissions.start); if (!request) return; startRequest = request; startLoading.value = true; startReady.value = false; startOwner.value++; problem.value = undefined;
  try { const [parent, result] = await Promise.all([getWorkflowDefinition(target.definitionId, request.signal), getWorkflowStartForm(target.formVersionId, request.signal)]); if (!request.current() || startRequest !== request || selectedVersion.value?.id !== target.id) return; assertDefinition(parent, target.definitionId); if (parent.statusKey !== 'active' || deleted.has(idKey(target.id)) || !validWorkflowFormVersion(result.version) || idKey(result.version.id) !== idKey(target.formVersionId) || workflowFormValueKey(result.schema) !== workflowFormValueKey(JSON.parse(result.version.formSchemaJson)) || startSchema.value && workflowFormValueKey(startSchema.value) !== workflowFormValueKey(result.schema)) throw Error('client.invalid_workflow_start_form'); replaceDefinition(parent); startSchema.value = result.schema; startReady.value = true;
  } catch (error) { if (request.current() && startRequest === request) failure(error); }
  finally { if (startRequest === request) { startRequest = undefined; startLoading.value = false; startOwner.value++; } request.finish(); }
}
watch([businessType, businessId, initialValues], () => { startOwner.value++; cancelWrite('start'); startIntent = undefined; finishRecovery(false); }, { flush: 'sync', deep: true });
async function submitStart(): Promise<void> {
  const target = selectedVersion.value, parent = selectedDefinition.value; if (!target || !parent || parent.statusKey !== 'active' || !current(parent) || !versionsReady.value || !versions.value.some(row => workflowFormValueKey(row) === workflowFormValueKey(target)) || !canSubmit.value || busy.value || blocks.value.has('start:' + idKey(target.id)) || !session.can('workflow.forms.read')) return;
  // 当前窗口同一输入重试复用原键；服务端只在既有活动实例竞争时提供重放，不能宣称全局恰好一次。
  startIntent ??= { versionId: target.id, type: workflowDefinitionTrim(businessType.value), businessId: workflowDefinitionTrim(businessId.value), values: JSON.parse(JSON.stringify(initialValues.value)) as WorkflowSubmission, key: globalThis.crypto.randomUUID() };
  const intent = startIntent;
  await runWrite('start', parent, signal => startWorkflowInstance(intent.versionId, intent.type, intent.businessId, intent.values, intent.key, signal), value => { if (!workflowFormIdValid(value.id) || idKey(value.definitionVersionId) !== idKey(target.id) || idKey(value.formVersionId) !== idKey(target.formVersionId) || value.businessType !== intent.type || value.businessId !== intent.businessId || !Number.isSafeInteger(value.revision) || value.revision < 1) throw Error('client.invalid_workflow_started_instance'); }, () => closeStart(), target.id);
}
const startControls = computed(() => { const ticket = startOwner.value, window = startWindow.value, id = selectedVersion.value?.id, owned = () => canRead.value && ticket === startOwner.value && window === startWindow.value && id === selectedVersion.value?.id; return { close: () => { if (owned()) closeStart(); }, reload: () => { if (owned()) void reloadStart(); }, submit: () => { if (owned()) void submitStart(); }, type: (event: Event) => { if (owned() && !busy.value) businessType.value = (event.target as HTMLInputElement).value; }, id: (event: Event) => { if (owned() && !busy.value) businessId.value = (event.target as HTMLInputElement).value; }, patch: (value: WorkflowSubmission) => { if (owned() && startReady.value && !writing.value) initialValues.value = JSON.parse(JSON.stringify(value)) as WorkflowSubmission; }, recover: () => { if (owned() && id) recover('start:' + idKey(id)); }, blocked: !!id && blocks.value.has('start:' + idKey(id)) }; });
</script>

<template>
  <section v-if="canRead" class="workflow-definitions art-page-stack art-full-height" :aria-busy="busy">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowDefinitions.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowDefinitions.caption') }}</p>

    <div v-if="problem && problemIsRead && !editorId && !selectedVersion" class="art-inline-alert" role="alert">{{ problem.title }}</div>
    <p v-if="receipt" role="status" data-testid="workflow-definition-receipt">{{ t('workflowDefinitions.recoveryAction.' + receipt.kind) }}：{{ t('workflowDefinitions.recovery.' + receipt.outcome) }}</p>
    <el-button data-testid="workflow-definition-refresh" :disabled="busy" v-on="{ click: controls.refresh }">{{ t('workflowDefinitions.recoveryRetry') }}</el-button>
    <div class="workflow-definitions__layout art-split-layout">
      <el-card class="workflow-definitions__list art-table-card" shadow="never">
        <template #header>
          <div class="workflow-definitions__list-header">
            <h2>{{ t('workflowDefinitions.listTitle') }}</h2>
            <PermissionGate code="workflow.definitions.create">
              <el-button type="primary" size="small" data-testid="workflow-definition-create" :disabled="busy" v-on="{ click: controls.create }">
                {{ t('workflowDefinitions.create') }}
              </el-button>
            </PermissionGate>
          </div>
        </template>

        <div ref="tableMainRef" class="art-crud-table-main">
        <el-table
          v-loading="listLoading"
          :data="definitions"
          :height="tableHeight"
          class="workflow-definitions__table"
          highlight-current-row
          row-key="id"
          empty-text=""
          :current-row-key="selectedDefinitionId"
        >
          <el-table-column
            :label="t('workflowDefinitions.definitionKey')"
            min-width="160"
            show-overflow-tooltip
          >
            <template #default="{ row }">
              <code translate="no">{{ row.definitionKey }}</code>
            </template>
          </el-table-column>
          <el-table-column :label="t('workflowDefinitions.status')" width="96" align="center">
            <template #default="{ row }">
              <el-tag size="small" :type="definitionStatusTagType(definitionStatus(row as WorkflowDefinitionResponse))">
                {{ t(`workflowDefinitions.statusLabel.${definitionStatus(row as WorkflowDefinitionResponse)}`) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column
            :label="t('workflowDefinitions.latestVersion')"
            min-width="120"
            show-overflow-tooltip
          >
            <template #default="{ row }">
              <span translate="no">{{ row.latestPublishedVersionId ?? '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('workflowDefinitions.updatedAt')" width="168">
            <template #default="{ row }">
              <span translate="no">{{ formatDateTime(row.updatedAtUtc ?? row.createdAtUtc) }}</span>
            </template>
          </el-table-column>
          <el-table-column
            :label="t('workflowDefinitions.actions')"
            min-width="320"
            fixed="right"
          >
            <template #default="{ row }">
              <div class="workflow-definitions__actions">
                <PermissionGate code="workflow.definitions.update">
                  <el-button
                    size="small"
                    data-testid="workflow-definition-edit"
                    :disabled="busy || !catalogReady || definitionStatus(row as WorkflowDefinitionResponse) === 'archived'"
                    v-on="{ click: rowControls.get(row.id)?.edit }"
                  >{{ t('workflowDefinitions.edit') }}</el-button>
                </PermissionGate>
                <el-button
                  size="small"
                  data-testid="workflow-definition-versions"
                  :disabled="busy"
                  v-on="{ click: rowControls.get(row.id)?.versions }"
                >{{ t('workflowDefinitions.versions') }}</el-button>
                <PermissionGate v-if="definitionStatus(row as WorkflowDefinitionResponse) === 'active'" code="workflow.definitions.manage_status">
                  <el-button
                    size="small"
                    data-testid="workflow-definition-disable"
                    :disabled="busy || !catalogReady || !!editorId || rowBlocked(row)"
                    v-on="{ click: rowControls.get(row.id)?.disable }"
                  >{{ t('workflowDefinitions.disable') }}</el-button>
                </PermissionGate>
                <PermissionGate v-if="definitionStatus(row as WorkflowDefinitionResponse) === 'disabled'" code="workflow.definitions.manage_status">
                  <el-button
                    size="small"
                    data-testid="workflow-definition-enable"
                    :disabled="busy || !catalogReady || !!editorId || rowBlocked(row)"
                    v-on="{ click: rowControls.get(row.id)?.enable }"
                  >{{ t('workflowDefinitions.enable') }}</el-button>
                </PermissionGate>
                <PermissionGate v-if="definitionStatus(row as WorkflowDefinitionResponse) !== 'archived'" code="workflow.definitions.manage_status">
                  <el-button
                    size="small"
                    data-testid="workflow-definition-archive"
                    :disabled="busy || !catalogReady || !!editorId || rowBlocked(row)"
                    v-on="{ click: rowControls.get(row.id)?.archive }"
                  >{{ t('workflowDefinitions.archive') }}</el-button>
                </PermissionGate>
                <PermissionGate v-if="blockKey(row)" :code="permissions[blocks.get(blockKey(row)!)!.kind]"><el-button :disabled="busy" v-on="{ click: rowControls.get(row.id)?.recover }">{{ t('workflowDefinitions.recoveryUnlock') }}</el-button></PermissionGate>
              </div>
            </template>
          </el-table-column>
          <template #empty>
            <p v-if="!listLoading" class="workflow-definitions__empty">{{ t('workflowDefinitions.empty') }}</p>
          </template>
        </el-table>
        </div>
      </el-card>

      <el-card class="workflow-definitions__versions-panel art-form-card" shadow="never">
        <template #header>
          <h2>{{ t('workflowDefinitions.versionsTitle') }}</h2>
        </template>
        <template v-if="selectedDefinitionId"><el-button data-testid="workflow-definition-reload-versions" :disabled="busy" v-on="{ click: versionControls.reload }">{{ t('workflowDefinitions.recoveryRetry') }}</el-button><el-button v-on="{ click: versionControls.close }">{{ t('workflowDefinitions.close') }}</el-button></template>
        <p v-if="!selectedDefinitionId" class="workflow-definitions__empty workflow-definitions__versions-hint">
          {{ t('workflowDefinitions.selectVersions') }}
        </p>
        <div v-else-if="versions.length === 0" class="workflow-definitions__empty">
          {{ t('workflowDefinitions.noVersions') }}
        </div>
        <ul v-else class="workflow-definitions__versions">
          <li v-for="version in versions" :key="version.id">
            <span>{{ t('workflowDefinitions.version') }} {{ version.versionNumber }}</span>
            <time :datetime="version.publishedAtUtc">{{ formatDateTime(version.publishedAtUtc) }}</time>
            <div class="workflow-definitions__version-actions">
              <PermissionGate
                v-if="selectedDefinition && definitionStatus(selectedDefinition) === 'active' && session.can('workflow.forms.read')"
                code="workflow.instances.start"
              >
                <el-button
                  type="primary"
                  plain
                  size="small"
                  data-testid="workflow-definition-start"
                  :disabled="busy || !versionsReady || !!selectedDefinition && rowBlocked(selectedDefinition)"
                  v-on="{ click: versionControls.rows.get(version.id)?.start }"
                >{{ t('workflowDefinitions.start') }}</el-button>
              </PermissionGate>
              <PermissionGate code="workflow.definitions.delete_version">
                <el-button
                  type="danger"
                  plain
                  size="small"
                  data-testid="workflow-definition-delete-version"
                  :disabled="busy || !versionsReady || !!selectedDefinition && rowBlocked(selectedDefinition)"
                  v-on="{ click: versionControls.rows.get(version.id)?.delete }"
                >{{ t('workflowDefinitions.deleteVersion') }}</el-button>
              </PermissionGate>
            </div>
          </li>
        </ul>
      </el-card>
    </div>

    <aside v-if="creating" class="workflow-definitions__panel" aria-modal="true" role="dialog">
      <h2>{{ t('workflowDefinitions.createTitle') }}</h2>
      <label>
        <span>{{ t('workflowDefinitions.definitionKey') }}</span>
        <input :value="definitionKey" :disabled="busy" data-testid="workflow-definition-key" autocomplete="off" required v-on="{ input: createControls.input }" />
      </label>
      <el-button v-if="createBlocked" :disabled="busy" v-on="{ click: createControls.recover }">{{ t('workflowDefinitions.recoveryUnlock') }}</el-button>
      <div class="workflow-definitions__decision-bar">
        <el-button v-on="{ click: createControls.close }">{{ t('workflowDefinitions.close') }}</el-button>
        <el-button type="primary" data-testid="workflow-definition-create-submit" :disabled="busy || createBlocked || !workflowFormKeyValid(normalizeWorkflowFormKey(definitionKey))" :loading="writing" v-on="{ click: createControls.submit }">
          {{ t('workflowDefinitions.create') }}
        </el-button>
      </div>
    </aside>

    <aside
      v-if="editorId"
      class="workflow-definitions__panel workflow-definitions__panel--designer"
      aria-modal="true"
      role="dialog"
    >
      <div class="workflow-definitions__editor-heading">
        <div>
          <h2 translate="no">{{ editingDefinition?.definitionKey }}</h2>
          <span>Revision {{ editingDefinition?.draftRevision }}</span>
        </div>
        <el-button data-testid="workflow-definition-close-editor" v-on="{ click: editorControls.close }">
          {{ t('workflowDefinitions.close') }}
        </el-button>
      </div>
      <p v-if="problem && problemIsRead" class="art-inline-alert" role="alert">{{ problem.title }}</p>
      <el-button data-testid="workflow-definition-reload-editor" :disabled="busy" v-on="{ click: editorControls.reload }">{{ t('workflowDefinitions.recoveryRetry') }}</el-button>
      <el-button v-if="editingDefinition && rowBlocked(editingDefinition)" :disabled="busy" v-on="{ click: editorControls.recover }">{{ t('workflowDefinitions.recoveryUnlock') }}</el-button>
      <label class="workflow-definitions__title-template">
        <span>{{ t('workflowDefinitions.businessTitleTemplate') }}</span>
        <input
          :value="businessTitleTemplate" :disabled="busy || !editorReady" v-on="{ input: editorControls.title }"
          data-testid="workflow-definition-title-template"
          maxlength="256"
          :placeholder="t('workflowDefinitions.businessTitleTemplateHint')"
        />
      </label>
      <WorkflowVue3Designer v-if="workflowTree" :key="editorWindow"
        ref="designer"
        :model-value="workflowTree"
        :disabled="busy || !editorReady || editingDefinition?.statusKey === 'archived'"
        :enabled-node-types="enabledNodeTypes"
        :gateway-fields="gatewayFields"
        v-on="{ 'update:modelValue': editorControls.input, 'validation-error': editorControls.error }"
      />
      <div v-if="canLoadPublishForms" class="workflow-definitions__publish-row">
        <p v-if="formsProblem" role="alert">{{ formsProblem.title }}</p>
        <el-button :disabled="busy" data-testid="workflow-definition-reload-forms" v-on="{ click: editorControls.forms }">{{ t('workflowDefinitions.recoveryRetry') }}</el-button>
        <el-button v-if="formsReady && !gatewayReady && publishFormVersionId" :disabled="busy" v-on="{ click: editorControls.fields }">{{ t('workflowDefinitions.recoveryRetry') }}</el-button>
        <label>
          <span>{{ t('workflowDefinitions.formVersion') }}</span>
          <select :value="publishFormVersionId" :disabled="busy || !formsReady" data-testid="workflow-definition-form-version" required v-on="{ change: editorControls.form }">
            <option value="">{{ t('workflowDefinitions.selectFormVersion') }}</option>
            <option
              v-for="form in publishedForms"
              :key="form.latestPublishedVersionId ?? form.id"
              :value="form.latestPublishedVersionId ?? ''"
            >{{ form.formKey }}</option>
          </select>
        </label>
      </div>
      <div class="workflow-definitions__decision-bar">
        <PermissionGate code="workflow.definitions.update">
          <el-button type="primary" data-testid="workflow-definition-save" :disabled="busy || !editorReady || !editingDefinition || rowBlocked(editingDefinition) || editingDefinition.statusKey === 'archived'" :loading="writing" v-on="{ click: editorControls.save }">
            {{ t('workflowDefinitions.save') }}
          </el-button>
        </PermissionGate>
        <PermissionGate v-if="canLoadPublishForms" code="workflow.definitions.publish">
          <el-button type="success" data-testid="workflow-definition-publish" :loading="writing" :disabled="busy || !editorReady || !formsReady || !gatewayReady || !editingDefinition || rowBlocked(editingDefinition) || editingDefinition.statusKey !== 'active'" v-on="{ click: editorControls.publish }">
            {{ t('workflowDefinitions.publish') }}
          </el-button>
        </PermissionGate>
      </div>
    </aside>

    <el-drawer :key="startWindow"
      :model-value="selectedVersion !== undefined"
      :title="t('workflowDefinitions.startTitle')"
      size="min(680px, 94vw)"
      v-on="{ close: startControls.close }"
    >
      <template v-if="selectedVersion">
        <p v-if="problem && problemIsRead" role="alert">{{ problem.title }}</p>
        <el-button data-testid="workflow-start-reload" :disabled="busy" v-on="{ click: startControls.reload }">{{ t('workflowDefinitions.recoveryRetry') }}</el-button>
        <el-button v-if="startControls.blocked" :disabled="busy" v-on="{ click: startControls.recover }">{{ t('workflowDefinitions.recoveryUnlock') }}</el-button>
        <div class="workflow-definitions__business">
          <label>
            <span>{{ t('workflowDefinitions.businessType') }}</span>
            <input :value="businessType" :disabled="busy || !startReady" data-testid="workflow-business-type" required maxlength="64" v-on="{ input: startControls.type }" />
          </label>
          <label>
            <span>{{ t('workflowDefinitions.businessId') }}</span>
            <input :value="businessId" :disabled="busy || !startReady" data-testid="workflow-business-id" required maxlength="128" v-on="{ input: startControls.id }" />
          </label>
        </div>
        <WorkflowFormRenderer v-if="startSchema" :key="startWindow"
          :schema="startSchema"
          :submission="emptySubmission"
          :field-policies="startPolicies"
          v-on="{ 'update:patch': startControls.patch }"
        />
        <div class="workflow-definitions__decision-bar">
          <el-button v-on="{ click: startControls.close }">
            {{ t('workflowDefinitions.close') }}
          </el-button>
          <el-button
            type="primary"
            data-testid="workflow-start-submit"
            :loading="writing"
            :disabled="busy || !canSubmit || startControls.blocked"
            v-on="{ click: startControls.submit }"
          >{{ t('workflowDefinitions.submit') }}</el-button>
        </div>
      </template>
    </el-drawer>
    <el-dialog :model-value="!!recovery" :title="t('workflowDefinitions.recoveryTitle')" :close-on-click-modal="false" v-on="{ 'update:modelValue': recoveryControls.close }">
      <p>{{ t('workflowDefinitions.recoveryHint') }}</p><template #footer><el-button v-on="{ click: recoveryControls.cancel }">{{ t('workflowDefinitions.close') }}</el-button><el-button type="warning" v-on="{ click: recoveryControls.confirm }">{{ t('workflowDefinitions.recoveryUnlock') }}</el-button></template>
    </el-dialog>
    <el-dialog :model-value="!!deletion" :title="t('workflowDefinitions.deleteVersion')" :close-on-click-modal="false" v-on="{ 'update:modelValue': deletionControls.close }">
      <p>{{ t('workflowDefinitions.deleteVersionConfirm') }}</p><template #footer><el-button v-on="{ click: deletionControls.cancel }">{{ t('workflowDefinitions.close') }}</el-button><el-button type="danger" v-on="{ click: deletionControls.confirm }">{{ t('workflowDefinitions.deleteVersion') }}</el-button></template>
    </el-dialog>
  </section>
</template>

<style scoped>
.workflow-definitions { display: grid; gap: 1rem; min-height: 0; }
.workflow-definitions__layout {
  display: flex;
  flex: 1;
  gap: 12px;
  min-height: 0;
}
.workflow-definitions__list {
  flex: 1 1 0;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}
.workflow-definitions__list :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}
.workflow-definitions__list :deep(.el-card__header) {
  padding: 12px 16px;
}
.workflow-definitions__list-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.workflow-definitions__list-header h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}
.workflow-definitions__table {
  flex: 1;
  min-height: 200px;
}
.workflow-definitions__versions-panel {
  flex: 0 0 320px;
  min-width: 280px;
  max-width: 380px;
  overflow: auto;
}
.workflow-definitions__versions-panel :deep(.el-card__header) {
  padding: 12px 16px;
}
.workflow-definitions__versions-panel :deep(.el-card__header) h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}
.workflow-definitions__versions-panel :deep(.el-card__body) {
  padding: 12px 16px 16px;
}
.workflow-definitions__versions-hint {
  margin: 12px 0;
}
.workflow-definitions__version-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.workflow-definitions code { font-size: 0.85rem; }
.workflow-definitions__actions { display: flex; flex-wrap: wrap; gap: 0.35rem; }
.workflow-definitions__empty { padding: 2.5rem 1rem; color: var(--el-text-color-secondary); text-align: center; }
.workflow-definitions__panel { display: grid; gap: 1rem; padding: 1rem; border: 1px solid var(--el-border-color); border-top: 4px solid var(--el-color-primary); background: var(--el-bg-color); box-shadow: var(--el-box-shadow-light); }
.workflow-definitions__panel--designer { position: fixed; z-index: 2000; inset: 3vh 2vw; overflow: auto; }
.workflow-definitions__panel h2 { margin: 0; }
.workflow-definitions__panel label { display: grid; gap: 0.4rem; }
.workflow-definitions__panel input, .workflow-definitions__panel select { min-height: 38px; padding: 0.5rem 0.7rem; border: 1px solid var(--el-border-color); border-radius: 8px; color: var(--el-text-color-primary); background: var(--el-bg-color); font: inherit; }
.workflow-definitions__editor-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
.workflow-definitions__editor-heading div { display: grid; gap: 0.25rem; }
.workflow-definitions__editor-heading span { color: var(--el-text-color-secondary); font-family: var(--art-font-mono, monospace); }
.workflow-definitions__publish-row { display: grid; grid-template-columns: minmax(18rem, 28rem); justify-content: end; }
.workflow-definitions__versions { display: grid; gap: 0.6rem; margin: 0; padding: 0; list-style: none; }
.workflow-definitions__versions li { display: grid; grid-template-columns: minmax(7rem, 1fr) minmax(12rem, 2fr) auto; align-items: center; gap: 0.75rem; padding: 0.7rem; border: 1px solid var(--el-border-color-lighter); border-radius: 10px; }
.workflow-definitions__business { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem; margin-bottom: 1rem; }
.workflow-definitions__business label { display: grid; gap: 0.4rem; color: var(--el-text-color-regular); font-weight: 650; }
.workflow-definitions__business input { min-height: 38px; padding: 0.5rem 0.7rem; border: 1px solid var(--el-border-color); border-radius: 8px; color: var(--el-text-color-primary); background: var(--el-bg-color); font: inherit; }
.workflow-definitions__decision-bar { display: flex; justify-content: flex-end; gap: 0.65rem; margin-top: 1.25rem; padding-top: 1rem; border-top: 1px solid var(--el-border-color-lighter); }
@media (max-width: 960px) {
  .workflow-definitions__layout { flex-direction: column; }
  .workflow-definitions__versions-panel {
    flex: none;
    max-width: none;
    width: 100%;
  }
}
@media (max-width: 720px) {
  .workflow-definitions__panel--designer { inset: 1rem; }
  .workflow-definitions__business { grid-template-columns: 1fr; }
  .workflow-definitions__versions li { grid-template-columns: 1fr; }
}
</style>
