<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useBlobPreview } from '../composables/useBlobPreview';
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
  type HostUserProfileResponse,
  type HostUserProfileWriteRequest
} from '@fullnet/client-contracts';
import {
  fetchProfileAvatarBlob,
  fetchProfileSignatureBlob,
  getSelfServiceProfile,
  removeProfileAvatar,
  removeProfileSignature,
  updateSelfServiceProfile,
  uploadProfileAvatar,
  uploadProfileSignature
} from '../api/me';
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
const uploadingAvatar = ref(false);
const uploadingSignature = ref(false);
const removingAvatar = ref(false);
const removingSignature = ref(false);
const avatarFileId = ref<string | null>(null);
const signatureFileId = ref<string | null>(null);
const avatarPreview = useBlobPreview();
const signaturePreview = useBlobPreview();
const avatarPreviewUrl = avatarPreview.url;
const signaturePreviewUrl = signaturePreview.url;
const username = ref('');
const displayName = ref('');
const userVersion = ref(0);
const readableFieldKeys = ref<string[]>([]);
const writableFieldKeys = ref<string[]>([]);
const profile = reactive<{ -readonly [K in keyof HostUserProfileWriteRequest]: HostUserProfileWriteRequest[K] }>({
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

async function refreshMediaPreviews(): Promise<void> {
  avatarPreview.clear();
  signaturePreview.clear();

  if (avatarFileId.value) {
    try {
      await avatarPreview.load(fetchProfileAvatarBlob);
    } catch {
      // 头像预览失败不影响资料操作。
    }
  }

  if (signatureFileId.value) {
    try {
      await signaturePreview.load(fetchProfileSignatureBlob);
    } catch {
      // 签名预览失败不影响资料操作。
    }
  }
}

async function handleAvatarSelected(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (!file) {
    return;
  }

  uploadingAvatar.value = true;
  try {
    const response = await uploadProfileAvatar(file);
    avatarFileId.value = response.avatarFileId;
    signatureFileId.value = response.signatureFileId;
    await refreshMediaPreviews();
    ElMessage.success(t('profileSettings.avatarUploadSuccess'));
  } catch (error: unknown) {
    if (isFullNetProblemDetails(error)) {
      ElMessage.error(error.title || error.detail || t('profileSettings.avatarUploadFailed'));
      return;
    }
    ElMessage.error(t('profileSettings.avatarUploadFailed'));
  } finally {
    uploadingAvatar.value = false;
  }
}

async function handleSignatureSelected(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (!file) {
    return;
  }

  uploadingSignature.value = true;
  try {
    const response = await uploadProfileSignature(file);
    avatarFileId.value = response.avatarFileId;
    signatureFileId.value = response.signatureFileId;
    await refreshMediaPreviews();
    ElMessage.success(t('profileSettings.signatureUploadSuccess'));
  } catch (error: unknown) {
    if (isFullNetProblemDetails(error)) {
      ElMessage.error(error.title || error.detail || t('profileSettings.signatureUploadFailed'));
      return;
    }
    ElMessage.error(t('profileSettings.signatureUploadFailed'));
  } finally {
    uploadingSignature.value = false;
  }
}

async function removeAvatar(): Promise<void> {
  removingAvatar.value = true;
  try {
    const response = await removeProfileAvatar();
    avatarFileId.value = response.avatarFileId;
    await refreshMediaPreviews();
    ElMessage.success(t('profileSettings.avatarRemoveSuccess'));
  } catch {
    ElMessage.error(t('profileSettings.avatarRemoveFailed'));
  } finally {
    removingAvatar.value = false;
  }
}

async function removeSignature(): Promise<void> {
  removingSignature.value = true;
  try {
    const response = await removeProfileSignature();
    signatureFileId.value = response.signatureFileId;
    await refreshMediaPreviews();
    ElMessage.success(t('profileSettings.signatureRemoveSuccess'));
  } catch {
    ElMessage.error(t('profileSettings.signatureRemoveFailed'));
  } finally {
    removingSignature.value = false;
  }
}
const hasWritableProfileFields = computed(() => writableFieldKeys.value.length > 0);

function hasField(fieldKey: string, writable = false): boolean {
  const keys = writable ? writableFieldKeys.value : readableFieldKeys.value;
  return keys.includes(fieldKey);
}

function assignProfile(source: HostUserProfileResponse): void {
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
    avatarFileId.value = response.avatarFileId;
    signatureFileId.value = response.signatureFileId;
    if (response.profile) {
      assignProfile(response.profile);
    }
    await refreshMediaPreviews();
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


const canEditDisplayName = computed(() => true);
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
      <div class="profile-media-grid">
        <section class="profile-media-block">
          <h2>{{ t('profileSettings.avatar') }}</h2>
          <div class="profile-media-preview" data-testid="profile-settings-avatar-preview">
            <img v-if="avatarPreviewUrl" :src="avatarPreviewUrl" alt="">
            <span v-else>{{ t('profileSettings.mediaEmpty') }}</span>
          </div>
          <div class="profile-media-actions">
            <label class="profile-media-upload">
              <input
                type="file"
                accept="image/jpeg,image/png,image/webp"
                data-testid="profile-settings-avatar-input"
                :disabled="uploadingAvatar || removingAvatar"
                @change="handleAvatarSelected"
              >
              <ElButton :loading="uploadingAvatar">{{ t('profileSettings.avatarUpload') }}</ElButton>
            </label>
            <ElButton
              v-if="avatarFileId"
              plain
              :loading="removingAvatar"
              data-testid="profile-settings-avatar-remove"
              @click="removeAvatar"
            >
              {{ t('profileSettings.avatarRemove') }}
            </ElButton>
          </div>
        </section>

        <section class="profile-media-block">
          <h2>{{ t('profileSettings.signature') }}</h2>
          <div class="profile-media-preview" data-testid="profile-settings-signature-preview">
            <img v-if="signaturePreviewUrl" :src="signaturePreviewUrl" alt="">
            <span v-else>{{ t('profileSettings.mediaEmpty') }}</span>
          </div>
          <div class="profile-media-actions">
            <label class="profile-media-upload">
              <input
                type="file"
                accept="image/jpeg,image/png"
                data-testid="profile-settings-signature-input"
                :disabled="uploadingSignature || removingSignature"
                @change="handleSignatureSelected"
              >
              <ElButton :loading="uploadingSignature">{{ t('profileSettings.signatureUpload') }}</ElButton>
            </label>
            <ElButton
              v-if="signatureFileId"
              plain
              :loading="removingSignature"
              data-testid="profile-settings-signature-remove"
              @click="removeSignature"
            >
              {{ t('profileSettings.signatureRemove') }}
            </ElButton>
          </div>
        </section>
      </div>

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
.profile-media-grid {
  display: grid;
  gap: 24px;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  margin-bottom: 24px;
}

.profile-media-block h2 {
  margin: 0 0 12px;
  font-size: 16px;
}

.profile-media-preview {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 120px;
  margin-bottom: 12px;
  border: 1px dashed var(--el-border-color);
  border-radius: 8px;
  background: var(--el-fill-color-light);
  overflow: hidden;
}

.profile-media-preview img {
  display: block;
  max-width: 100%;
  max-height: 160px;
  object-fit: contain;
}

.profile-media-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.profile-media-upload input {
  display: none;
}

.art-field-hint {
  display: block;
  margin-top: 6px;
  color: var(--el-text-color-secondary);
}
</style>
