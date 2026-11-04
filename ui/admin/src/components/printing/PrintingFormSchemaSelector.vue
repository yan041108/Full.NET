<script setup lang="ts">
import {computed,onBeforeUnmount,ref,watch} from 'vue';
import {ElAlert,ElButton,ElOption,ElPopconfirm,ElSelect} from 'element-plus';
import type {PrintingFormSchemaDefinition} from '@fullnet/client-contracts';
import {listPrintingFormSchemas} from '../../api/printing-templates';
import {useSessionStore} from '../../auth/session';
import {useAdminI18n} from '../../i18n/adminI18n';
import {useAuthorizedViewScope} from '../../composables/useAuthorizedViewScope';
import {suggestedPrintingSchemaLayout} from '../../printing/schema-layout';

const props=defineProps<{modelValue:string;layoutHtml:string;disabled:boolean;immutable:boolean}>();
const emit=defineEmits<{'update:modelValue':[value:string];replace:[value:{formSchemaKey:string;layoutHtml:string}]}>();
const session=useSessionStore(),{t}=useAdminI18n(),permission='printing.form_schemas.read';
const allowed=()=>!session.switching&&session.currentUser?.tenantId==null&&session.currentUser?.scope==='host'&&session.currentUser?.actorScope==='host'&&session.can(permission);
const rows=ref<PrintingFormSchemaDefinition[]>([]),loading=ref(false),failed=ref(false),epoch=ref(0);
let request:ReturnType<typeof scope.begin>,timer:ReturnType<typeof setTimeout>|undefined,requestVersion=0;
function stop(){requestVersion++;epoch.value++;request?.cancel();request=undefined;if(timer)clearTimeout(timer);timer=undefined;loading.value=false;rows.value=[];failed.value=false;}
const scope=useAuthorizedViewScope(session,stop,()=>{if(allowed()&&!props.immutable)void load();});
async function load(){
  stop();if(!allowed()||props.immutable||props.disabled)return;const active=scope.begin(permission);if(!active)return;request=active;const generation=requestVersion;loading.value=true;
  timer=setTimeout(()=>{if(generation===requestVersion){active.cancel();loading.value=false;failed.value=true;epoch.value++;}},10000);
  try{const value=await listPrintingFormSchemas(active.signal);if(active.current()&&allowed()&&!props.immutable&&generation===requestVersion){rows.value=value;failed.value=false;epoch.value++;}}
  catch{if(active.current()&&generation===requestVersion)failed.value=true;}
  finally{if(request===active){if(timer)clearTimeout(timer);timer=undefined;request=undefined;loading.value=false;}active.finish();}
}
const selected=computed(()=>rows.value.find(row=>row.formSchemaKey===props.modelValue));
const controls=computed(()=>{
  const generation=epoch.value,key=props.modelValue,layout=props.layoutHtml,catalog=rows.value;
  const owned=()=>generation===epoch.value&&allowed()&&!props.disabled&&!props.immutable&&!loading.value&&!failed.value&&props.modelValue===key;
  return {select:(value:string)=>{if(owned()&&catalog===rows.value&&rows.value.some(row=>row.formSchemaKey===value)){epoch.value++;emit('update:modelValue',value);}},
    reload:()=>{if(allowed()&&!props.disabled&&!props.immutable)void load();},
    replace:()=>{const schema=selected.value;if(!owned()||layout!==props.layoutHtml||!schema||!schema.fields.length)return;try{emit('replace',{formSchemaKey:key,layoutHtml:suggestedPrintingSchemaLayout(schema)});}catch{failed.value=true;}}
  };
});
watch(()=>props.immutable,()=>{stop();if(allowed()&&!props.immutable)void load();});
watch(()=>session.switching,()=>{stop();if(allowed()&&!props.immutable&&!props.disabled)void load();},{flush:'sync'});
watch(()=>[props.modelValue,props.layoutHtml,props.disabled],()=>{epoch.value++;},{flush:'sync'});
onBeforeUnmount(stop);
</script>
<template>
  <section class="printing-schema-selector" data-testid="printing-schema-selector" :aria-busy="loading">
    <template v-if="!immutable&&allowed()">
      <ElSelect :model-value="modelValue" filterable :disabled="disabled||loading||failed||!rows.length" :loading="loading" :aria-label="t('printingTemplates.schema')" data-testid="printing-schema-choice" v-on="{'update:modelValue':controls.select}">
        <ElOption v-for="row in rows" :key="row.formSchemaKey" :value="row.formSchemaKey" :label="`${row.formSchemaKey==='printing.tenant_profile_card'?t('printingTemplates.tenantProfileSchema'):row.displayName} · ${row.formSchemaKey}`" />
      </ElSelect>
      <code>{{modelValue}}</code>
      <ElAlert v-if="failed" type="warning" :closable="false" :title="t('printingTemplates.schemaLoadFailed')" />
      <ElButton v-if="failed" data-testid="printing-schema-retry" :disabled="disabled" v-on="{click:controls.reload}">{{t('printingTemplates.fieldsRetry')}}</ElButton>
      <p v-if="!loading&&!failed&&!rows.length">{{t('printingTemplates.schemaEmpty')}}</p>
      <p>{{t('printingTemplates.schemaChangeHint')}}</p>
      <p v-if="selected?.requiresRecordId">{{t('printingTemplates.schemaRecordRequired')}}</p>
      <ElPopconfirm v-if="selected?.fields.length" :key="epoch" :title="t('printingTemplates.schemaReplaceConfirm')" :width="280" v-on="{confirm:controls.replace}">
        <template #reference><ElButton data-testid="printing-schema-layout" :disabled="disabled||loading||failed">{{t('printingTemplates.schemaUseLayout')}}</ElButton></template>
      </ElPopconfirm>
    </template>
    <p v-else><code>{{modelValue}}</code> · {{t(immutable?'printingTemplates.schemaImmutable':'printingTemplates.schemaReadRequired')}}</p>
  </section>
</template>
<style scoped>
.printing-schema-selector{display:flex;flex-direction:column;gap:10px;width:100%;overflow-wrap:anywhere}.printing-schema-selector p{margin:0}.printing-schema-selector :deep(.el-select){width:100%}.printing-schema-selector :deep(.el-alert--warning),.printing-schema-selector :deep(.el-alert--warning .el-alert__description){color:#795207}
/* 绝对定位的选中文字沿用表单底色，避免弹窗遮罩参与文字对比度计算。 */
.printing-schema-selector :deep(.el-select__placeholder > span){background-color:var(--el-fill-color-blank)}
</style>
