<script setup lang="ts">
import {
  ElButton,
  ElDatePicker,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElOption,
  ElRadio,
  ElRadioGroup,
  ElSelect,
  ElTabPane,
  ElTabs,
  ElTransfer
} from 'element-plus';
import type { HostUser, HostUserProfileWrite } from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';

defineOptions({ name: 'UserEditorDialog' });

const props = defineProps<{
  open: boolean;
  mode: 'create' | 'edit';
  user: HostUser | null;
  username: string;
  displayName: string;
  password: string;
  profile: HostUserProfileWrite;
  activeTab: 'basic' | 'roles' | 'org' | 'profile' | 'binding';
  transferRoles: Array<{ key: string; label: string; disabled?: boolean }>;
  selectedRoleIds: string[];
  orgUnitOptions: Array<{ value: string; label: string }>;
  positionOptions: Array<{ value: string; label: string }>;
  primaryUnitId: string;
  subsidiaryUnitIds: string[];
  positionId: string;
  saving: boolean;
  canAssignRoles: boolean;
  canCreate: boolean;
  canUpdate: boolean;
  translate: (key: MessageKey) => string;
}>();

const emit = defineEmits<{
  'update:open': [value: boolean];
  'update:username': [value: string];
  'update:displayName': [value: string];
  'update:password': [value: string];
  'update:profile': [value: HostUserProfileWrite];
  'update:activeTab': [value: 'basic' | 'roles' | 'org' | 'profile' | 'binding'];
  'update:selectedRoleIds': [value: string[]];
  'update:primaryUnitId': [value: string];
  'update:subsidiaryUnitIds': [value: string[]];
  'update:positionId': [value: string];
  submit: [];
  cancel: [];
}>();

const dialogTitle = () => (
  props.mode === 'create'
    ? props.translate('users.createDialogTitle')
    : props.translate('users.editDialogTitle')
);

function close(): void {
  emit('update:open', false);
  emit('cancel');
}

function patchProfile(patch: Partial<HostUserProfileWrite>): void {
  emit('update:profile', { ...props.profile, ...patch });
}
</script>

<template>
  <el-dialog
    :model-value="open"
    width="920px"
    class="users-editor-dialog"
    modal-class="users-editor-modal"
    destroy-on-close
    :show-close="false"
    append-to-body
    align-center
    @update:model-value="emit('update:open', $event)"
  >
    <template #header>
      <div class="users-editor-dialog__header">
        <span>{{ dialogTitle() }}</span>
        <button type="button" class="users-editor-dialog__close" @click="close">×</button>
      </div>
    </template>

    <el-tabs
      :model-value="activeTab"
      class="users-editor-dialog__tabs"
      @update:model-value="emit('update:activeTab', $event as typeof activeTab)"
    >
      <el-tab-pane :label="translate('users.tabBasic')" name="basic">
        <el-form label-width="96px" class="users-editor-dialog__form">
          <div class="users-editor-dialog__grid">
            <el-form-item :label="translate('users.username')" required>
              <el-input
                :model-value="username"
                :disabled="mode === 'edit'"
                :placeholder="translate('users.usernamePlaceholder')"
                @update:model-value="emit('update:username', $event)"
              />
            </el-form-item>
            <el-form-item :label="translate('users.realName')" required>
              <el-input
                :model-value="displayName"
                :placeholder="translate('users.displayNamePlaceholder')"
                @update:model-value="emit('update:displayName', $event)"
              />
            </el-form-item>
            <el-form-item :label="translate('users.nickname')">
              <el-input
                :model-value="profile.nickname ?? ''"
                @update:model-value="patchProfile({ nickname: $event || null })"
              />
            </el-form-item>
            <el-form-item :label="translate('users.phone')">
              <el-input
                :model-value="profile.phoneNumber ?? ''"
                @update:model-value="patchProfile({ phoneNumber: $event || null })"
              />
            </el-form-item>
            <el-form-item :label="translate('users.email')">
              <el-input
                :model-value="profile.email ?? ''"
                @update:model-value="patchProfile({ email: $event || null })"
              />
            </el-form-item>
            <el-form-item :label="translate('users.gender')">
              <el-radio-group
                :model-value="profile.gender ?? ''"
                @update:model-value="patchProfile({ gender: $event || null })"
              >
                <el-radio value="male">{{ translate('users.genderMale') }}</el-radio>
                <el-radio value="female">{{ translate('users.genderFemale') }}</el-radio>
              </el-radio-group>
            </el-form-item>
            <el-form-item :label="translate('users.employeeNumber')">
              <el-input
                :model-value="profile.employeeNumber ?? ''"
                @update:model-value="patchProfile({ employeeNumber: $event || null })"
              />
            </el-form-item>
            <el-form-item
              v-if="mode === 'create'"
              :label="translate('users.password')"
              required
            >
              <el-input
                :model-value="password"
                type="password"
                show-password
                :placeholder="translate('users.passwordPlaceholder')"
                @update:model-value="emit('update:password', $event)"
              />
            </el-form-item>
            <el-form-item v-else :label="translate('users.status')">
              <el-input
                :model-value="user?.isActive ? translate('users.active') : translate('users.inactive')"
                disabled
              />
            </el-form-item>
            <el-form-item class="users-editor-dialog__full" :label="translate('users.remark')">
              <el-input
                :model-value="profile.remark ?? ''"
                type="textarea"
                :rows="2"
                @update:model-value="patchProfile({ remark: $event || null })"
              />
            </el-form-item>
          </div>
        </el-form>
      </el-tab-pane>

      <el-tab-pane
        v-if="canAssignRoles"
        :label="translate('users.tabRoles')"
        name="roles"
      >
        <el-transfer
          :model-value="selectedRoleIds"
          :data="transferRoles"
          filterable
          :titles="[
            translate('users.unauthorizedRoles'),
            translate('users.authorizedRoles')
          ]"
          :filter-placeholder="translate('users.roleFilterPlaceholder')"
          class="users-editor-dialog__transfer"
          @update:model-value="emit('update:selectedRoleIds', $event)"
        />
      </el-tab-pane>

      <el-tab-pane :label="translate('users.tabOrg')" name="org">
        <el-form label-width="108px" class="users-editor-dialog__form">
          <div class="users-editor-dialog__grid users-editor-dialog__grid--single">
            <el-form-item :label="translate('users.primaryOrg')">
              <el-select
                :model-value="primaryUnitId || undefined"
                clearable
                filterable
                :placeholder="translate('users.selectPrimaryOrg')"
                style="width: 100%"
                @update:model-value="emit('update:primaryUnitId', $event ?? '')"
              >
                <el-option
                  v-for="option in orgUnitOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
            <el-form-item :label="translate('users.subsidiaryOrgs')">
              <el-select
                :model-value="subsidiaryUnitIds"
                multiple
                clearable
                filterable
                collapse-tags
                collapse-tags-tooltip
                :placeholder="translate('users.selectSubsidiaryOrgs')"
                style="width: 100%"
                @update:model-value="emit('update:subsidiaryUnitIds', $event)"
              >
                <el-option
                  v-for="option in orgUnitOptions"
                  :key="`sub-${option.value}`"
                  :label="option.label"
                  :value="option.value"
                  :disabled="option.value === primaryUnitId"
                />
              </el-select>
            </el-form-item>
            <el-form-item :label="translate('users.positionName')">
              <el-select
                :model-value="positionId || undefined"
                clearable
                filterable
                :placeholder="translate('users.selectPosition')"
                style="width: 100%"
                @update:model-value="emit('update:positionId', $event ?? '')"
              >
                <el-option
                  v-for="option in positionOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </div>
        </el-form>
      </el-tab-pane>

      <el-tab-pane :label="translate('users.tabProfile')" name="profile">
        <el-form label-width="120px" class="users-editor-dialog__form">
          <div class="users-editor-dialog__grid">
            <el-form-item :label="translate('users.birthDate')">
              <el-date-picker
                :model-value="profile.birthDate ?? ''"
                type="date"
                value-format="YYYY-MM-DD"
                style="width: 100%"
                @update:model-value="patchProfile({ birthDate: ($event as string) || null })"
              />
            </el-form-item>
            <el-form-item :label="translate('users.profileLocale')">
              <el-input
                :model-value="user?.projectedFields?.preferredLocale ?? translate('users.fieldEmpty')"
                disabled
              />
            </el-form-item>
            <el-form-item :label="translate('users.address')" class="users-editor-dialog__full">
              <el-input
                :model-value="profile.address ?? ''"
                @update:model-value="patchProfile({ address: $event || null })"
              />
            </el-form-item>
            <el-form-item :label="translate('users.profileFailedLogin')">
              <el-input
                :model-value="String(user?.projectedFields?.failedLoginCount ?? 0)"
                disabled
              />
            </el-form-item>
            <el-form-item :label="translate('users.profileLockout')">
              <el-input
                :model-value="user?.projectedFields?.lockoutEndUtc ?? translate('users.fieldEmpty')"
                disabled
              />
            </el-form-item>
            <el-form-item :label="translate('users.createdAt')">
              <el-input :model-value="user?.createdAtUtc ?? translate('users.fieldEmpty')" disabled />
            </el-form-item>
          </div>
        </el-form>
      </el-tab-pane>

      <el-tab-pane
        v-if="mode === 'edit'"
        :label="translate('users.tabBinding')"
        name="binding"
      >
        <p class="users-editor-dialog__empty">{{ translate('users.bindingEmpty') }}</p>
      </el-tab-pane>
    </el-tabs>

    <template #footer>
      <div class="users-editor-dialog__footer">
        <el-button @click="close">{{ translate('users.cancel') }}</el-button>
        <el-button
          v-if="mode === 'create' ? canCreate : canUpdate || canAssignRoles"
          type="primary"
          :loading="saving"
          data-testid="users-editor-submit"
          @click="emit('submit')"
        >
          {{ translate('users.confirm') }}
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped>
.users-editor-dialog__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: -8px -8px 0;
  padding: 12px 16px;
  border-radius: 8px 8px 0 0;
  background: var(--art-theme-color);
  color: #fff;
  font-size: 15px;
  font-weight: 600;
  cursor: move;
  user-select: none;
}

.users-editor-dialog__close {
  border: 0;
  background: transparent;
  color: inherit;
  font-size: 22px;
  line-height: 1;
  cursor: pointer;
}

.users-editor-dialog__tabs {
  margin-top: 4px;
}

.users-editor-dialog__form {
  padding-top: 8px;
}

.users-editor-dialog__grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 4px 20px;
}

.users-editor-dialog__full {
  grid-column: 1 / -1;
}

.users-editor-dialog__grid--single {
  grid-template-columns: 1fr;
}

.users-editor-dialog__transfer {
  display: flex;
  justify-content: center;
  padding: 8px 0 4px;
}

.users-editor-dialog__transfer :deep(.el-transfer-panel) {
  width: 280px;
}

.users-editor-dialog__empty {
  margin: 24px 0;
  color: var(--art-gray-600);
  font-size: 13px;
  text-align: center;
}

.users-editor-dialog__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
