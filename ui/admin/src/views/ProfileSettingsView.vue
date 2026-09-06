<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect
} from 'element-plus';
import {
  isFullNetProblemDetails,
  isMaskedHostUserIdCardNumber,
  isMaskedHostUserPhoneNumber,
  type HostUserProfileWriteRequest
} from '@fullnet/client-contracts';
import { getSelfServiceProfile, updateSelfServiceProfile } from '../api/me';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  HOST_USER_PROFILE_DICT_CODES,
  loadHostUserProfileDictOptions,
  type HostUserProfileDictOption
} from '../users/profile-dict-options';

defineOptions({ name: 'ProfileSettingsView' });

const { t } = useAdminI18n();
const loading = ref(false);
const saving = ref(false);
const username = ref('');
const displayName = ref('');
const userVersion = ref(0);
const readableFieldKeys = ref<string[]>([]);
const writableFieldKeys = ref<string[]>([]);
const profile = reactive<HostUserProfileWriteRequest>({
  fieldKeys: [],
  nickname: null,
  phoneNumber: null,
  email: null,
  employeeNumber: null,
  gender: null,
  joinDateUtc: null,
  sortOrder: null,
  idCardType: null,
  idCardNumber: null,
  birthDate: null,
  ethnicity: null,
  address: null,
  graduatedSchool: null,
  educationLevel: null,
  politicalStatus: null,
  officePhone: null,
  emergencyContact: null,
  emergencyContactRelation: null,
  emergencyContactPhone: null,
  emergencyContactAddress: null,
  remark: null,
  version: null
});
const profileDictOptions = ref<Record<string, HostUserProfileDictOption[]>>({
  [HOST_USER_PROFILE_DICT_CODES.ethnicity]: [],
  [HOST_USER_PROFILE_DICT_CODES.educationLevel]: [],
  [HOST_USER_PROFILE_DICT_CODES.emergencyContactRelation]: []
});

const canEditDisplayName = computed(() => true);
const hasWritableProfileFields = computed(() => writableFieldKeys.value.length > 0);

function hasField(fieldKey: string, writable = false): boolean {
  const keys = writable ? writableFieldKeys.value : readableFieldKeys.value;
  return keys.includes(fieldKey);
}

function assignProfile(source: HostUserProfileWriteRequest): void {
  profile.fieldKeys = [...writableFieldKeys.value];
  profile.nickname = source.nickname ?? null;
  profile.phoneNumber = source.phoneNumber ?? null;
  profile.email = source.email ?? null;
  profile.employeeNumber = source.employeeNumber ?? null;
  profile.gender = source.gender ?? null;
  profile.joinDateUtc = source.joinDateUtc ?? null;
  profile.sortOrder = source.sortOrder ?? null;
  profile.idCardType = source.idCardType ?? null;
  profile.idCardNumber = source.idCardNumber ?? null;
  profile.birthDate = source.birthDate ?? null;
  profile.ethnicity = source.ethnicity ?? null;
  profile.address = source.address ?? null;
  profile.graduatedSchool = source.graduatedSchool ?? null;
  profile.educationLevel = source.educationLevel ?? null;
  profile.politicalStatus = source.politicalStatus ?? null;
  profile.officePhone = source.officePhone ?? null;
  profile.emergencyContact = source.emergencyContact ?? null;
  profile.emergencyContactRelation = source.emergencyContactRelation ?? null;
  profile.emergencyContactPhone = source.emergencyContactPhone ?? null;
  profile.emergencyContactAddress = source.emergencyContactAddress ?? null;
  profile.remark = source.remark ?? null;
  profile.version = source.version ?? null;
}

async function loadProfile(): Promise<void> {
  loading.value = true;
  try {
    const response = await getSelfServiceProfile();
    username.value = response.username;
    displayName.value = response.displayName;
    userVersion.value = response.userVersion;
    readableFieldKeys.value = [...response.readableFieldKeys];
    writableFieldKeys.value = [...response.writableFieldKeys];
    if (response.profile) {
      assignProfile(response.profile);
    }
  } catch (error: unknown) {
    if (isFullNetProblemDetails(error)) {
      ElMessage.error(error.title || error.detail || t('profileSettings.loadFailed'));
      return;
    }
    ElMessage.error(t('profileSettings.loadFailed'));
  } finally {
    loading.value = false;
  }
}

function profilePayloadForSubmit(): HostUserProfileWriteRequest | null {
  if (!hasWritableProfileFields.value) {
    return null;
  }

  const fieldKeys = writableFieldKeys.value.filter(fieldKey =>
    fieldKey !== 'phone_number' && fieldKey !== 'id_card_number' && fieldKey !== 'id_card_type'
  );
  return {
    fieldKeys: [...fieldKeys].sort(),
    nickname: hasField('nickname', true) ? profile.nickname ?? null : null,
    phoneNumber: null,
    email: hasField('email', true) ? profile.email ?? null : null,
    employeeNumber: null,
    gender: hasField('gender', true) ? profile.gender ?? null : null,
    joinDateUtc: null,
    sortOrder: null,
    idCardType: null,
    idCardNumber: null,
    birthDate: hasField('birth_date', true) ? profile.birthDate ?? null : null,
    ethnicity: hasField('ethnicity', true) ? profile.ethnicity ?? null : null,
    educationLevel: hasField('education_level', true) ? profile.educationLevel ?? null : null,
    politicalStatus: hasField('political_status', true) ? profile.politicalStatus ?? null : null,
    officePhone: hasField('office_phone', true) ? profile.officePhone ?? null : null,
    emergencyContact: hasField('emergency_contact', true) ? profile.emergencyContact ?? null : null,
    emergencyContactRelation: hasField('emergency_contact_relation', true)
      ? profile.emergencyContactRelation ?? null
      : null,
    emergencyContactPhone: hasField('emergency_contact_phone', true)
      ? profile.emergencyContactPhone ?? null
      : null,
    emergencyContactAddress: hasField('emergency_contact_address', true)
      ? profile.emergencyContactAddress ?? null
      : null,
    address: hasField('address', true) ? profile.address ?? null : null,
    graduatedSchool: hasField('graduated_school', true) ? profile.graduatedSchool ?? null : null,
    remark: hasField('remark', true) ? profile.remark ?? null : null,
    version: profile.version ?? null
  };
}

async function submit(): Promise<void> {
  const trimmedDisplayName = displayName.value.trim();
  if (canEditDisplayName.value && trimmedDisplayName.length === 0) {
    ElMessage.warning(t('profileSettings.displayNameRequired'));
    return;
  }

  saving.value = true;
  try {
    const response = await updateSelfServiceProfile({
      displayName: trimmedDisplayName,
      userVersion: userVersion.value,
      profile: profilePayloadForSubmit()
    });
    username.value = response.username;
    displayName.value = response.displayName;
    userVersion.value = response.userVersion;
    readableFieldKeys.value = [...response.readableFieldKeys];
    writableFieldKeys.value = [...response.writableFieldKeys];
    if (response.profile) {
      assignProfile(response.profile);
    }
    ElMessage.success(t('profileSettings.saveSuccess'));
  } catch (error: unknown) {
    if (isFullNetProblemDetails(error)) {
      ElMessage.error(error.title || error.detail || t('profileSettings.saveFailed'));
      return;
    }
    ElMessage.error(t('profileSettings.saveFailed'));
  } finally {
    saving.value = false;
  }
}

onMounted(async () => {
  try {
    profileDictOptions.value = await loadHostUserProfileDictOptions();
  } catch {
    // 字典缺失不阻断档案页；表单仍可编辑自由文本字段。
  }
  await loadProfile();
});
</script>

<template>
  <section class="profile-settings-view art-page-stack" :aria-busy="loading || saving">
    <header class="art-page-header">
      <div>
        <h1>{{ t('profileSettings.title') }}</h1>
        <p>{{ t('profileSettings.subtitle') }}</p>
      </div>
    </header>

    <ElCard class="art-card" v-loading="loading">
      <ElForm label-position="top" @submit.prevent="submit">
        <ElFormItem :label="t('profileSettings.username')">
          <ElInput :model-value="username" disabled data-testid="profile-settings-username" />
        </ElFormItem>

        <ElFormItem :label="t('profileSettings.displayName')">
          <ElInput
            v-model="displayName"
            maxlength="128"
            :disabled="!canEditDisplayName"
            data-testid="profile-settings-display-name"
          />
        </ElFormItem>

        <template v-if="hasField('nickname')">
          <ElFormItem :label="t('profileSettings.nickname')">
            <ElInput
              v-model="profile.nickname"
              :disabled="!hasField('nickname', true)"
              data-testid="profile-settings-nickname"
            />
          </ElFormItem>
        </template>

        <template v-if="hasField('phone_number')">
          <ElFormItem :label="t('profileSettings.phoneNumber')">
            <ElInput
              :model-value="profile.phoneNumber ?? ''"
              disabled
              data-testid="profile-settings-phone-number"
            />
            <small v-if="isMaskedHostUserPhoneNumber(profile.phoneNumber)" class="art-field-hint">
              {{ t('profileSettings.readOnlySensitiveHint') }}
            </small>
          </ElFormItem>
        </template>

        <template v-if="hasField('email')">
          <ElFormItem :label="t('profileSettings.email')">
            <ElInput
              v-model="profile.email"
              :disabled="!hasField('email', true)"
              data-testid="profile-settings-email"
            />
          </ElFormItem>
        </template>

        <template v-if="hasField('gender')">
          <ElFormItem :label="t('profileSettings.gender')">
            <ElSelect
              v-model="profile.gender"
              :disabled="!hasField('gender', true)"
              :teleported="false"
              clearable
              data-testid="profile-settings-gender"
            >
              <ElOption label="男" value="male" />
              <ElOption label="女" value="female" />
              <ElOption label="未知" value="unknown" />
            </ElSelect>
          </ElFormItem>
        </template>

        <template v-if="hasField('id_card_number')">
          <ElFormItem :label="t('profileSettings.idCardNumber')">
            <ElInput
              :model-value="profile.idCardNumber ?? ''"
              disabled
              data-testid="profile-settings-id-card-number"
            />
            <small v-if="isMaskedHostUserIdCardNumber(profile.idCardNumber)" class="art-field-hint">
              {{ t('profileSettings.readOnlySensitiveHint') }}
            </small>
          </ElFormItem>
        </template>

        <template v-if="hasField('address')">
          <ElFormItem :label="t('profileSettings.address')">
            <ElInput
              v-model="profile.address"
              :disabled="!hasField('address', true)"
              data-testid="profile-settings-address"
            />
          </ElFormItem>
        </template>

        <template v-if="hasField('emergency_contact')">
          <ElFormItem :label="t('profileSettings.emergencyContact')">
            <ElInput
              v-model="profile.emergencyContact"
              :disabled="!hasField('emergency_contact', true)"
              data-testid="profile-settings-emergency-contact"
            />
          </ElFormItem>
        </template>

        <ElFormItem>
          <ElButton
            type="primary"
            :loading="saving"
            data-testid="profile-settings-save"
            @click="submit"
          >
            {{ t('profileSettings.submit') }}
          </ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>
  </section>
</template>

<style scoped>
.art-field-hint {
  display: block;
  margin-top: 6px;
  color: var(--el-text-color-secondary);
}
</style>
