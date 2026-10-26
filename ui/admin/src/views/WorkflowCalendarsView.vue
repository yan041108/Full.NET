<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDrawer, ElInput, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listWorkflowWorkCalendars, getWorkflowWorkCalendar, createWorkflowWorkCalendar, updateWorkflowWorkCalendar, publishWorkflowWorkCalendar,
  listWorkflowWorkCalendarVersions, getWorkflowWorkCalendarVersion, type WorkflowWorkCalendarResponse, type WorkflowWorkCalendarSummary,
  type WorkflowWorkCalendarVersionSummary, type WorkflowWorkCalendarVersionResponse } from '../api/workflow-calendars';
import { defaultWorkflowWorkCalendar, readWorkflowWorkCalendar } from '../workflow/workflow-work-calendar';
import WorkflowWorkCalendarEditor from '../workflow/WorkflowWorkCalendarEditor.vue';
const session = useSessionStore(); const { t } = useAdminI18n();
const rows = ref<WorkflowWorkCalendarSummary[]>([]); const loading = ref(false); const problem = ref('');
const cursor = ref<string>(); const nextCursor = ref<string | null>(null); const back = ref<Array<string | undefined>>([]);
const drawer = ref(false); const creating = ref(false); const selected = ref<WorkflowWorkCalendarResponse>();
const draft = ref(defaultWorkflowWorkCalendar()); const name = ref(''); const key = ref(''); const savedDraft = ref('');
const acting = ref(false); const detailLoading = ref(false); const mustRefresh = ref(false);
const versions = ref<WorkflowWorkCalendarVersionSummary[]>([]); const beforeVersion = ref<number | null>(null); const versionsLoading = ref(false);
const historical = ref<WorkflowWorkCalendarVersionResponse>(); const receipt = ref<WorkflowWorkCalendarVersionResponse>();
let epoch = 0; let listGeneration = 0; let detailGeneration = 0; let versionGeneration = 0; let disposed = false;
const readable = computed(() => !session.switching && session.can('workflow.calendars.read'));
const editable = computed(() => !historical.value && !mustRefresh.value && !acting.value && !detailLoading.value && !session.switching && session.can(creating.value ? 'workflow.calendars.create' : 'workflow.calendars.update'));
const validDraft = computed(() => { try { return readWorkflowWorkCalendar(draft.value); } catch { return undefined; } });
const dirty = computed(() => creating.value || name.value !== selected.value?.displayName || JSON.stringify(validDraft.value) !== savedDraft.value);
const canSave = computed(() => editable.value && validDraft.value && dirty.value && name.value.trim().length > 0 && name.value.trim().length <= 200 && /^[A-Za-z][A-Za-z0-9_.-]{2,127}$/u.test(key.value));
const canPublish = computed(() => !creating.value && selected.value && !historical.value && !mustRefresh.value && !dirty.value && !acting.value && !detailLoading.value && !session.switching && session.can('workflow.calendars.publish'));
const message = (error: unknown, fallback: string) => typeof error === 'object' && error !== null && 'title' in error && typeof error.title === 'string' && error.title.length <= 500 ? error.title : fallback;
const current = (expectedEpoch: number, generation: number) => !disposed && expectedEpoch === epoch && generation === detailGeneration;
function close(): void {
  detailGeneration++; versionGeneration++; drawer.value = false; creating.value = false; selected.value = undefined;
  historical.value = undefined; receipt.value = undefined; acting.value = false; detailLoading.value = false; versionsLoading.value = false; mustRefresh.value = false;
  key.value = ''; name.value = ''; draft.value = defaultWorkflowWorkCalendar(); savedDraft.value = ''; versions.value = []; beforeVersion.value = null;
}
function applyRecord(value: WorkflowWorkCalendarResponse): void {
  selected.value = value; creating.value = false; key.value = value.calendarKey; name.value = value.displayName;
  draft.value = readWorkflowWorkCalendar(value.draft); savedDraft.value = JSON.stringify(draft.value); mustRefresh.value = false;
}
async function load(afterId?: string): Promise<boolean> {
  if (!readable.value) return false; const expectedEpoch = epoch; const generation = ++listGeneration; loading.value = true;
  try { const result = await listWorkflowWorkCalendars(afterId); if (disposed || expectedEpoch !== epoch || generation !== listGeneration) return false;
    rows.value = [...result.items]; nextCursor.value = result.nextAfterId; cursor.value = afterId; return true;
  } catch (error) { if (!disposed && expectedEpoch === epoch && generation === listGeneration) problem.value = message(error, t('workCalendars.loadFailed')); return false; }
  finally { if (expectedEpoch === epoch && generation === listGeneration) loading.value = false; }
}
async function next(): Promise<void> { const value = nextCursor.value; const previous = cursor.value; if (value && !loading.value && await load(value)) back.value.push(previous); }
async function previous(): Promise<void> { if (!back.value.length || loading.value) return; const value = back.value.at(-1); if (await load(value)) back.value.pop(); }
function create(): void {
  if (!session.can('workflow.calendars.create') || session.switching) return; close(); creating.value = true; drawer.value = true; problem.value = '';
}
async function loadVersions(id: string, before?: number): Promise<void> {
  const expectedEpoch = epoch; const generation = ++versionGeneration; const detail = detailGeneration; versionsLoading.value = true;
  try { const value = await listWorkflowWorkCalendarVersions(id, before); if (!current(expectedEpoch, detail) || generation !== versionGeneration || selected.value?.id !== id) return;
    versions.value = [...value.items]; beforeVersion.value = value.nextBeforeVersionNumber;
  } catch (error) { if (current(expectedEpoch, detail) && generation === versionGeneration) problem.value = message(error, t('workCalendars.loadFailed')); }
  finally { if (current(expectedEpoch, detail) && generation === versionGeneration) versionsLoading.value = false; }
}
async function open(id: string): Promise<void> {
  if (!readable.value) return; close(); drawer.value = true; detailLoading.value = true; problem.value = ''; const expectedEpoch = epoch; const generation = detailGeneration;
  try { const value = await getWorkflowWorkCalendar(id); if (!current(expectedEpoch, generation)) return; applyRecord(value); void loadVersions(id); }
  catch (error) { if (current(expectedEpoch, generation)) { mustRefresh.value = true; problem.value = message(error, t('workCalendars.loadFailed')); } }
  finally { if (current(expectedEpoch, generation)) detailLoading.value = false; }
}
async function save(): Promise<void> {
  if (!canSave.value || !validDraft.value) return; const expectedEpoch = epoch; const generation = detailGeneration; const existing = selected.value; acting.value = true; problem.value = '';
  try { const value = creating.value ? await createWorkflowWorkCalendar({ calendarKey: key.value, displayName: name.value, draft: validDraft.value })
      : await updateWorkflowWorkCalendar(existing!.id, { expectedRevision: existing!.revision, displayName: name.value, draft: validDraft.value });
    if (!current(expectedEpoch, generation)) return; applyRecord(value); back.value = []; void load(); void loadVersions(value.id);
  } catch (error) { if (current(expectedEpoch, generation)) problem.value = message(error, t('workCalendars.saveFailed')); }
  finally { if (current(expectedEpoch, generation)) acting.value = false; }
}
async function publish(): Promise<void> {
  const value = selected.value; if (!canPublish.value || !value) return; const expectedEpoch = epoch; const generation = detailGeneration; acting.value = true; problem.value = '';
  try {
    const published = await publishWorkflowWorkCalendar(value.id, { expectedRevision: value.revision }); if (!current(expectedEpoch, generation)) return;
    receipt.value = published; mustRefresh.value = true;
    try { const updated = await getWorkflowWorkCalendar(value.id); if (!current(expectedEpoch, generation)) return; applyRecord(updated); void load(cursor.value); void loadVersions(value.id); }
    catch { if (current(expectedEpoch, generation)) problem.value = t('workCalendars.publishedRefreshFailed'); }
  } catch (error) { if (current(expectedEpoch, generation)) { mustRefresh.value = true; problem.value = message(error, t('workCalendars.publishFailed')); } }
  finally { if (current(expectedEpoch, generation)) acting.value = false; }
}
async function viewVersion(id: string): Promise<void> {
  const parent = selected.value?.id; if (!parent || acting.value) return; const expectedEpoch = epoch; const generation = detailGeneration; acting.value = true;
  try { const value = await getWorkflowWorkCalendarVersion(id); if (!current(expectedEpoch, generation)) return;
    if (value.calendarId !== parent) throw new TypeError('client.invalid_workflow_work_calendar'); historical.value = value;
  } catch (error) { if (current(expectedEpoch, generation)) problem.value = message(error, t('workCalendars.loadFailed')); }
  finally { if (current(expectedEpoch, generation)) acting.value = false; }
}
watch(() => JSON.stringify([session.currentUser?.id, session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.scope, session.switching,
  session.can('workflow.calendars.read'), session.can('workflow.calendars.create'), session.can('workflow.calendars.update'), session.can('workflow.calendars.publish')]), () => {
  epoch++; listGeneration++; close(); rows.value = []; loading.value = false; problem.value = ''; cursor.value = undefined; nextCursor.value = null; back.value = [];
  if (readable.value) void load();
}, { flush: 'sync' });
onMounted(() => { if (readable.value) void load(); }); onBeforeUnmount(() => { disposed = true; epoch++; listGeneration++; close(); });
</script>
<template>
  <section class="work-calendar-page">
    <header><div><h1>{{ t('workCalendars.title') }}</h1><p>{{ t('workCalendars.caption') }}</p></div>
      <div class="work-calendar-page__actions"><ElButton :disabled="loading || !readable" @click="load(cursor)">{{ t('common.refresh') }}</ElButton>
        <ElButton v-if="session.can('workflow.calendars.create')" type="primary" :disabled="session.switching" data-testid="work-calendar-create" @click="create">{{ t('workCalendars.create') }}</ElButton></div></header>
    <ElAlert v-if="!readable" :title="t('workCalendars.readDenied')" type="warning" :closable="false" />
    <ElAlert v-if="problem" :title="problem" type="error" :closable="false" role="alert" />
    <ElTable v-loading="loading" :data="rows" row-key="id" :empty-text="t('workCalendars.empty')">
      <ElTableColumn prop="displayName" :label="t('workCalendars.name')" min-width="180" /><ElTableColumn prop="calendarKey" :label="t('workCalendars.key')" min-width="180" />
      <ElTableColumn :label="t('workCalendars.latest')" min-width="140"><template #default="scope">{{ scope.row.latestVersionNumber ? t('workCalendars.versionLabel', { version: scope.row.latestVersionNumber }) : t('workCalendars.nonePublished') }}</template></ElTableColumn>
      <ElTableColumn prop="timeZoneKey" :label="t('workCalendars.zone')" min-width="160" />
      <ElTableColumn width="120"><template #default="scope"><ElButton link type="primary" @click="open(scope.row.id)">{{ session.can('workflow.calendars.update') ? t('workCalendars.edit') : t('workCalendars.showVersion') }}</ElButton></template></ElTableColumn>
    </ElTable>
    <div class="work-calendar-page__actions"><ElButton :disabled="!back.length || loading" @click="previous">{{ t('common.previousPage') }}</ElButton><ElButton :disabled="!nextCursor || loading" @click="next">{{ t('common.nextPage') }}</ElButton></div>
    <ElDrawer :model-value="drawer" :title="creating ? t('workCalendars.create') : t('workCalendars.edit')" size="min(880px, 96vw)" @update:model-value="!$event && close()">
      <div v-if="detailLoading">{{ t('common.loading') }}</div>
      <div v-else class="work-calendar-page__editor">
        <ElAlert v-if="receipt" :title="t('workCalendars.published', { version: receipt.versionNumber })" type="success" :closable="false" />
        <ElAlert v-if="historical" :title="t('workCalendars.versionImmutable')" type="info" :closable="false" />
        <ElTag v-if="!editable && !historical">{{ t('workCalendars.readOnly') }}</ElTag>
        <label>{{ t('workCalendars.key') }}<ElInput v-model="key" :disabled="!creating || !editable" data-testid="work-calendar-key" /></label>
        <label>{{ t('workCalendars.name') }}<ElInput v-model="name" :disabled="!editable" data-testid="work-calendar-name" /></label>
        <WorkflowWorkCalendarEditor :model-value="historical?.calendar ?? draft" :disabled="!editable || Boolean(historical)" @update:model-value="draft = $event" />
        <div v-if="historical"><ElButton @click="historical = undefined">{{ t('common.cancel') }}</ElButton></div>
        <template v-if="selected">
          <h2>{{ t('workCalendars.versions') }}</h2>
          <div v-for="version in versions" :key="version.id" class="work-calendar-page__version"><span>{{ t('workCalendars.versionLabel', { version: version.versionNumber }) }} · {{ version.timeZoneKey }} · {{ version.publishedAtUtc }}</span>
            <ElButton :disabled="acting" @click="viewVersion(version.id)">{{ t('workCalendars.showVersion') }}</ElButton></div>
          <div class="work-calendar-page__actions"><ElButton :disabled="versionsLoading" @click="loadVersions(selected.id)">{{ t('common.refresh') }}</ElButton><ElButton :disabled="!beforeVersion || versionsLoading" @click="beforeVersion && loadVersions(selected.id, beforeVersion)">{{ t('common.loadOlder') }}</ElButton></div>
        </template>
      </div>
      <template #footer><div class="work-calendar-page__footer"><span v-if="dirty && !creating">{{ t('workCalendars.saveBeforePublish') }}</span>
        <ElButton v-if="mustRefresh && selected" @click="open(selected.id)">{{ t('common.refresh') }}</ElButton><ElButton @click="close">{{ t('common.cancel') }}</ElButton>
        <ElButton v-if="session.can(creating ? 'workflow.calendars.create' : 'workflow.calendars.update')" type="primary" :disabled="!canSave" :loading="acting" data-testid="work-calendar-save" @click="save">{{ t('workCalendars.save') }}</ElButton>
        <ElButton v-if="session.can('workflow.calendars.publish') && !creating" type="primary" :disabled="!canPublish" :loading="acting" data-testid="work-calendar-publish" @click="publish">{{ t('workCalendars.publish') }}</ElButton></div></template>
    </ElDrawer>
  </section>
</template>
<style scoped>
.work-calendar-page { display: grid; gap: 20px; }
.work-calendar-page header { display: flex; justify-content: space-between; align-items: start; flex-wrap: wrap; gap: 16px; }
.work-calendar-page h1 { margin: 0 0 8px; font-size: 24px; }.work-calendar-page header p { margin: 0; color: var(--el-text-color-secondary); }
.work-calendar-page__actions,.work-calendar-page__footer { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.work-calendar-page__footer { justify-content: flex-end; }.work-calendar-page__footer > span { margin-right: auto; color: var(--el-text-color-secondary); }
.work-calendar-page__editor { display: grid; gap: 16px; }.work-calendar-page__editor > label { display: grid; gap: 8px; }
.work-calendar-page__version { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 12px; padding-bottom: 12px; border-bottom: 1px solid var(--el-border-color-light); }
.work-calendar-page__editor h2 { margin: 12px 0 0; font-size: 17px; }
</style>
