// 代码生成与格式化使用本地依赖，不加载上游远程脚本。
import beautifier from 'js-beautify';
export const beautifierOpts = { html: { indent_size: 2 }, js: { indent_size: 2 }, css: { indent_size: 2 } };
export default function loadBeautifier(callback) { callback(beautifier); }
