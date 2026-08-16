<script setup lang="ts">

import { SwitchButton } from '@element-plus/icons-vue';

import { ElButton, ElFormItem, ElOption, ElPopover, ElSelect } from 'element-plus';



defineOptions({ name: 'ArtUserMenu' });



defineProps<{

  displayName: string;

  roleLabel: string;

  logoutLabel: string;

  tenantSelectorLabel: string;

  selectedContext: string;

  hostContextValue: string;

  canReadTenants: boolean;

  canSwitchTenant: boolean;

  switching: boolean;

  availableTenants: Array<{ id: string; name: string }>;

}>();



const emit = defineEmits<{

  logout: [];

  switchTenant: [value: string];

}>();



const visible = defineModel<boolean>('open', { default: false });

</script>



<template>

  <el-popover

    v-model:visible="visible"

    placement="bottom-end"

    :width="260"

    :show-arrow="false"

    :offset="10"

    trigger="click"

    popper-class="art-user-menu-popper"

  >

    <template #reference>

      <button type="button" class="art-user-menu__trigger" :aria-label="displayName">

        <span aria-hidden="true">{{ displayName.slice(0, 1) }}</span>

      </button>

    </template>



    <div class="art-user-menu">

      <div class="art-user-menu__profile">

        <span class="art-user-menu__avatar" aria-hidden="true">{{ displayName.slice(0, 1) }}</span>

        <div>

          <strong>{{ displayName }}</strong>

          <small>{{ roleLabel }}</small>

        </div>

      </div>



      <div v-if="canReadTenants" class="art-user-menu__tenant">
        <el-form-item :label="tenantSelectorLabel" class="art-user-menu__tenant-field">
          <el-select
            data-testid="shell-tenant-select"
            :model-value="selectedContext"
            :disabled="switching || !canSwitchTenant"
            :aria-label="tenantSelectorLabel"
            size="small"
            @change="value => emit('switchTenant', String(value))"
          >
            <el-option label="Full.NET Host" :value="hostContextValue" />
            <el-option
              v-for="tenant in availableTenants"
              :key="tenant.id"
              :label="tenant.name"
              :value="tenant.id"
            />
          </el-select>
        </el-form-item>
      </div>



      <el-button

        class="art-user-menu__logout"

        :icon="SwitchButton"

        @click="emit('logout'); visible = false"

      >

        {{ logoutLabel }}

      </el-button>

    </div>

  </el-popover>

</template>



<style scoped>

.art-user-menu__trigger {

  display: grid;

  width: 34px;

  height: 34px;

  margin-right: 4px;

  padding: 0;

  border: 0;

  border-radius: 50%;

  background: linear-gradient(135deg, var(--art-theme-color), #79bbff);

  color: #fff;

  font-size: 13px;

  font-weight: 700;

  cursor: pointer;

}



.art-user-menu__profile {

  display: flex;

  gap: 12px;

  align-items: center;

  padding: 8px 0 16px;

}



.art-user-menu__avatar {

  display: grid;

  width: 40px;

  height: 40px;

  place-items: center;

  border-radius: 50%;

  background: linear-gradient(135deg, var(--art-theme-color), #79bbff);

  color: #fff;

  font-size: 16px;

  font-weight: 700;

}



.art-user-menu__profile strong,

.art-user-menu__profile small {

  display: block;

}



.art-user-menu__profile strong {

  font-size: 14px;

}



.art-user-menu__profile small {

  margin-top: 4px;

  color: var(--art-gray-500);

  font-size: 12px;

}



.art-user-menu__tenant {

  padding-bottom: 16px;

}



.art-user-menu__tenant label {

  display: block;

  margin-bottom: 8px;

  color: var(--art-gray-600);

  font-size: 12px;

}



.art-user-menu__tenant .el-select {

  width: 100%;

}



.art-user-menu__logout {

  width: 100%;

}

</style>

