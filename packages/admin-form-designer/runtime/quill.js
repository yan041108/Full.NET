// 保留上游 vue3-quill 的值/事件协议，使用仍维护的 Quill 2 适配当前工具链。
import Quill from 'quill';
import DOMPurify from 'dompurify';
const sanitize = value => DOMPurify.sanitize(value || '', { USE_PROFILES: { html: true } });
// GHSA-v3m3-f69x-jf25：所有 HTML 边界都清理，不能把隔离替代内容安全。
const originalExport = Quill.prototype.getSemanticHTML;
Quill.prototype.getSemanticHTML = function (...args) { return sanitize(originalExport.apply(this, args)); };
import { defineComponent, h, ref, onMounted, onBeforeUnmount, watch } from 'vue';
import 'quill/dist/quill.snow.css';
export { Quill };
export const quillEditor = defineComponent({
  props: ['value', 'options', 'disabled'], emits: ['update:value', 'change', 'focus', 'blur'],
  setup(props, { emit }) {
    const element = ref(); let editor;
    const setValue = value => { const safe = sanitize(value); if (editor && editor.root.innerHTML !== safe) editor.clipboard.dangerouslyPasteHTML(safe, 'silent'); if (typeof value === 'string' && value !== safe) { emit('update:value', safe); emit('change', { html: safe, text: editor?.getText() ?? '', quill: editor }); } };
    onMounted(() => {
      editor = new Quill(element.value, { theme: 'snow', ...props.options, readOnly: !!props.disabled });
      setValue(props.value);
      editor.on('text-change', () => { const html = sanitize(editor.root.innerHTML); emit('update:value', html); emit('change', { html, text: editor.getText(), quill: editor }); });
      editor.on('selection-change', range => emit(range ? 'focus' : 'blur', editor));
    });
    watch(() => props.value, setValue);
    watch(() => props.disabled, value => editor?.enable(!value));
    onBeforeUnmount(() => { editor?.off('text-change'); editor?.off('selection-change'); editor = undefined; });
    return () => h('div', { ref: element });
  }
});
