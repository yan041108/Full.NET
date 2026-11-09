<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, reactive, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElDatePicker,
  ElDialog,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElSelect,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, PersonalSchedule } from '@fullnet/client-contracts';
import {
  isFullNetProblemDetails,
  PERSONAL_SCHEDULE_STATUSES
} from '@fullnet/client-contracts';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import { personalScheduleAssistantText } from '../i18n/personal-schedule-assistant';
import { personalScheduleCompletionText } from '../i18n/personal-schedule-completion';
import {
  createPersonalSchedule,
  deletePersonalSchedule,
  listPersonalSchedules,
  setPersonalScheduleStatus,
  updatePersonalSchedule
} from '../api/personal-schedules';

defineOptions({ name: 'PersonalSchedulesView' });

interface CalendarCell {
  date: Date;
  inCurrentMonth: boolean;
  isToday: boolean;
  isSelected: boolean;
  scheduleCount: number;
}

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const monthAnchor = ref(startOfMonth(new Date()));
const selectedDate = ref(startOfDay(new Date()));
const schedules = ref<PersonalSchedule[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const dialogVisible = ref(false);
const editingSchedule = ref<PersonalSchedule>();
const formContent = ref('');
const formStart = ref<Date | null>(null);
const formEnd = ref<Date | null>(null);
const dialogSaving = ref(false);
const dialogProblem = ref<FullNetProblemDetails>();
const fieldErrors = reactive({ content: '', start: '', end: '' });
const dialogScope = useAuthorizedViewScope(session, () => {
  dialogVisible.value = false;
  dialogSaving.value = false;
  editingSchedule.value = undefined;
  formContent.value = '';
  formStart.value = null;
  formEnd.value = null;
  clearDialogFeedback();
}, () => {});
watch(() => session.switching, value => { if (value) dialogScope.invalidate(); }, { flush: 'sync' });
const filterStatus = ref<'' | PersonalSchedule['status']>('');
const sourceSchedule = ref<Pick<PersonalSchedule, 'id' | 'version'>>();
const SourceDialog = defineAsyncComponent(() => import('../components/AiPersonalScheduleSourceDialog.vue'));
const CompletionDialog = defineAsyncComponent(() => import('../components/AiPersonalScheduleCompletionDialog.vue'));
const completionSchedule = ref<Pick<PersonalSchedule, 'id' | 'version'>>();
const completionRuns = ref<Record<string, string>>({});
const canCompleteAssistant = computed(() => !session.switching && ['calendar.personal_schedules.read', 'calendar.personal_schedules.set_status',
  'calendar.personal_schedules.assistant_complete', 'ai.agent_runs.create', 'ai.agent_runs.read', 'ai.agent_runs.resume',
  'ai.agent_approvals.read', 'ai.agent_approvals.request', 'ai.models.read'].every(code => session.can(code)));
watch(() => [session.currentUser, session.currentContextName, session.switching], () => { completionSchedule.value = undefined; completionRuns.value = {}; }, { deep: true, flush: 'sync' });
const canReadAssistant = computed(() => !session.switching && session.can('calendar.personal_schedules.read') && session.can('calendar.personal_schedules.assistant_read'));
watch(() => [session.currentUser, session.currentContextName, session.switching, canReadAssistant.value], () => { sourceSchedule.value = undefined; }, { deep: true, flush: 'sync' });
function invalidateSourceRow() {
  // 来源拒读后，普通列表也不能继续显示该旧正文；打开的来源窗口仅保留身份及刷新提示。
  if (sourceSchedule.value) schedules.value = schedules.value.filter(row => row.id !== sourceSchedule.value!.id);
}

const canCreate = computed(() => session.can('calendar.personal_schedules.create'));
const canUpdate = computed(() => session.can('calendar.personal_schedules.update'));
const canDelete = computed(() => session.can('calendar.personal_schedules.delete'));
const canSetStatus = computed(() => session.can('calendar.personal_schedules.set_status'));
const showDialogActions = computed(() =>
  editingSchedule.value ? canUpdate.value : canCreate.value
);

const monthLabel = computed(() =>
  monthAnchor.value.toLocaleDateString(locale.value, {
    year: 'numeric',
    month: 'long'
  })
);

const weekdayLabels = computed(() => {
  const formatter = new Intl.DateTimeFormat(locale.value, { weekday: 'short' });
  const start = startOfWeek(new Date());
  return Array.from({ length: 7 }, (_, index) =>
    formatter.format(addDays(start, index))
  );
});

const calendarCells = computed<CalendarCell[]>(() => {
  const monthStart = startOfMonth(monthAnchor.value);
  const monthEnd = endOfMonth(monthAnchor.value);
  const gridStart = startOfWeek(monthStart);
  const gridEnd = endOfWeek(monthEnd);
  const today = startOfDay(new Date());
  const selected = startOfDay(selectedDate.value);
  const cells: CalendarCell[] = [];

  for (let cursor = gridStart; cursor <= gridEnd; cursor = addDays(cursor, 1)) {
    const dayStart = startOfDay(cursor);
    const dayEnd = endOfDay(cursor);
    const count = schedules.value.filter(schedule =>
      overlapsRange(schedule.startAtUtc, schedule.endAtUtc, dayStart, dayEnd)
    ).length;
    cells.push({
      date: new Date(cursor),
      inCurrentMonth: cursor.getMonth() === monthStart.getMonth(),
      isToday: dayStart.getTime() === today.getTime(),
      isSelected: dayStart.getTime() === selected.getTime(),
      scheduleCount: count
    });
  }

  return cells;
});

const daySchedules = computed(() => {
  const dayStart = startOfDay(selectedDate.value);
  const dayEnd = endOfDay(selectedDate.value);
  return schedules.value
    .filter(schedule =>
      overlapsRange(schedule.startAtUtc, schedule.endAtUtc, dayStart, dayEnd)
    )
    .sort((left, right) =>
      Date.parse(left.startAtUtc) - Date.parse(right.startAtUtc)
    );
});

watch([monthAnchor, filterStatus], () => {
  void loadMonthSchedules();
});

onMounted(() => {
  void loadMonthSchedules();
});

function startOfMonth(value: Date): Date {
  return new Date(value.getFullYear(), value.getMonth(), 1);
}

function endOfMonth(value: Date): Date {
  return new Date(value.getFullYear(), value.getMonth() + 1, 0, 23, 59, 59, 999);
}

function startOfWeek(value: Date): Date {
  const day = value.getDay();
  const diff = day === 0 ? -6 : 1 - day;
  return startOfDay(addDays(value, diff));
}

function endOfWeek(value: Date): Date {
  return endOfDay(addDays(startOfWeek(value), 6));
}

function startOfDay(value: Date): Date {
  return new Date(value.getFullYear(), value.getMonth(), value.getDate());
}

function endOfDay(value: Date): Date {
  return new Date(value.getFullYear(), value.getMonth(), value.getDate(), 23, 59, 59, 999);
}

function addDays(value: Date, amount: number): Date {
  const next = new Date(value);
  next.setDate(next.getDate() + amount);
  return next;
}

function overlapsRange(
  startIso: string,
  endIso: string,
  rangeStart: Date,
  rangeEnd: Date
): boolean {
  const start = Date.parse(startIso);
  const end = Date.parse(endIso);
  return start <= rangeEnd.getTime() && end >= rangeStart.getTime();
}

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString(locale.value);
}

function formatDay(value: Date): string {
  return value.toLocaleDateString(locale.value, {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    weekday: 'long'
  });
}

function statusLabel(status: PersonalSchedule['status']): string {
  return status === PERSONAL_SCHEDULE_STATUSES.completed
    ? t('personalSchedules.statusCompleted')
    : t('personalSchedules.statusPending');
}

function statusTagType(status: PersonalSchedule['status']): 'success' | 'info' {
  return status === PERSONAL_SCHEDULE_STATUSES.completed ? 'success' : 'info';
}

async function loadMonthSchedules(signal?: AbortSignal): Promise<void> {
  sourceSchedule.value = undefined;
  loading.value = true;
  problem.value = undefined;
  try {
    const fromUtc = startOfMonth(monthAnchor.value).toISOString();
    const toUtc = endOfMonth(monthAnchor.value).toISOString();
    const page = await listPersonalSchedules({
      page: 1,
      pageSize: 500,
      status: filterStatus.value,
      fromUtc,
      toUtc
    }, signal);
    if (!signal?.aborted) schedules.value = page.items;
  } catch (error: unknown) {
    if (!signal?.aborted) problem.value = toProblem(error, 'personalSchedules.loadFailed');
  } finally {
    loading.value = false;
  }
}

function selectDate(date: Date): void {
  selectedDate.value = startOfDay(date);
}

function goToPreviousMonth(): void {
  monthAnchor.value = startOfMonth(
    new Date(monthAnchor.value.getFullYear(), monthAnchor.value.getMonth() - 1, 1)
  );
}

function goToNextMonth(): void {
  monthAnchor.value = startOfMonth(
    new Date(monthAnchor.value.getFullYear(), monthAnchor.value.getMonth() + 1, 1)
  );
}

function goToToday(): void {
  const today = new Date();
  monthAnchor.value = startOfMonth(today);
  selectedDate.value = startOfDay(today);
}

function openCreateDialog(): void {
  if (changing.value || dialogSaving.value || session.switching || !canCreate.value) return;
  clearDialogFeedback();
  editingSchedule.value = undefined;
  const base = selectedDate.value;
  formContent.value = '';
  formStart.value = new Date(base.getFullYear(), base.getMonth(), base.getDate(), 9, 0, 0);
  formEnd.value = new Date(base.getFullYear(), base.getMonth(), base.getDate(), 10, 0, 0);
  dialogVisible.value = true;
}

function openEditDialog(schedule: PersonalSchedule): void {
  if (changing.value || dialogSaving.value || session.switching || !canUpdate.value) return;
  clearDialogFeedback();
  editingSchedule.value = schedule;
  formContent.value = schedule.content;
  formStart.value = new Date(schedule.startAtUtc);
  formEnd.value = new Date(schedule.endAtUtc);
  dialogVisible.value = true;
}

function clearDialogFeedback(): void {
  dialogProblem.value = undefined;
  fieldErrors.content = '';
  fieldErrors.start = '';
  fieldErrors.end = '';
}

async function submitDialog(): Promise<void> {
  if (!dialogVisible.value || changing.value || dialogSaving.value || session.switching || !showDialogActions.value) {
    return;
  }
  clearDialogFeedback();
  const content = formContent.value.trim();
  const start = formStart.value;
  const end = formEnd.value;
  if (!content) fieldErrors.content = t('personalSchedules.contentRequired');
  if (!(start instanceof Date) || !Number.isFinite(start.getTime())) fieldErrors.start = t('personalSchedules.startRequired');
  if (!(end instanceof Date) || !Number.isFinite(end.getTime())) fieldErrors.end = t('personalSchedules.endRequired');
  if (fieldErrors.content || fieldErrors.start || fieldErrors.end || !start || !end) {
    return;
  }
  if (end < start) {
    fieldErrors.end = t('personalSchedules.invalidTimeRange');
    return;
  }
  const existing = editingSchedule.value;
  const operation = dialogScope.begin(existing ? 'calendar.personal_schedules.update' : 'calendar.personal_schedules.create');
  if (!operation) return;

  dialogSaving.value = true;
  // 日期选择器使用本地 Date；只在发送时转 UTC，避免把本地钟点直接标成 Z。
  const body = { content, startAtUtc: start.toISOString(), endAtUtc: end.toISOString() };
  try {
    if (existing) {
      await updatePersonalSchedule(existing.id, { ...body, version: existing.version }, operation.signal);
    } else {
      await createPersonalSchedule(body, operation.signal);
    }
    if (!operation.current()) return;
    ElMessage.success(t(existing ? 'personalSchedules.updateSuccess' : 'personalSchedules.createSuccess'));
    dialogVisible.value = false;
    await loadMonthSchedules(operation.signal);
  } catch (error: unknown) {
    if (operation.current()) dialogProblem.value = toProblem(error);
  } finally {
    if (operation.current()) dialogSaving.value = false;
    operation.finish();
  }
}

async function toggleStatus(schedule: PersonalSchedule): Promise<void> {
  if (changing.value || !canSetStatus.value) {
    return;
  }
  const nextStatus =
    schedule.status === PERSONAL_SCHEDULE_STATUSES.completed
      ? PERSONAL_SCHEDULE_STATUSES.pending
      : PERSONAL_SCHEDULE_STATUSES.completed;
  changing.value = true;
  problem.value = undefined;
  try {
    await setPersonalScheduleStatus(schedule.id, {
      status: nextStatus,
      version: schedule.version
    });
    ElMessage.success(t('personalSchedules.statusChangeSuccess'));
    await loadMonthSchedules();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function removeSchedule(schedule: PersonalSchedule): Promise<void> {
  if (changing.value || !canDelete.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('personalSchedules.confirmDelete'),
      t('personalSchedules.deleteTitle'),
      { type: 'warning' }
    );
  } catch {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    await deletePersonalSchedule(schedule.id, { version: schedule.version });
    ElMessage.success(t('personalSchedules.deleteSuccess'));
    await loadMonthSchedules();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'personalSchedules.loadFailed' | 'personalSchedules.operationFailed' = 'personalSchedules.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.personal_schedule_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="personal-schedules-view art-page-stack" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('personalSchedules.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card shadow="never" class="personal-schedules-toolbar">
      <div class="personal-schedules-toolbar__row">
        <div class="personal-schedules-toolbar__nav">
          <el-button data-testid="personal-schedules-prev-month" @click="goToPreviousMonth">
            {{ t('personalSchedules.prevMonth') }}
          </el-button>
          <strong data-testid="personal-schedules-month-label">{{ monthLabel }}</strong>
          <el-button data-testid="personal-schedules-next-month" @click="goToNextMonth">
            {{ t('personalSchedules.nextMonth') }}
          </el-button>
          <el-button data-testid="personal-schedules-today" @click="goToToday">
            {{ t('personalSchedules.today') }}
          </el-button>
        </div>
        <div class="personal-schedules-toolbar__filters">
          <label>
            <span>{{ t('personalSchedules.statusFilter') }}</span>
            <el-select
              v-model="filterStatus"
              clearable
              data-testid="personal-schedules-status-filter"
              :placeholder="t('personalSchedules.statusFilterPlaceholder')"
            >
              <el-option
                :label="t('personalSchedules.statusPending')"
                :value="PERSONAL_SCHEDULE_STATUSES.pending"
              />
              <el-option
                :label="t('personalSchedules.statusCompleted')"
                :value="PERSONAL_SCHEDULE_STATUSES.completed"
              />
            </el-select>
          </label>
          <PermissionGate code="calendar.personal_schedules.create">
            <el-button
              type="primary"
              data-testid="personal-schedules-create"
              @click="openCreateDialog"
            >
              {{ t('personalSchedules.create') }}
            </el-button>
          </PermissionGate>
        </div>
      </div>
    </el-card>

    <div class="personal-schedules-layout">
      <el-card shadow="never" class="personal-schedules-calendar">
        <div class="personal-schedules-calendar__weekdays">
          <span v-for="label in weekdayLabels" :key="label">{{ label }}</span>
        </div>
        <div class="personal-schedules-calendar__grid" data-testid="personal-schedules-month-grid">
          <button
            v-for="cell in calendarCells"
            :key="cell.date.toISOString()"
            type="button"
            class="personal-schedules-calendar__cell"
            :class="{
              'is-other-month': !cell.inCurrentMonth,
              'is-today': cell.isToday,
              'is-selected': cell.isSelected
            }"
            @click="selectDate(cell.date)"
          >
            <span class="personal-schedules-calendar__day">{{ cell.date.getDate() }}</span>
            <span
              v-if="cell.scheduleCount > 0"
              class="personal-schedules-calendar__count"
            >
              {{ cell.scheduleCount }}
            </span>
          </button>
        </div>
      </el-card>

      <el-card shadow="never" class="personal-schedules-day-list">
        <div class="personal-schedules-day-list__header">
          <h2 data-testid="personal-schedules-day-title">{{ formatDay(selectedDate) }}</h2>
        </div>
        <p v-if="daySchedules.length === 0" class="personal-schedules-day-list__empty">
          {{ t('personalSchedules.emptyDay') }}
        </p>
        <ul v-else class="personal-schedules-day-list__items">
          <li
            v-for="schedule in daySchedules"
            :key="schedule.id"
            class="personal-schedules-day-list__item"
          >
            <div class="personal-schedules-day-list__content">
              <strong>{{ schedule.content }}</strong>
              <span>{{ formatDateTime(schedule.startAtUtc) }} - {{ formatDateTime(schedule.endAtUtc) }}</span>
              <el-tag :type="statusTagType(schedule.status)">{{ statusLabel(schedule.status) }}</el-tag>
            </div>
            <div class="personal-schedules-day-list__actions">
              <el-button v-if="canCompleteAssistant && schedule.status === 'pending'" link data-testid="personal-schedules-assistant-complete" @click="completionSchedule = { id: schedule.id, version: schedule.version }">
                {{ personalScheduleCompletionText(locale, 'open') }}
              </el-button>
              <el-button v-if="canReadAssistant" link data-testid="personal-schedules-assistant-source" @click="sourceSchedule = { id: schedule.id, version: schedule.version }">
                {{ personalScheduleAssistantText(locale, 'open') }}
              </el-button>
              <PermissionGate code="calendar.personal_schedules.update">
                <el-button
                  link
                  type="primary"
                  data-testid="personal-schedules-edit"
                  @click="openEditDialog(schedule)"
                >
                  {{ t('personalSchedules.edit') }}
                </el-button>
              </PermissionGate>
              <PermissionGate code="calendar.personal_schedules.set_status">
                <el-button
                  link
                  data-testid="personal-schedules-toggle-status"
                  @click="toggleStatus(schedule)"
                >
                  {{
                    schedule.status === PERSONAL_SCHEDULE_STATUSES.completed
                      ? t('personalSchedules.markPending')
                      : t('personalSchedules.markCompleted')
                  }}
                </el-button>
              </PermissionGate>
              <PermissionGate code="calendar.personal_schedules.delete">
                <el-button
                  link
                  type="danger"
                  data-testid="personal-schedules-delete"
                  @click="removeSchedule(schedule)"
                >
                  {{ t('personalSchedules.delete') }}
                </el-button>
              </PermissionGate>
            </div>
          </li>
        </ul>
      </el-card>
    </div>

    <SourceDialog v-if="sourceSchedule && canReadAssistant" :schedule-id="sourceSchedule.id" :expected-version="sourceSchedule.version" @invalidated="invalidateSourceRow" @close="sourceSchedule = undefined" />
    <CompletionDialog v-if="completionSchedule && canCompleteAssistant" :schedule-id="completionSchedule.id" :expected-version="completionSchedule.version"
      :original-run-id="completionRuns[completionSchedule.id + ':' + completionSchedule.version]"
      @accepted="id => { if (completionSchedule) completionRuns[completionSchedule.id + ':' + completionSchedule.version] = id; }"
      @changed="loadMonthSchedules" @close="completionSchedule = undefined" />
    <el-dialog
      v-model="dialogVisible"
      :title="editingSchedule ? t('personalSchedules.editTitle') : t('personalSchedules.createTitle')"
      width="520px"
      :close-on-click-modal="!dialogSaving"
      :close-on-press-escape="!dialogSaving"
      :show-close="!dialogSaving"
      data-testid="personal-schedules-dialog"
    >
      <div v-if="dialogProblem" class="art-inline-alert" role="alert" data-testid="personal-schedules-submit-problem">
        <strong translate="no">{{ dialogProblem.code }}</strong>
        <span>{{ dialogProblem.title }}</span>
        <code v-if="dialogProblem.traceId" translate="no">{{ dialogProblem.traceId }}</code>
      </div>
      <label>
        <span>{{ t('personalSchedules.fieldContent') }}</span>
        <el-input
          v-model="formContent"
          maxlength="256"
          data-testid="personal-schedules-content"
          @update:model-value="fieldErrors.content = ''"
        />
        <span v-if="fieldErrors.content" role="alert">{{ fieldErrors.content }}</span>
      </label>
      <label>
        <span>{{ t('personalSchedules.fieldStartAt') }}</span>
        <el-date-picker
          v-model="formStart"
          id="personal-schedules-start"
          type="datetime"
          data-testid="personal-schedules-start"
          style="width: 100%"
          @change="fieldErrors.start = ''; fieldErrors.end = ''"
        />
        <span v-if="fieldErrors.start" role="alert">{{ fieldErrors.start }}</span>
      </label>
      <label>
        <span>{{ t('personalSchedules.fieldEndAt') }}</span>
        <el-date-picker
          v-model="formEnd"
          id="personal-schedules-end"
          type="datetime"
          data-testid="personal-schedules-end"
          style="width: 100%"
          @change="fieldErrors.end = ''"
        />
        <span v-if="fieldErrors.end" role="alert">{{ fieldErrors.end }}</span>
      </label>
      <template #footer>
        <el-button :disabled="dialogSaving" @click="dialogVisible = false">{{ t('personalSchedules.cancel') }}</el-button>
        <el-button
          v-if="showDialogActions"
          type="primary"
          data-testid="personal-schedules-submit"
          :loading="dialogSaving"
          @click="submitDialog"
        >
          {{ editingSchedule ? t('personalSchedules.save') : t('personalSchedules.create') }}
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.personal-schedules-toolbar__row {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  justify-content: space-between;
  align-items: center;
}

.personal-schedules-toolbar__nav,
.personal-schedules-toolbar__filters {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
}

.personal-schedules-toolbar__filters label {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.personal-schedules-layout {
  display: grid;
  grid-template-columns: minmax(280px, 1.2fr) minmax(280px, 1fr);
  gap: 16px;
}

.personal-schedules-calendar__weekdays,
.personal-schedules-calendar__grid {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  gap: 8px;
}

.personal-schedules-calendar__weekdays {
  margin-bottom: 8px;
  color: var(--el-text-color-regular);
  font-size: 12px;
  text-align: center;
}

.personal-schedules-calendar__cell {
  min-height: 72px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  background: var(--el-fill-color-blank);
  padding: 8px;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  cursor: pointer;
}

.personal-schedules-calendar__cell.is-other-month {
  color: var(--el-text-color-regular);
  background: var(--el-fill-color-light);
}

.personal-schedules-calendar__cell.is-today {
  border-color: var(--el-color-primary);
}

.personal-schedules-calendar__cell.is-selected {
  background: var(--el-color-primary-light-9);
}

.personal-schedules-calendar__day {
  font-weight: 600;
  white-space: nowrap;
}

.personal-schedules-calendar__count {
  display: inline-flex;
  min-width: 20px;
  height: 20px;
  padding: 0 6px;
  border-radius: 999px;
  align-items: center;
  justify-content: center;
  background: var(--el-color-primary);
  color: #fff;
  font-size: 12px;
}

.personal-schedules-day-list__header h2 {
  margin: 0 0 12px;
}

.personal-schedules-day-list__empty {
  margin: 0;
  color: var(--el-text-color-regular);
}

.personal-schedules-day-list__items {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.personal-schedules-day-list__item {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 12px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
}

.personal-schedules-day-list__content {
  display: flex;
  min-width: 0;
  overflow-wrap: anywhere;
  flex-direction: column;
  gap: 6px;
}

.personal-schedules-day-list__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  align-items: flex-start;
}

.personal-schedules-day-list :deep(.el-tag) {
  --el-tag-text-color: var(--el-text-color-primary);
}

.personal-schedules-day-list__actions :deep(.el-button--danger) {
  --el-button-text-color: var(--fullnet-color-danger);
  --el-button-hover-text-color: var(--fullnet-color-danger);
  --el-button-active-text-color: var(--fullnet-color-danger);
}

@media (max-width: 960px) {
  .personal-schedules-layout {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .personal-schedules-calendar__weekdays,
  .personal-schedules-calendar__grid {
    gap: 4px;
  }

  .personal-schedules-calendar__cell {
    min-height: 56px;
    padding: 4px 2px;
    align-items: center;
  }

  .personal-schedules-calendar__day {
    font-size: 14px;
  }

  .personal-schedules-day-list__item {
    flex-direction: column;
  }
}
</style>
