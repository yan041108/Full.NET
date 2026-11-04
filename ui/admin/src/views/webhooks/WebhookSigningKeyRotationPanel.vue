<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCheckbox, ElDialog, ElForm, ElFormItem, ElInput } from 'element-plus';
import { isFullNetProblemDetails, type WebhookSubscription, type WebhookSigningKeyRotationReceipt } from '@fullnet/client-contracts';
import { useSessionStore } from '../../auth/session';
import { useAdminI18n } from '../../i18n/adminI18n';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { getPendingWebhookSigningKeyRotation, getWebhookSigningKeyRotation, prepareWebhookSigningKeyRotation, activateWebhookSigningKeyRotation, cancelWebhookSigningKeyRotation } from '../../api/webhook-subscriptions';

const props=defineProps<{modelValue:boolean;subscription:WebhookSubscription}>();
const emit=defineEmits<{'update:modelValue':[value:boolean];changed:[receipt:WebhookSigningKeyRotationReceipt];unknown:[value:boolean];refresh:[]}>();
const session=useSessionStore(),{t}=useAdminI18n(),read='webhooks.subscriptions.read',write='webhooks.subscriptions.rotate_signing_key';
const canRead=()=>!session.switching&&!!session.currentUser?.tenantId&&session.currentUser.tenantId.toLowerCase()===props.subscription.tenantId.toLowerCase()&&session.can(read);
const canWrite=()=>canRead()&&session.can(write);
type Phase='prepare'|'activate'|'cancel';type Operation={operationId:string;phase:Phase;version:number;signingKeyId?:string};
const secret=ref(''),receipt=ref<WebhookSigningKeyRotationReceipt>(),operation=ref<Operation>(),unknown=ref(false),busy=ref(false),checked=ref(false),problem=ref(''),acknowledged=ref(false),confirmation=ref<Operation>(),epoch=ref(0);
const identity=()=>JSON.stringify([session.currentUser?.id,session.currentUser?.sessionId,session.currentUser?.tenantId,props.subscription.id]);
let owner=identity(),pending:ReturnType<typeof scope.begin>,dispatched=false;
function markUnknown(){if(dispatched&&operation.value){unknown.value=true;emit('unknown',true);}dispatched=false;}
function stop(){markUnknown();pending?.cancel();pending=undefined;busy.value=false;secret.value='';confirmation.value=undefined;acknowledged.value=false;epoch.value++;}
const scope=useAuthorizedViewScope(session,()=>{stop();if(owner!==identity()){owner=identity();receipt.value=undefined;operation.value=undefined;unknown.value=false;checked.value=false;problem.value='';emit('unknown',false);emit('update:modelValue',false);}},()=>{if(props.modelValue&&canRead())void readReceipt();});
function accept(row:WebhookSigningKeyRotationReceipt){
  if(row.subscriptionId.toLowerCase()!==props.subscription.id.toLowerCase()||row.tenantId.toLowerCase()!==props.subscription.tenantId.toLowerCase()||operation.value&&row.operationId.toLowerCase()!==operation.value.operationId.toLowerCase())throw Error('client.invalid_webhook_rotation_identity');
  if(receipt.value?.operationId===row.operationId&&receipt.value.stateKey!=='prepared'&&row.stateKey==='prepared')throw Error('client.stale_webhook_rotation');
  receipt.value=row;checked.value=true;
  // 准备事实可确认原准备；未完成记录不能证明一个未知激活/取消未提交。
  if(!operation.value||operation.value.phase==='prepare'||row.stateKey!=='prepared'){operation.value=undefined;unknown.value=false;emit('unknown',false);emit('changed',row);}
}
async function readReceipt(){
  if(!props.modelValue||!canRead()||busy.value)return;const request=scope.begin(read);if(!request)return;pending=request;busy.value=true;problem.value='';const original=operation.value?.operationId??receipt.value?.operationId;
  try{const row=original?await getWebhookSigningKeyRotation(props.subscription.id,original,request.signal):await getPendingWebhookSigningKeyRotation(props.subscription.id,request.signal);if(request.current()&&props.modelValue&&canRead()){accept(row);emit('refresh');}}
  catch(error){if(request.current()&&props.modelValue){if(!original&&isFullNetProblemDetails(error)&&error.status===404&&error.code==='webhooks.signing_key_rotation.not_found'){checked.value=true;emit('refresh');}else problem.value=t('webhooks.rotation.readFailed');}}
  finally{if(request.current()){busy.value=false;pending=undefined;}request.finish();}
}
function close(){stop();emit('update:modelValue',false);}
function openComplete(phase:'activate'|'cancel'){
  if(!canWrite()||!checked.value||busy.value||receipt.value?.stateKey!=='prepared')return;
  if(unknown.value&&operation.value?.phase!==phase)return;
  confirmation.value=operation.value??{operationId:receipt.value.operationId,phase,version:props.subscription.version,signingKeyId:receipt.value.signingKeyId};acknowledged.value=false;epoch.value++;
}
async function submitPrepare(){
  if(!canWrite()||busy.value||!checked.value||receipt.value||secret.value.length<16||secret.value.length>128||unknown.value&&operation.value?.phase!=='prepare')return;
  const target=operation.value??{operationId:crypto.randomUUID(),phase:'prepare' as const,version:props.subscription.version};const signingSecret=secret.value;secret.value='';await submit(target,signingSecret);
}
async function submit(target:Operation,signingSecret?:string){
  if(!canWrite()||!props.modelValue||busy.value)return;const request=scope.begin(write);if(!request)return;pending=request;operation.value={...target};dispatched=true;busy.value=true;problem.value='';
  try{
    const row=target.phase==='prepare'?await prepareWebhookSigningKeyRotation(props.subscription.id,{operationId:target.operationId,version:target.version,signingSecret:signingSecret!},request.signal)
      :await(target.phase==='activate'?activateWebhookSigningKeyRotation:cancelWebhookSigningKeyRotation)(props.subscription.id,target.operationId,{version:target.version,signingKeyId:target.signingKeyId!},request.signal);
    if(!request.current()||!props.modelValue||!canWrite())return;accept(row);dispatched=false;confirmation.value=undefined;epoch.value++;
  }catch(error){if(request.current()&&props.modelValue){
    if(isFullNetProblemDetails(error)&&error.status>=400&&error.status<500&&![408,429].includes(error.status)){dispatched=false;operation.value=undefined;unknown.value=false;emit('unknown',false);checked.value=false;}
    else markUnknown();confirmation.value=undefined;problem.value=t('webhooks.rotation.writeFailed');epoch.value++;
  }}finally{secret.value='';if(request.current()){busy.value=false;pending=undefined;}request.finish();}
}
const controls=computed(()=>{const generation=epoch.value,confirmed=confirmation.value;const owned=()=>generation===epoch.value&&canRead();return{
  input:(value:string)=>{if(owned()&&props.modelValue&&canWrite()&&!busy.value&&!receipt.value)secret.value=value;},
  acknowledge:(value:string|number|boolean)=>{if(owned()&&props.modelValue&&confirmed===confirmation.value&&!busy.value)acknowledged.value=value===true;},
  read:()=>{if(owned())void readReceipt();},close:()=>{if(owned())close();},update:(value:boolean)=>{if(!value&&owned())close();},prepare:()=>{if(owned())void submitPrepare();},
  activate:()=>{if(owned())openComplete('activate');},cancel:()=>{if(owned())openComplete('cancel');},
  confirm:()=>{if(owned()&&confirmed===confirmation.value&&confirmed&&canWrite()&&(confirmed.phase!=='activate'||acknowledged.value))void submit(confirmed);},
  dismiss:()=>{if(owned()){confirmation.value=undefined;acknowledged.value=false;epoch.value++;}},
  next:()=>{if(owned()&&!busy.value&&!unknown.value&&receipt.value?.stateKey!=='prepared'){receipt.value=undefined;operation.value=undefined;checked.value=false;epoch.value++;void readReceipt();}}
};});
watch(()=>props.modelValue,value=>{if(value&&canRead())void readReceipt();else if(!value)stop();});
watch(()=>props.subscription.version,()=>{if(!busy.value){confirmation.value=undefined;acknowledged.value=false;epoch.value++;}},{flush:'sync'});
watch(identity,()=>{stop();receipt.value=undefined;operation.value=undefined;unknown.value=false;checked.value=false;owner=identity();emit('unknown',false);emit('update:modelValue',false);},{flush:'sync'});
</script>
<template>
  <ElDialog :model-value="modelValue&&canRead()" :title="t('webhooks.rotation.title')" width="min(620px, calc(100vw - 24px))" :close-on-click-modal="false" v-on="{'update:modelValue':controls.update}">
    <section class="webhook-key-rotation" data-testid="webhook-key-rotation">
      <p>{{ t('webhooks.rotation.hint') }}</p><p>{{ subscription.eventType }} · {{ subscription.targetUrl }}</p>
      <p>{{ t('webhooks.rotation.activeKey') }} <code>{{ subscription.signingKeyId }}</code></p>
      <ElAlert v-if="unknown" data-testid="webhook-key-unknown" type="warning" :closable="false" :title="t('webhooks.rotation.unknown')"><p>{{ operation?.operationId }}</p></ElAlert>
      <ElAlert v-if="problem" type="error" :closable="false" :title="problem" />
      <div v-if="receipt" data-testid="webhook-key-receipt"><strong>{{ t(`webhooks.rotation.${receipt.stateKey}`) }}</strong><p>{{ receipt.operationId }}</p><p>{{ t('webhooks.rotation.newKey') }} <code>{{ receipt.signingKeyId }}</code></p></div>
      <ElForm v-if="canWrite()&&checked&&!receipt" label-position="top" class="webhook-key-rotation__form">
        <ElFormItem :label="t('webhooks.rotation.secret')"><ElInput :model-value="secret" data-testid="webhook-key-secret" type="password" autocomplete="new-password" :maxlength="128" :disabled="busy" v-on="{'update:modelValue':controls.input}" /></ElFormItem>
        <ElButton data-testid="webhook-key-prepare" :disabled="busy||secret.length<16||secret.length>128" v-on="{click:controls.prepare}">{{ t(unknown?'webhooks.rotation.retryPrepare':'webhooks.rotation.prepare') }}</ElButton>
      </ElForm>
      <div v-if="canWrite()&&receipt?.stateKey==='prepared'" class="webhook-key-rotation__actions">
        <ElButton data-testid="webhook-key-activate" :disabled="!checked||busy||unknown&&operation?.phase!=='activate'" v-on="{click:controls.activate}">{{ t('webhooks.rotation.activate') }}</ElButton>
        <ElButton data-testid="webhook-key-cancel" :disabled="!checked||busy||unknown&&operation?.phase!=='cancel'" v-on="{click:controls.cancel}">{{ t('webhooks.rotation.cancel') }}</ElButton>
      </div>
      <ElButton v-if="receipt&&receipt.stateKey!=='prepared'&&canWrite()" :disabled="busy" v-on="{click:controls.next}">{{ t('webhooks.rotation.next') }}</ElButton>
      <ElButton data-testid="webhook-key-read" :disabled="busy" v-on="{click:controls.read}">{{ t('webhooks.rotation.read') }}</ElButton>
      <div v-if="confirmation" data-testid="webhook-key-confirmation" class="webhook-key-rotation__confirmation">
        <p>{{ t(confirmation.phase==='activate'?'webhooks.rotation.activateHint':'webhooks.rotation.cancelHint') }}</p><p>{{ receipt?.signingKeyId }}</p>
        <ElCheckbox v-if="confirmation.phase==='activate'" :model-value="acknowledged" data-testid="webhook-key-ack" v-on="{'update:modelValue':controls.acknowledge}">{{ t('webhooks.rotation.configured') }}</ElCheckbox>
        <div><ElButton :disabled="busy" v-on="{click:controls.dismiss}">{{ t('common.cancel') }}</ElButton><ElButton data-testid="webhook-key-confirm" type="primary" :disabled="busy||confirmation.phase==='activate'&&!acknowledged" v-on="{click:controls.confirm}">{{ t('common.confirm') }}</ElButton></div>
      </div>
    </section>
    <template #footer><ElButton data-testid="webhook-key-close" v-on="{click:controls.close}">{{ t('common.cancel') }}</ElButton></template>
  </ElDialog>
</template>
<style scoped>
.webhook-key-rotation{display:flex;flex-direction:column;gap:12px;overflow-wrap:anywhere}.webhook-key-rotation p{margin:0}.webhook-key-rotation__actions{display:flex;flex-wrap:wrap;gap:8px}.webhook-key-rotation__confirmation{display:flex;flex-direction:column;gap:12px;border:1px solid var(--el-border-color);padding:12px}.webhook-key-rotation :deep(.el-checkbox){height:auto;white-space:normal}.webhook-key-rotation :deep(.el-checkbox__label){white-space:normal}.webhook-key-rotation :deep(.el-alert--warning),.webhook-key-rotation :deep(.el-alert--warning .el-alert__description){color:#795207}.webhook-key-rotation :deep(.el-alert--error),.webhook-key-rotation :deep(.el-alert--error .el-alert__description){color:#a32121}
</style>
