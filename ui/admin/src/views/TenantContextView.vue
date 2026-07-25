<script setup lang="ts">

import { computed, ref } from 'vue';

import { ElButton, ElCard, ElTag } from 'element-plus';

import {

  isFullNetProblemDetails,

  type FullNetProblemDetails

} from '@fullnet/client-contracts';

import { useSessionStore } from '../auth/session';

import { useAdminI18n } from '../i18n/adminI18n';



const session = useSessionStore();

const { t } = useAdminI18n();

const problem = ref<FullNetProblemDetails>();

const pendingTenantId = ref<string | null>();

const canSwitch = computed(() => session.can('tenancy.tenants.switch'));



async function selectContext(tenantId: string | null): Promise<void> {

  if (session.switching) {

    return;

  }



  pendingTenantId.value = tenantId;

  problem.value = undefined;

  try {

    await session.switchTenant(tenantId);

  } catch (error: unknown) {

    problem.value = isFullNetProblemDetails(error)

      ? error

      : {

          status: 500,

          code: 'client.context_switch_failed',

          title: t('shell.contextSwitchFailed')

        };

  } finally {

    pendingTenantId.value = undefined;

  }

}

</script>



<template>

  <section class="tenant-context-view art-page-stack art-full-height">

    <div class="tenant-context-view__toolbar">

      <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('tenant.title') }}</h1>

      <span class="art-page-badge">{{ t('tenant.sessionBound') }}</span>

    </div>



    <section class="art-context-banner" :aria-label="t('tenant.currentAria')">

      <div class="art-context-banner__label">

        <i aria-hidden="true" />

        <span>{{ t('tenant.currentLabel') }}</span>

      </div>

      <strong translate="no">{{ session.currentContextName }}</strong>

      <code translate="no">{{ session.currentUser?.scope }}</code>

      <el-button

        v-if="canSwitch && session.currentUser?.tenantId"

        :loading="session.switching && pendingTenantId === null"

        :disabled="session.switching"

        data-testid="return-host"

        @click="selectContext(null)"

      >

        {{ t('tenant.returnHost') }}

      </el-button>

    </section>



    <div v-if="problem" class="art-inline-alert" role="alert">

      <strong translate="no">{{ problem.code }}</strong>

      <span>{{ problem.title }}</span>

      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>

    </div>



    <el-card class="art-table-card" shadow="never" aria-labelledby="tenant-directory-title">

      <template #header>

        <div class="art-table-card__header">

          <h2 id="tenant-directory-title">{{ t('tenant.availableTitle') }}</h2>

          <span class="art-table-card__count">{{ t('tenant.activeCount', { count: session.availableTenants.length }) }}</span>

        </div>

      </template>



      <div class="art-tenant-grid">

        <article

          v-for="tenant in session.availableTenants"

          :key="tenant.id"

          class="art-tenant-card"

          :class="{ 'is-active': session.currentUser?.tenantId === tenant.id }"

        >

          <span class="art-tenant-card__code" translate="no">{{ tenant.identifier }}</span>

          <h3 translate="no">{{ tenant.name }}</h3>

          <p translate="no">{{ tenant.domain }}</p>

          <div class="art-tenant-card__footer">

            <small>{{ session.currentUser?.tenantId === tenant.id ? t('tenant.current') : t('tenant.available') }}</small>

            <el-button
              v-if="canSwitch && session.currentUser?.tenantId !== tenant.id"
              class="art-contrast-primary"
              :data-tenant-id="tenant.id"
              :loading="session.switching && pendingTenantId === tenant.id"
              :disabled="session.switching"
              type="primary"
              @click="selectContext(tenant.id)"
            >
              {{ t('tenant.enter') }}
            </el-button>

          </div>

        </article>

        <p v-if="session.availableTenants.length === 0" class="art-empty-state art-tenant-grid__empty">

          {{ t('tenant.directoryEmpty') }}

        </p>

      </div>

    </el-card>

  </section>

</template>



<style scoped>

.tenant-context-view__toolbar {

  display: flex;

  align-items: center;

  justify-content: space-between;

  gap: 12px;

  margin-bottom: 4px;

}



.art-tenant-grid__empty {

  grid-column: 1 / -1;
}
</style>
