<script setup lang="ts">
import {
  ElButton,
  ElDatePicker,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElRadio,
  ElRadioGroup,
  ElSelect,
  ElTabPane,
  ElTabs,
  ElTransfer,
  ElTreeSelect
} from 'element-plus';
import type { FormInstance } from 'element-plus';
import { computed, nextTick, reactive, ref, watch } from 'vue';
import type { HostUser, HostUserProfileWrite } from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';
import { isIdentityPasswordValid } from '../../auth/identity-password-policy';
import {
  applyDisabledToOrganizationUnitTreeSelectOptions,
  type OrganizationUnitTreeSelectOption
} from '../../organization/org-unit-tree';

defineOptions({ name: 'UserEditorDialog' });

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

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
  orgUnitTreeOptions: OrganizationUnitTreeSelectOption[];
  positionOptions: Array<{ value: string; label: string }>;
  primaryUnitId: string;
  subsidiaryUnitIds: string[];
  positionId: string;
  saving: boolean;
  canAssignRoles: boolean;
  canCreate: boolean;
  canUpdate: boolean;
  canManageOrganizations: boolean;
  canManageProfile: boolean;
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

const subsidiaryUnitTreeOptions = computed(() =>
  applyDisabledToOrganizationUnitTreeSelectOptions(
    props.orgUnitTreeOptions,
    props.primaryUnitId ? new Set([props.primaryUnitId]) : new Set()
  )
);

const basicFormRef = ref<FormInstance>();
const basicForm = reactive({
  username: '',
  displayName: '',
  password: '',
  nickname: '',
  phoneNumber: '',
  email: '',
  employeeNumber: '',
  remark: ''
});
const fieldErrors = reactive({
  username: '',
  displayName: '',
  password: '',
  nickname: '',
  phoneNumber: '',
  email: '',
  employeeNumber: '',
  remark: ''
});

function clearFieldErrors(): void {
  fieldErrors.username = '';
  fieldErrors.displayName = '';
  fieldErrors.password = '';
  fieldErrors.nickname = '';
  fieldErrors.phoneNumber = '';
  fieldErrors.email = '';
  fieldErrors.employeeNumber = '';
  fieldErrors.remark = '';
}

function validateUsername(): string {
  if (props.mode !== 'create') {
    return '';
  }

  const username = basicForm.username.trim();
  if (!username) {
    return props.translate('users.usernameRequired');
  }
  if (username.length < 3 || username.length > 128) {
    return props.translate('users.usernameInvalid');
  }
  return '';
}

function validateDisplayName(): string {
  const displayName = basicForm.displayName.trim();
  if (!displayName) {
    return props.translate('users.displayNameRequired');
  }
  if (displayName.length > 128) {
    return props.translate('users.displayNameInvalid');
  }
  return '';
}

function validatePassword(): string {
  if (props.mode !== 'create') {
    return '';
  }

  const password = basicForm.password;
  if (!password) {
    return props.translate('users.passwordRequired');
  }
  if (!isIdentityPasswordValid(password)) {
    return props.translate('users.passwordInvalid');
  }
  return '';
}

function validateNickname(): string {
  if (basicForm.nickname.trim().length > 128) {
    return props.translate('users.nicknameInvalid');
  }
  return '';
}

function validatePhoneNumber(): string {
  if (basicForm.phoneNumber.trim().length > 32) {
    return props.translate('users.phoneInvalid');
  }
  return '';
}

function validateEmail(): string {
  const email = basicForm.email.trim();
  if (!email) {
    return '';
  }
  if (email.length > 256 || !EMAIL_PATTERN.test(email)) {
    return props.translate('users.emailInvalid');
  }
  return '';
}

function validateEmployeeNumber(): string {
  if (basicForm.employeeNumber.trim().length > 64) {
    return props.translate('users.employeeNumberInvalid');
  }
  return '';
}

function validateRemark(): string {
  if (basicForm.remark.trim().length > 512) {
    return props.translate('users.remarkInvalid');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.username = validateUsername();
  fieldErrors.displayName = validateDisplayName();
  fieldErrors.password = validatePassword();
  fieldErrors.nickname = validateNickname();
  fieldErrors.phoneNumber = validatePhoneNumber();
  fieldErrors.email = validateEmail();
  fieldErrors.employeeNumber = validateEmployeeNumber();
  fieldErrors.remark = validateRemark();

  return Object.values(fieldErrors).every(error => !error);
}

const dialogTitle = () => (
  props.mode === 'create'
    ? props.translate('users.createDialogTitle')
    : props.translate('users.editDialogTitle')
);

function syncBasicFormFromProps(): void {
  basicForm.username = props.username;
  basicForm.displayName = props.displayName;
  basicForm.password = props.password;
  basicForm.nickname = props.profile.nickname ?? '';
  basicForm.phoneNumber = props.profile.phoneNumber ?? '';
  basicForm.email = props.profile.email ?? '';
  basicForm.employeeNumber = props.profile.employeeNumber ?? '';
  basicForm.remark = props.profile.remark ?? '';
}

watch(
  () => props.open,
  (open) => {
    if (!open) {
      return;
    }
    syncBasicFormFromProps();
    clearFieldErrors();
    void nextTick(() => basicFormRef.value?.clearValidate());
  }
);

function close(): void {
  emit('update:open', false);
  emit('cancel');
}

function patchProfile(patch: Partial<HostUserProfileWrite>): void {
  emit('update:profile', { ...props.profile, ...patch });
}

function updateSelectedRoleIds(value: Array<string | number>): void {
  emit('update:selectedRoleIds', value.map(String));
}

function updateGender(value: string | number | boolean | null | undefined): void {
  patchProfile({ gender: typeof value === 'string' && value ? value : null });
}

function onUsernameInput(value: string): void {
  basicForm.username = value;
  fieldErrors.username = validateUsername();
  emit('update:username', value);
}

function onDisplayNameInput(value: string): void {
  basicForm.displayName = value;
  fieldErrors.displayName = validateDisplayName();
  emit('update:displayName', value);
}

function onPasswordInput(value: string): void {
  basicForm.password = value;
  fieldErrors.password = validatePassword();
  emit('update:password', value);
}

function onNicknameInput(value: string): void {
  basicForm.nickname = value;
  fieldErrors.nickname = validateNickname();
  patchProfile({ nickname: value || null });
}

function onPhoneInput(value: string): void {
  basicForm.phoneNumber = value;
  fieldErrors.phoneNumber = validatePhoneNumber();
  patchProfile({ phoneNumber: value || null });
}

function onEmailInput(value: string): void {
  basicForm.email = value;
  fieldErrors.email = validateEmail();
  patchProfile({ email: value || null });
}

function onEmployeeNumberInput(value: string): void {
  basicForm.employeeNumber = value;
  fieldErrors.employeeNumber = validateEmployeeNumber();
  patchProfile({ employeeNumber: value || null });
}

function onRemarkInput(value: string): void {
  basicForm.remark = value;
  fieldErrors.remark = validateRemark();
  patchProfile({ remark: value || null });
}

function syncTrimmedValuesToParent(): void {
  const username = basicForm.username.trim();
  const displayName = basicForm.displayName.trim();
  emit('update:username', username);
  emit('update:displayName', displayName);
  if (props.mode === 'create') {
    emit('update:password', basicForm.password);
  }
  emit('update:profile', {
    ...props.profile,
    nickname: basicForm.nickname.trim() || null,
    phoneNumber: basicForm.phoneNumber.trim() || null,
    email: basicForm.email.trim() || null,
    employeeNumber: basicForm.employeeNumber.trim() || null,
    remark: basicForm.remark.trim() || null
  });
}

function validateBasicForm(): boolean {
  return applyFieldErrors();
}

async function onSubmitClick(): Promise<void> {
  if (props.mode === 'edit' && props.activeTab === 'roles') {
    emit('submit');
    return;
  }

  await nextTick();
  if (!validateBasicForm()) {
    if (props.activeTab !== 'basic') {
      emit('update:activeTab', 'basic');
    }
    return;
  }

  syncTrimmedValuesToParent();
  emit('submit');
}

defineExpose({
  validateBasicForm,
  onSubmitClick
});
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

    <el-form
      ref="basicFormRef"
      data-testid="users-editor-form"
      :model="basicForm"
      label-width="96px"
      class="users-editor-dialog__form"
    >
      <el-tabs
        :model-value="activeTab"
        class="users-editor-dialog__tabs"
        @update:model-value="emit('update:activeTab', $event as typeof activeTab)"
      >
        <el-tab-pane :label="translate('users.tabBasic')" name="basic">
          <div class="users-editor-dialog__grid">
            <el-form-item
              :label="translate('users.username')"
              prop="username"
              required
              :error="fieldErrors.username || undefined"
            >
              <el-input
                v-model="basicForm.username"
                :disabled="mode === 'edit'"
                :placeholder="translate('users.usernamePlaceholder')"
                @update:model-value="onUsernameInput"
              />
            </el-form-item>
            <el-form-item
              :label="translate('users.realName')"
              prop="displayName"
              required
              :error="fieldErrors.displayName || undefined"
            >
              <el-input
                v-model="basicForm.displayName"
                :placeholder="translate('users.displayNamePlaceholder')"
                @update:model-value="onDisplayNameInput"
              />
            </el-form-item>
            <el-form-item
              v-if="canManageProfile"
              :label="translate('users.nickname')"
              prop="nickname"
              :error="fieldErrors.nickname || undefined"
            >
              <el-input
                v-model="basicForm.nickname"
                @update:model-value="onNicknameInput"
              />
            </el-form-item>
            <el-form-item
              v-if="canManageProfile"
              :label="translate('users.phone')"
              prop="phoneNumber"
              :error="fieldErrors.phoneNumber || undefined"
            >
              <el-input
                v-model="basicForm.phoneNumber"
                @update:model-value="onPhoneInput"
              />
            </el-form-item>
            <el-form-item
              v-if="canManageProfile"
              :label="translate('users.email')"
              prop="email"
              :error="fieldErrors.email || undefined"
            >
              <el-input
                v-model="basicForm.email"
                @update:model-value="onEmailInput"
              />
            </el-form-item>
            <el-form-item v-if="canManageProfile" :label="translate('users.gender')">
              <el-radio-group
                :model-value="profile.gender ?? ''"
                @update:model-value="updateGender"
              >
                <el-radio value="male">{{ translate('users.genderMale') }}</el-radio>
                <el-radio value="female">{{ translate('users.genderFemale') }}</el-radio>
              </el-radio-group>
            </el-form-item>
            <el-form-item
              v-if="canManageProfile"
              :label="translate('users.employeeNumber')"
              prop="employeeNumber"
              :error="fieldErrors.employeeNumber || undefined"
            >
              <el-input
                v-model="basicForm.employeeNumber"
                @update:model-value="onEmployeeNumberInput"
              />
            </el-form-item>
            <el-form-item
              v-if="mode === 'create'"
              :label="translate('users.password')"
              prop="password"
              required
              :error="fieldErrors.password || undefined"
            >
              <el-input
                v-model="basicForm.password"
                type="password"
                show-password
                :placeholder="translate('users.passwordPlaceholder')"
                @update:model-value="onPasswordInput"
              />
            </el-form-item>
            <el-form-item v-else :label="translate('users.status')">
              <el-input
                :model-value="user?.isActive ? translate('users.active') : translate('users.inactive')"
                disabled
              />
            </el-form-item>
            <el-form-item
              v-if="canManageProfile"
              class="users-editor-dialog__full"
              :label="translate('users.remark')"
              prop="remark"
              :error="fieldErrors.remark || undefined"
            >
              <el-input
                v-model="basicForm.remark"
                type="textarea"
                :rows="2"
                @update:model-value="onRemarkInput"
              />
            </el-form-item>
          </div>
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
          @update:model-value="updateSelectedRoleIds"
        />
      </el-tab-pane>

      <el-tab-pane
        v-if="canManageOrganizations"
        :label="translate('users.tabOrg')"
        name="org"
      >
        <div class="users-editor-dialog__form">
          <div class="users-editor-dialog__grid users-editor-dialog__grid--single">
            <el-form-item :label="translate('users.primaryOrg')">
              <el-tree-select
                :model-value="primaryUnitId || undefined"
                :data="orgUnitTreeOptions"
                check-strictly
                filterable
                clearable
                :render-after-expand="false"
                :placeholder="translate('users.selectPrimaryOrg')"
                style="width: 100%"
                @update:model-value="emit('update:primaryUnitId', $event ?? '')"
              />
            </el-form-item>
            <el-form-item :label="translate('users.subsidiaryOrgs')">
              <el-tree-select
                :model-value="subsidiaryUnitIds"
                :data="subsidiaryUnitTreeOptions"
                multiple
                check-strictly
                filterable
                clearable
                collapse-tags
                collapse-tags-tooltip
                :render-after-expand="false"
                :placeholder="translate('users.selectSubsidiaryOrgs')"
                style="width: 100%"
                @update:model-value="emit('update:subsidiaryUnitIds', $event ?? [])"
              />
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
        </div>
      </el-tab-pane>

      <el-tab-pane
        v-if="canManageProfile"
        :label="translate('users.tabProfile')"
        name="profile"
      >
        <div class="users-editor-dialog__form users-editor-dialog__form--profile">
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
        </div>
      </el-tab-pane>

      <el-tab-pane
        v-if="mode === 'edit'"
        :label="translate('users.tabBinding')"
        name="binding"
      >
        <p class="users-editor-dialog__empty">{{ translate('users.bindingEmpty') }}</p>
      </el-tab-pane>
      </el-tabs>
    </el-form>

    <template #footer>
      <div class="users-editor-dialog__footer">
        <el-button @click="close">{{ translate('users.cancel') }}</el-button>
        <el-button
          v-if="mode === 'create' ? canCreate : canUpdate || canAssignRoles"
          type="primary"
          :loading="saving"
          data-testid="users-editor-submit"
          @click="onSubmitClick"
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

.users-editor-dialog__form--profile :deep(.el-form-item__label) {
  width: 120px;
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
