<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue';
import { ElButton, ElDialog, ElForm, ElFormItem, ElInput, ElInputNumber } from 'element-plus';
import 'element-plus/theme-chalk/el-input-number.css';
import type { EnterpriseRequestAggregateResponse } from '../api/enterprise-request-client/index.generated';
import * as api from '../api/enterprise-request-aggregate';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { enterpriseEditorText } from '../i18n/enterprise-request-editor';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { buildRequestAggregateUpdate, editRequestLine, requestLineInput, validRequestEditor, type EditableRequestLine } from '../views/enterprise-requests/aggregate-editor';

const props = defineProps<{ requestId?: string }>();
const open = defineModel<boolean>({ default: false });
const emit = defineEmits<{ saved: [] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof enterpriseEditorText>[1]) => enterpriseEditorText(locale.value, key);
const can = (suffix: string) => session.can(`enterprise_request.enterprise_requests.${suffix}`);
const allowed = computed(() => !session.switching && !!session.currentUser?.tenantId && can('read')
  && (props.requestId ? session.can('enterprise_request.enterprise_request_lines.read') : can('create')));
const original = ref<EnterpriseRequestAggregateResponse>();
const editable = computed(() => allowed.value && (props.requestId ? original.value?.master.status === 'Draft' && can('update') : can('create')));
const busy = ref(false), rows = ref<EditableRequestLine[]>([]);
const form = reactive({ organizationUnitId: '', requestNumber: '', title: '', status: 'Draft', totalAmount: '0', applicantUserId: '' });
const statusLabel = computed(() => {
  switch (form.status) {
    case 'Draft': return text('draft');
    case 'Submitted': return text('submitted');
    case 'Approved': return text('approved');
    case 'Rejected': return text('rejected');
    case 'Cancelled': return text('cancelled');
    default: return form.status;
  }
});
let generation = 0, cancellation = new AbortController();
function reset() {
  generation++; cancellation.abort(); cancellation = new AbortController(); original.value = undefined; rows.value = []; busy.value = false;
  Object.assign(form, { organizationUnitId: '', requestNumber: '', title: '', status: 'Draft', totalAmount: '0', applicantUserId: session.currentUser?.id ?? '' });
}
async function act(action: () => Promise<void>) {
  if (busy.value || !allowed.value || !open.value) return;
  const current = generation; busy.value = true;
  try { await action(); }
  catch (error) { if (current === generation) showProblem(error, text('failure')); }
  finally { if (current === generation) busy.value = false; }
}
watch([open, () => props.requestId], () => {
  reset();
  if (open.value && allowed.value && props.requestId) {
    const current = generation, id = props.requestId;
    void act(async () => {
      const result = await api.getEnterpriseRequestAggregate(id, cancellation.signal);
      if (current !== generation) return;
      original.value = result;
      const master = result.master;
      Object.assign(form, { organizationUnitId: master.organizationUnitId, requestNumber: master.requestNumber, title: master.title,
        status: master.status, totalAmount: master.totalAmount, applicantUserId: master.applicantUserId });
      rows.value = result.details.map(editRequestLine);
    });
  }
}, { immediate: true, flush: 'sync' });
watch(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId, session.switching,
  session.currentUser?.permissions.join('|')], () => { open.value = false; reset(); }, { flush: 'sync' });
onBeforeUnmount(reset);
function add() {
  if (!editable.value || busy.value || rows.value.length >= 128) return;
  rows.value.push({ key: crypto.randomUUID(), lineNumber: Math.max(0, ...rows.value.map(row => row.lineNumber)) + 1,
    itemDescription: '', quantity: '1', unitPrice: '0', lineAmount: '0' });
}
function remove(key: string) { if (editable.value && !busy.value) rows.value = rows.value.filter(row => row.key !== key); }
async function save() {
  if (!editable.value) return;
  if (!validRequestEditor(form, rows.value)) { showWarning(text('invalid')); return; }
  const current = generation;
  if (props.requestId && original.value) {
    await api.updateEnterpriseRequestAggregate(props.requestId, buildRequestAggregateUpdate(form, original.value, rows.value), cancellation.signal);
  } else {
    await api.createEnterpriseRequestAggregate(form.organizationUnitId, { master: { requestNumber: form.requestNumber, title: form.title,
      status: 'Draft', totalAmount: form.totalAmount, applicantUserId: form.applicantUserId }, details: rows.value.map(requestLineInput) }, cancellation.signal);
  }
  if (current !== generation) return;
  showSuccess(text('saved')); emit('saved'); open.value = false;
}
</script>

<template>
  <el-dialog v-model="open" :title="text('title')" width="min(880px, calc(100vw - 32px))" append-to-body destroy-on-close
    :show-close="!busy" :close-on-click-modal="false" :close-on-press-escape="!busy">
    <el-form v-if="allowed && (!requestId || original)" class="request-aggregate-form" label-position="top" :aria-busy="busy" @submit.prevent="act(save)">
      <p v-if="!editable" role="status">{{ text('readonly') }}</p>
      <div class="request-master-grid">
        <el-form-item :label="text('organization')" :required="!requestId"><el-input v-model="form.organizationUnitId" :readonly="!!requestId || !editable" :disabled="busy" data-testid="aggregate-organization" /></el-form-item>
        <el-form-item :label="text('number')" :required="editable"><el-input v-model="form.requestNumber" :readonly="!editable" :disabled="busy" maxlength="64" data-testid="aggregate-number" /></el-form-item>
        <el-form-item :label="text('subject')" :required="editable"><el-input v-model="form.title" :readonly="!editable" :disabled="busy" maxlength="200" data-testid="aggregate-title" /></el-form-item>
        <el-form-item :label="text('applicant')" :required="editable"><el-input v-model="form.applicantUserId" :readonly="!editable" :disabled="busy" data-testid="aggregate-applicant" /></el-form-item>
        <el-form-item :label="text('total')" :required="editable"><el-input v-model="form.totalAmount" :readonly="!editable" :disabled="busy" inputmode="decimal" data-testid="aggregate-total" /></el-form-item>
        <el-form-item :label="text('status')"><span>{{ statusLabel }}</span></el-form-item>
      </div>
      <h3>{{ text('lines') }}</h3><p>{{ text('limit') }}</p>
      <el-button v-if="editable" :disabled="busy || rows.length >= 128" data-testid="aggregate-add-line" @click="add">{{ text('add') }}</el-button>
      <article v-for="row in rows" :key="row.key" class="request-line" data-testid="aggregate-line">
        <el-form-item :label="text('line')" :required="editable"><el-input-number v-model="row.lineNumber" :min="1" :max="2147483647" :precision="0" :disabled="busy || !editable" :aria-label="text('line')" /></el-form-item>
        <el-form-item :label="text('description')" :required="editable"><el-input v-model="row.itemDescription" :readonly="!editable" :disabled="busy" maxlength="200" data-testid="aggregate-line-description" /></el-form-item>
        <el-form-item :label="text('quantity')" :required="editable"><el-input v-model="row.quantity" :readonly="!editable" :disabled="busy" inputmode="decimal" data-testid="aggregate-line-quantity" /></el-form-item>
        <el-form-item :label="text('price')" :required="editable"><el-input v-model="row.unitPrice" :readonly="!editable" :disabled="busy" inputmode="decimal" data-testid="aggregate-line-price" /></el-form-item>
        <el-form-item :label="text('amount')" :required="editable"><el-input v-model="row.lineAmount" :readonly="!editable" :disabled="busy" inputmode="decimal" data-testid="aggregate-line-amount" /></el-form-item>
        <el-button v-if="editable" :disabled="busy" data-testid="aggregate-remove-line" @click="remove(row.key)">{{ text('remove') }}</el-button>
      </article>
    </el-form>
    <template #footer>
      <el-button :disabled="busy" @click="open = false">{{ text('close') }}</el-button>
      <el-button v-if="editable" type="primary" :loading="busy" data-testid="aggregate-save" @click="act(save)">{{ text('save') }}</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.request-aggregate-form { max-height: 65vh; overflow-y: auto; padding: 2px 8px 2px 2px; }
.request-master-grid, .request-line { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.request-line { margin-top: 16px; padding: 16px; border: 1px solid var(--el-border-color); border-radius: 6px; }
.request-line .el-button { justify-self: start; align-self: end; }
@media (max-width: 600px) { .request-master-grid, .request-line { grid-template-columns: minmax(0, 1fr); } }
</style>
