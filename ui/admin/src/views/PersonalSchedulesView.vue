<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
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
import { useAdminI18n } from '../i18n/adminI18n';
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
const formStartAtUtc = ref('');
const formEndAtUtc = ref('');
const filterStatus = ref<'' | PersonalSchedule['status']>('');

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

function toUtcIso(value: string): string {
  return new Date(value).toISOString();
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

async function loadMonthSchedules(): Promise<void> {
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
    });
    schedules.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'personalSchedules.loadFailed');
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
  editingSchedule.value = undefined;
  const base = selectedDate.value;
  const start = new Date(base.getFullYear(), base.getMonth(), base.getDate(), 9, 0, 0);
  const end = new Date(base.getFullYear(), base.getMonth(), base.getDate(), 10, 0, 0);
  formContent.value = '';
  formStartAtUtc.value = start.toISOString();
  formEndAtUtc.value = end.toISOString();
  dialogVisible.value = true;
}

function openEditDialog(schedule: PersonalSchedule): void {
  editingSchedule.value = schedule;
  formContent.value = schedule.content;
  formStartAtUtc.value = schedule.startAtUtc;
  formEndAtUtc.value = schedule.endAtUtc;
  dialogVisible.value = true;
}

async function submitDialog(): Promise<void> {
  if (changing.value || !showDialogActions.value) {
    return;
  }
  const content = formContent.value.trim();
  if (!content || !formStartAtUtc.value || !formEndAtUtc.value) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    if (editingSchedule.value) {
      await updatePersonalSchedule(editingSchedule.value.id, {
        content,
        startAtUtc: toUtcIso(formStartAtUtc.value),
        endAtUtc: toUtcIso(formEndAtUtc.value),
        version: editingSchedule.value.version
      });
      ElMessage.success(t('personalSchedules.updateSuccess'));
    } else {
      await createPersonalSchedule({
        content,
        startAtUtc: toUtcIso(formStartAtUtc.value),
        endAtUtc: toUtcIso(formEndAtUtc.value)
      });
      ElMessage.success(t('personalSchedules.createSuccess'));
    }
    dialogVisible.value = false;
    await loadMonthSchedules();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
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

    <el-dialog
      v-model="dialogVisible"
      :title="editingSchedule ? t('personalSchedules.editTitle') : t('personalSchedules.createTitle')"
      width="520px"
      data-testid="personal-schedules-dialog"
    >
      <label>
        <span>{{ t('personalSchedules.fieldContent') }}</span>
        <el-input
          v-model="formContent"
          maxlength="256"
          data-testid="personal-schedules-content"
        />
      </label>
      <label>
        <span>{{ t('personalSchedules.fieldStartAt') }}</span>
        <el-date-picker
          v-model="formStartAtUtc"
          type="datetime"
          value-format="YYYY-MM-DDTHH:mm:ss.SSS[Z]"
          data-testid="personal-schedules-start"
          style="width: 100%"
        />
      </label>
      <label>
        <span>{{ t('personalSchedules.fieldEndAt') }}</span>
        <el-date-picker
          v-model="formEndAtUtc"
          type="datetime"
          value-format="YYYY-MM-DDTHH:mm:ss.SSS[Z]"
          data-testid="personal-schedules-end"
          style="width: 100%"
        />
      </label>
      <template #footer>
        <el-button @click="dialogVisible = false">{{ t('personalSchedules.cancel') }}</el-button>
        <el-button
          v-if="showDialogActions"
          type="primary"
          data-testid="personal-schedules-submit"
          :loading="changing"
          :disabled="!formContent.trim() || !formStartAtUtc || !formEndAtUtc"
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
  color: var(--el-text-color-secondary);
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
  opacity: 0.45;
}

.personal-schedules-calendar__cell.is-today {
  border-color: var(--el-color-primary);
}

.personal-schedules-calendar__cell.is-selected {
  background: var(--el-color-primary-light-9);
}

.personal-schedules-calendar__day {
  font-weight: 600;
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
  color: var(--el-text-color-secondary);
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
  flex-direction: column;
  gap: 6px;
}

.personal-schedules-day-list__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  align-items: flex-start;
}

@media (max-width: 960px) {
  .personal-schedules-layout {
    grid-template-columns: 1fr;
  }
}
</style>
