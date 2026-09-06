<script setup lang="ts">
import { ElTableColumn } from 'element-plus';
import type { HostUser } from '@fullnet/client-contracts';
import type { UsersGridColumnState } from '../../users/users-grid-columns';

defineOptions({ name: 'UsersGridDataColumn' });

interface UserRow extends HostUser {
  roleLabels: string;
  orgLabel: string;
  positionLabel: string;
}

const props = defineProps<{
  column: UsersGridColumnState;
  genderLabel: (value: string | null | undefined) => string;
  profileText: (value: string | number | null | undefined) => string;
  sortOrderText: (value: number | null | undefined) => string;
  accountTypeLabel: (user: UserRow) => string;
  formatDate: (value: string) => string;
  emptyLabel: string;
}>();
</script>

<template>
  <el-table-column
    v-if="column.visible"
    :key="column.key"
    :label="column.label"
    :width="column.width ?? undefined"
    :min-width="column.width ? undefined : column.minWidth"
    :fixed="column.fixed ?? undefined"
    :align="column.key === 'roles' || column.key === 'org' || column.key === 'position' ? undefined : 'center'"
    :show-overflow-tooltip="column.key === 'roles' || column.key === 'org' || column.key === 'position'"
  >
    <template #default="{ row }">
      <template v-if="column.key === 'gender'">
        {{ genderLabel((row as UserRow).profile?.gender) }}
      </template>
      <template v-else-if="column.key === 'roles'">
        {{ (row as UserRow).roleLabels || emptyLabel }}
      </template>
      <template v-else-if="column.key === 'org'">
        {{ (row as UserRow).orgLabel }}
      </template>
      <template v-else-if="column.key === 'position'">
        {{ (row as UserRow).positionLabel }}
      </template>
      <template v-else-if="column.key === 'employeeNumber'">
        {{ profileText((row as UserRow).profile?.employeeNumber) }}
      </template>
      <template v-else-if="column.key === 'accountType'">
        {{ accountTypeLabel(row as UserRow) }}
      </template>
      <template v-else-if="column.key === 'sortOrder'">
        {{ sortOrderText((row as UserRow).profile?.sortOrder) }}
      </template>
      <template v-else-if="column.key === 'phone'">
        {{ profileText((row as UserRow).profile?.phoneNumber) }}
      </template>
      <template v-else-if="column.key === 'createdAt'">
        {{ formatDate((row as UserRow).createdAtUtc) }}
      </template>
    </template>
  </el-table-column>
</template>
