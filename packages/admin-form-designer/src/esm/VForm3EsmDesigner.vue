<!--
/**
 * author: vformAdmin
 * email: vdpadmin@163.com
 * website: https://www.vform666.com
 * source: variant-form3-vite@c67479e496bab56a93a3dff168a4f529d8293c67
 * remark: 本文件保留 VForm3 的 JSON/三栏交互模型，并由 Full.NET 重写为 Vue 3.5 ESM 安全子集。
 */
-->
<script setup lang="ts">
import { computed, ref } from 'vue';
import {
  getVForm3WidgetLabel,
  vform3SafeCatalog,
  type VForm3CatalogItem
} from './vform3-catalog';
import {
  cloneDesignerJson,
  createVForm3Widget,
  moveVForm3Widget,
  removeVForm3Widget,
  type VForm3DesignerJson,
  type VForm3Widget
} from './vform3-schema';

const state = ref<VForm3DesignerJson>({ widgetList: [], formConfig: {} });
const selectedIndex = ref<number>(-1);
const selectedWidget = computed(() => state.value.widgetList[selectedIndex.value]);
const optionValues = computed({
  get: () => {
    const items = selectedWidget.value?.options.optionItems;
    if (!Array.isArray(items)) return '';
    return items.map(item => isRecord(item) ? item.value ?? item.label : item)
      .filter(value => typeof value === 'string' || typeof value === 'number')
      .join('\n');
  },
  set: (value: string) => {
    updateSelectedOption('optionItems', value.split(/\r?\n/u)
      .map(item => item.trim())
      .filter(Boolean)
      .map(item => ({ label: item, value: item })));
  }
});

/** 返回当前设计态的隔离副本，避免业务适配层意外改写画布状态。 */
function getFormJson(): VForm3DesignerJson {
  return cloneDesignerJson(state.value);
}

/** 同步替换设计态；同一响应式引用直接驱动画布，不依赖第三方组件私有 ref。 */
function setFormJson(value: unknown): void {
  state.value = cloneDesignerJson(value);
  selectedIndex.value = state.value.widgetList.length > 0 ? 0 : -1;
}

/** 从安全目录新增字段并立即选中，便于继续配置稳定机器码。 */
function addWidget(item: VForm3CatalogItem): void {
  state.value.widgetList.push(createVForm3Widget(item.type, undefined, item.fieldTypeKey));
  selectedIndex.value = state.value.widgetList.length - 1;
}

/** 选择画布中的字段。 */
function selectWidget(index: number): void {
  selectedIndex.value = index;
}

/** 更新当前字段的一个受控属性键。 */
function updateSelectedOption(key: string, value: unknown): void {
  const widget = selectedWidget.value;
  if (widget === undefined) return;
  widget.options[key] = value;
}

/** 将可空数字输入安全写入选项；清空输入时移除键，避免产生非 JSON 的 undefined。 */
function updateSelectedNumberOption(key: string, event: Event): void {
  const widget = selectedWidget.value;
  if (widget === undefined) return;
  const raw = readInput(event).trim();
  if (raw === '') {
    delete widget.options[key];
    return;
  }
  const value = Number(raw);
  if (Number.isFinite(value)) widget.options[key] = value;
}

/** 删除当前字段并将选择位置收敛到剩余字段。 */
function deleteSelected(): void {
  const index = selectedIndex.value;
  state.value.widgetList = removeVForm3Widget(state.value.widgetList, index);
  selectedIndex.value = Math.min(index, state.value.widgetList.length - 1);
}

/** 上下移动当前字段，并同步选择索引。 */
function moveSelected(offset: -1 | 1): void {
  const index = selectedIndex.value;
  const target = index + offset;
  state.value.widgetList = moveVForm3Widget(state.value.widgetList, index, offset);
  if (target >= 0 && target < state.value.widgetList.length) selectedIndex.value = target;
}

/** 读取文本输入事件，避免模板对 unknown 属性使用不安全的双向绑定。 */
function readInput(event: Event): string {
  return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
}

/** 根据字段语义而不只是渲染控件类型显示目录名称。 */
function getWidgetLabel(widget: VForm3Widget): string {
  const fieldTypeKey = typeof widget.options.fullNetFieldType === 'string'
    ? widget.options.fullNetFieldType
    : undefined;
  return getVForm3WidgetLabel(widget.type, fieldTypeKey);
}

/** 判断 JSON 选项节点是否为普通记录。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

defineExpose({ getFormJson, setFormJson });
</script>

<template>
  <div class="vform3-esm" data-testid="vform3-esm-designer">
    <aside class="vform3-esm__palette" aria-label="字段组件库">
      <header>
        <strong>字段组件</strong>
        <small>安全组件目录</small>
      </header>
      <div class="vform3-esm__catalog">
        <button
          v-for="item in vform3SafeCatalog"
          :key="item.key"
          type="button"
          :data-testid="`vform3-add-${item.key}`"
          @click="addWidget(item)"
        >
          <span class="vform3-esm__catalog-icon">+</span>
          {{ item.label }}
        </button>
      </div>
    </aside>

    <main class="vform3-esm__workspace">
      <header class="vform3-esm__toolbar">
        <div>
          <strong>表单画布</strong>
          <small>{{ state.widgetList.length }} 个字段</small>
        </div>
        <span>Workflow Schema 安全模式</span>
      </header>
      <div class="vform3-esm__canvas">
        <div v-if="state.widgetList.length === 0" class="vform3-esm__empty">
          <strong>从左侧添加字段</strong>
          <span>这里只展示可发布、可执行的安全组件。</span>
        </div>
        <button
          v-for="(widget, index) in state.widgetList"
          :key="widget.id"
          type="button"
          class="vform3-esm__field"
          :class="{ 'is-selected': selectedIndex === index }"
          :aria-pressed="selectedIndex === index"
          :data-testid="`vform3-field-${index}`"
          @click="selectWidget(index)"
        >
          <span class="vform3-esm__field-label">
            {{ String(widget.options.label ?? widget.options.name ?? getWidgetLabel(widget)) }}
            <b v-if="widget.options.required === true">*</b>
          </span>
          <span class="vform3-esm__preview" aria-hidden="true">
            {{ getWidgetLabel(widget) }}预览
          </span>
          <code>{{ String(widget.options.name ?? '') }}</code>
        </button>
      </div>
    </main>

    <aside class="vform3-esm__properties" aria-label="字段属性">
      <header>
        <strong>字段属性</strong>
        <small v-if="selectedWidget">{{ getWidgetLabel(selectedWidget) }}</small>
      </header>
      <div v-if="selectedWidget" class="vform3-esm__property-form">
        <label>
          显示名称
          <input
            data-testid="vform3-property-label"
            :value="String(selectedWidget.options.label ?? '')"
            @input="updateSelectedOption('label', readInput($event))"
          >
        </label>
        <label>
          字段机器码
          <input
            data-testid="vform3-property-name"
            :value="String(selectedWidget.options.name ?? '')"
            @input="updateSelectedOption('name', readInput($event))"
          >
        </label>
        <label>
          分组机器码
          <input
            data-testid="vform3-property-section"
            :value="String(selectedWidget.options.fullNetSectionKey ?? 'main')"
            @input="updateSelectedOption('fullNetSectionKey', readInput($event))"
          >
        </label>
        <label class="vform3-esm__check">
          <input
            type="checkbox"
            :checked="selectedWidget.options.required === true"
            @change="updateSelectedOption('required', ($event.target as HTMLInputElement).checked)"
          >
          必填字段
        </label>
        <template v-if="['input', 'textarea'].includes(selectedWidget.type)">
          <label>
            最小长度
            <input
              type="number"
              min="0"
              :value="selectedWidget.options.minLength ?? ''"
              @input="updateSelectedNumberOption('minLength', $event)"
            >
          </label>
          <label>
            最大长度
            <input
              type="number"
              min="0"
              :value="selectedWidget.options.maxLength ?? ''"
              @input="updateSelectedNumberOption('maxLength', $event)"
            >
          </label>
        </template>
        <template v-if="selectedWidget.type === 'number'">
          <label>
            最小值
            <input
              type="number"
              :value="selectedWidget.options.min ?? ''"
              @input="updateSelectedNumberOption('min', $event)"
            >
          </label>
          <label>
            最大值
            <input
              type="number"
              :value="selectedWidget.options.max ?? ''"
              @input="updateSelectedNumberOption('max', $event)"
            >
          </label>
          <label>
            小数位数
            <input
              type="number"
              min="0"
              max="10"
              :value="selectedWidget.options.precision ?? ''"
              @input="updateSelectedNumberOption('precision', $event)"
            >
          </label>
        </template>
        <label v-if="['radio', 'checkbox', 'select'].includes(selectedWidget.type)">
          选项（每行一个）
          <textarea v-model="optionValues" rows="5" />
        </label>
        <div class="vform3-esm__actions">
          <button type="button" data-testid="vform3-move-up" :disabled="selectedIndex <= 0" @click="moveSelected(-1)">上移</button>
          <button type="button" data-testid="vform3-move-down" :disabled="selectedIndex >= state.widgetList.length - 1" @click="moveSelected(1)">下移</button>
          <button type="button" class="danger" data-testid="vform3-delete" @click="deleteSelected">删除</button>
        </div>
      </div>
      <div v-else class="vform3-esm__empty-property">选择画布字段后编辑属性</div>
    </aside>
  </div>
</template>

<style scoped>
.vform3-esm {
  display: grid;
  grid-template-columns: 220px minmax(420px, 1fr) 280px;
  height: 100%;
  min-height: 620px;
  color: var(--el-text-color-primary, #1f2937);
  background: #f4f6f9;
}

.vform3-esm > aside,
.vform3-esm__workspace {
  min-width: 0;
}

.vform3-esm__palette,
.vform3-esm__properties {
  background: #fff;
}

.vform3-esm__palette {
  border-right: 1px solid #e5e7eb;
}

.vform3-esm__properties {
  border-left: 1px solid #e5e7eb;
}

.vform3-esm header {
  display: flex;
  min-height: 58px;
  padding: 0 18px;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid #e5e7eb;
}

.vform3-esm header small,
.vform3-esm__toolbar span {
  color: #8492a6;
  font-size: 12px;
}

.vform3-esm__catalog {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  padding: 16px;
}

.vform3-esm__catalog button,
.vform3-esm__actions button {
  border: 1px solid #d9e0e8;
  border-radius: 6px;
  background: #fff;
  color: #334155;
  cursor: pointer;
}

.vform3-esm__catalog button {
  display: flex;
  min-height: 42px;
  align-items: center;
  gap: 7px;
  padding: 8px;
  font-size: 12px;
  text-align: left;
}

.vform3-esm__catalog button:hover,
.vform3-esm__actions button:hover {
  border-color: #409eff;
  color: #409eff;
}

.vform3-esm__catalog-icon {
  display: grid;
  width: 20px;
  height: 20px;
  place-items: center;
  border-radius: 5px;
  background: #ecf5ff;
  color: #409eff;
  font-weight: 700;
}

.vform3-esm__toolbar div {
  display: flex;
  align-items: baseline;
  gap: 10px;
}

.vform3-esm__canvas {
  display: flex;
  min-height: 520px;
  margin: 22px;
  padding: 22px;
  flex-direction: column;
  gap: 14px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  background: #fff;
  box-shadow: 0 6px 20px rgb(15 23 42 / 5%);
}

.vform3-esm__empty {
  display: grid;
  min-height: 430px;
  place-content: center;
  gap: 8px;
  color: #94a3b8;
  text-align: center;
}

.vform3-esm__field {
  display: grid;
  grid-template-columns: minmax(120px, 0.65fr) minmax(180px, 1fr) auto;
  gap: 16px;
  min-height: 74px;
  padding: 14px 16px;
  align-items: center;
  border: 1px solid #dbe3ec;
  border-radius: 6px;
  background: #fff;
  color: inherit;
  cursor: pointer;
  text-align: left;
}

.vform3-esm__field:hover,
.vform3-esm__field.is-selected {
  border-color: #409eff;
  box-shadow: 0 0 0 2px rgb(64 158 255 / 10%);
}

.vform3-esm__field-label b {
  color: #f56c6c;
}

.vform3-esm__preview {
  padding: 9px 12px;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  color: #a8abb2;
}

.vform3-esm__field code {
  color: #64748b;
  font-size: 12px;
}

.vform3-esm__property-form {
  display: grid;
  gap: 16px;
  padding: 18px;
}

.vform3-esm__property-form label {
  display: grid;
  gap: 7px;
  color: #475569;
  font-size: 13px;
}

.vform3-esm__property-form input:not([type='checkbox']),
.vform3-esm__property-form textarea {
  width: 100%;
  box-sizing: border-box;
  padding: 9px 10px;
  border: 1px solid #dcdfe6;
  border-radius: 5px;
  color: #334155;
  font: inherit;
}

.vform3-esm__property-form .vform3-esm__check {
  display: flex;
  align-items: center;
  gap: 8px;
}

.vform3-esm__actions {
  display: flex;
  gap: 8px;
  padding-top: 6px;
}

.vform3-esm__actions button {
  padding: 7px 12px;
}

.vform3-esm__actions .danger {
  margin-left: auto;
  color: #f56c6c;
}

.vform3-esm__empty-property {
  padding: 44px 18px;
  color: #94a3b8;
  font-size: 13px;
  text-align: center;
}

@media (max-width: 1080px) {
  .vform3-esm {
    grid-template-columns: 180px minmax(380px, 1fr) 240px;
  }
}
</style>
