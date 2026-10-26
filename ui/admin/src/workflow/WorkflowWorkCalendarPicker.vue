<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { ElButton, ElOption, ElSelect } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listWorkflowWorkCalendars, listWorkflowWorkCalendarVersions, getWorkflowWorkCalendarVersion,
  type WorkflowWorkCalendarSummary, type WorkflowWorkCalendarVersionSummary } from '../api/workflow-calendars';
const props = defineProps<{ modelValue?: string; disabled: boolean }>();
const emit = defineEmits<{ select: [value: WorkflowWorkCalendarVersionSummary | undefined] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const calendars = ref<WorkflowWorkCalendarSummary[]>([]); const versions = ref<WorkflowWorkCalendarVersionSummary[]>([]);
const calendarId = ref(''); const selected = ref<WorkflowWorkCalendarVersionSummary>();
const afterId = ref<string>(); const nextAfterId = ref<string | null>(null); const catalogBack = ref<Array<string | undefined>>([]);
const nextBeforeVersion = ref<number | null>(null); const loading = ref(false); const versionsLoading = ref(false); const failed = ref(false);
const readable = computed(() => !session.switching && session.can('workflow.calendars.read'));
let epoch = 0; let catalogGeneration = 0; let selectionGeneration = 0; let disposed = false;
const noEmission = Symbol('none'); let lastEmitted: string | undefined | typeof noEmission = noEmission;
const scopeKey = () => JSON.stringify([session.currentUser?.id, session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.scope, session.switching]);
let previousScope = scopeKey();
function chooseVersion(id: unknown): void {
  if (props.disabled || !readable.value) return; const version = versions.value.find(item => item.id === id); if (!version) return;
  selectionGeneration++; selected.value = version; lastEmitted = version.id; emit('select', version);
}
async function loadCatalog(cursor?: string): Promise<boolean> {
  if (!readable.value) return false; const expected = epoch; const generation = ++catalogGeneration; loading.value = true; failed.value = false;
  try { const value = await listWorkflowWorkCalendars(cursor); if (disposed || expected !== epoch || generation !== catalogGeneration) return false;
    calendars.value = [...value.items]; nextAfterId.value = value.nextAfterId; afterId.value = cursor; return true;
  } catch { if (expected === epoch && generation === catalogGeneration) failed.value = true; return false; }
  finally { if (expected === epoch && generation === catalogGeneration) loading.value = false; }
}
async function nextCatalog(): Promise<void> { const cursor = nextAfterId.value; const previous = afterId.value; if (cursor && await loadCatalog(cursor)) catalogBack.value.push(previous); }
async function previousCatalog(): Promise<void> { if (catalogBack.value.length && await loadCatalog(catalogBack.value.at(-1))) catalogBack.value.pop(); }
async function loadVersions(id: string, before?: number): Promise<void> {
  if (!readable.value) return; const expected = epoch; const generation = ++selectionGeneration; versionsLoading.value = true; failed.value = false;
  try { const value = await listWorkflowWorkCalendarVersions(id, before); if (disposed || expected !== epoch || generation !== selectionGeneration || calendarId.value !== id) return;
    versions.value = [...value.items]; nextBeforeVersion.value = value.nextBeforeVersionNumber;
  } catch { if (expected === epoch && generation === selectionGeneration) failed.value = true; }
  finally { if (expected === epoch && generation === selectionGeneration) versionsLoading.value = false; }
}
async function chooseCalendar(value: unknown): Promise<void> {
  if (props.disabled || !readable.value || typeof value !== 'string') return;
  selectionGeneration++; calendarId.value = value; selected.value = undefined; versions.value = []; nextBeforeVersion.value = null;
  lastEmitted = undefined; emit('select', undefined); await loadVersions(value);
}
async function loadSelected(value?: string): Promise<void> {
  if (!value || !readable.value) return; const expected = epoch; const generation = ++selectionGeneration; versionsLoading.value = true;
  try { const version = await getWorkflowWorkCalendarVersion(value); if (disposed || expected !== epoch || generation !== selectionGeneration || props.modelValue !== value) return;
    calendarId.value = version.calendarId; selected.value = version; await loadVersions(version.calendarId);
  } catch { if (expected === epoch && generation === selectionGeneration) failed.value = true; }
  finally { if (expected === epoch && generation === selectionGeneration) versionsLoading.value = false; }
}
watch(() => props.modelValue, value => {
  if (value === lastEmitted) { lastEmitted = noEmission; return; }
  selectionGeneration++; selected.value = undefined; calendarId.value = ''; versions.value = []; nextBeforeVersion.value = null;
  void loadSelected(value);
}, { immediate: true });
watch(() => JSON.stringify([scopeKey(), readable.value]), () => {
  epoch++; catalogGeneration++; selectionGeneration++; calendars.value = []; versions.value = []; calendarId.value = ''; selected.value = undefined;
  afterId.value = undefined; nextAfterId.value = null; catalogBack.value = []; nextBeforeVersion.value = null; failed.value = false; loading.value = false; versionsLoading.value = false;
  const changed = previousScope !== scopeKey(); previousScope = scopeKey(); lastEmitted = noEmission;
  if (changed) emit('select', undefined);
  if (readable.value) { void loadCatalog(); if (!changed) void loadSelected(props.modelValue); }
}, { flush: 'sync' });
onMounted(() => { if (readable.value) void loadCatalog(); });
onBeforeUnmount(() => { disposed = true; epoch++; catalogGeneration++; selectionGeneration++; });
</script>
<template>
  <div class="work-calendar-picker">
    <p v-if="!readable" role="status">{{ t('workCalendars.readDenied') }}</p>
    <template v-else>
      <label>{{ t('workCalendars.chooseCalendar') }}<ElSelect :model-value="calendarId" :disabled="disabled || loading" data-testid="work-calendar-picker-calendar" @update:model-value="chooseCalendar">
        <ElOption v-if="calendarId && !calendars.some(item => item.id === calendarId)" :value="calendarId" :label="calendarId" />
        <ElOption v-for="item in calendars" :key="item.id" :value="item.id" :disabled="!item.latestVersionId" :label="`${item.displayName} (${item.calendarKey})${item.latestVersionId ? '' : ` · ${t('workCalendars.nonePublished')}`}`" />
      </ElSelect></label>
      <div><ElButton :disabled="loading || !catalogBack.length" @click="previousCatalog">{{ t('common.previousPage') }}</ElButton><ElButton :disabled="loading || !nextAfterId" @click="nextCatalog">{{ t('common.nextPage') }}</ElButton></div>
      <label>{{ t('workCalendars.chooseVersion') }}<ElSelect :model-value="selected?.id ?? modelValue" :disabled="disabled || !calendarId || versionsLoading" data-testid="work-calendar-picker-version" @update:model-value="chooseVersion">
        <ElOption v-if="selected && !versions.some(item => item.id === selected?.id)" :value="selected.id" :label="`${t('workCalendars.versionLabel', { version: selected.versionNumber })} · ${selected.timeZoneKey}`" />
        <ElOption v-for="version in versions" :key="version.id" :value="version.id" :label="`${t('workCalendars.versionLabel', { version: version.versionNumber })} · ${version.timeZoneKey}`" />
      </ElSelect></label>
      <div><ElButton :disabled="!calendarId || versionsLoading" @click="calendarId && loadVersions(calendarId)">{{ t('common.refresh') }}</ElButton><ElButton :disabled="!nextBeforeVersion || versionsLoading" @click="calendarId && nextBeforeVersion && loadVersions(calendarId, nextBeforeVersion)">{{ t('common.loadOlder') }}</ElButton></div>
    </template>
    <p v-if="modelValue && !selected">{{ modelValue }}</p>
    <p v-if="failed" role="alert">{{ t('workCalendars.loadFailed') }}</p>
  </div>
</template>
<style scoped>.work-calendar-picker { display: grid; gap: 12px; }.work-calendar-picker > label { display: grid; gap: 8px; }.work-calendar-picker p { margin: 0; color: var(--el-text-color-secondary); }</style>
