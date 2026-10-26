import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import jsx from '@vitejs/plugin-vue-jsx';
import { fileURLToPath } from 'node:url';
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import { writeFile } from 'node:fs/promises';
import { createRuntimeRelease } from './runtime-release.mjs';
const here = path.dirname(fileURLToPath(import.meta.url));
const upstream = path.join(here, 'vendor/vform3/upstream/src');
function svgFiles(dir) { return readdirSync(dir, { withFileTypes: true }).flatMap(e => e.isDirectory() ? svgFiles(path.join(dir, e.name)) : e.name.endsWith('.svg') ? [path.join(dir, e.name)] : []); }
export default defineConfig({
  root: path.join(here, 'runtime'), base: '/vform3/', publicDir: path.join(here, 'runtime-public'),
  plugins: [
    { name: 'fullnet-vform-release', async writeBundle(options) {
      const release = await createRuntimeRelease(options.dir);
      await writeFile(path.join(options.dir, 'runtime-release.json'), JSON.stringify(release, null, 2) + '\n');
    } },
    { name: 'fullnet-vform-assets', enforce: 'pre',
      resolveId(id) { if (id === 'virtual:fullnet-vform-icons') return '\0fullnet-vform-icons'; },
      load(id) { if (id !== '\0fullnet-vform-icons') return;
        const symbols = svgFiles(path.join(upstream, 'icons/svg')).map(file => {
          const name = path.relative(path.join(upstream, 'icons/svg'), file).replaceAll(path.sep, '-').replace(/\.svg$/, '');
          const svg = readFileSync(file, 'utf8'); const viewBox = svg.match(/viewBox="([^"]+)"/)?.[1] ?? '0 0 1024 1024';
          return svg.replace(/<svg[^>]*>/, `<symbol id="icon-${name}" viewBox="${viewBox}">`).replace('</svg>', '</symbol>');
        }).join('');
        return `const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');svg.style.display='none';svg.innerHTML=${JSON.stringify(symbols)};document.body.append(svg);`;
      },
      generateBundle() {
        const seen = new Set(); const notices = [readFileSync(path.join(here, 'vendor/vform3/upstream/license.txt'), 'utf8')];
        for (const id of this.getModuleIds()) {
          if (!id.includes('node_modules') || id.startsWith('\0')) continue;
          let directory = path.dirname(id.split('?')[0]);
          while (directory !== path.dirname(directory) && !existsSync(path.join(directory, 'package.json'))) directory = path.dirname(directory);
          if (seen.has(directory) || !existsSync(path.join(directory, 'package.json'))) continue;
          seen.add(directory);
          const pkg = JSON.parse(readFileSync(path.join(directory, 'package.json'), 'utf8'));
          for (const file of readdirSync(directory).filter(file => /^(LICENSE|COPYING|NOTICE)(\.|$)/i.test(file)))
            notices.push(`${pkg.name}@${pkg.version}\n${readFileSync(path.join(directory, file), 'utf8')}`);
        }
        notices.push(readFileSync(path.join(here, 'vendor/vform3/upstream/lib/vuedraggable/LICENSE'), 'utf8'));
        this.emitFile({ type: 'asset', fileName: 'THIRD-PARTY-NOTICES.txt', source: notices.join('\n\n') });
        this.emitFile({ type: 'asset', fileName: 'LICENSE.txt', source: readFileSync(path.join(here, 'vendor/vform3/upstream/license.txt')) });
        const ace = path.dirname(fileURLToPath(import.meta.resolve('ace-builds/src-min-noconflict/ace.js')));
        for (const file of ['worker-javascript.js', 'worker-json.js', 'worker-css.js', 'worker-html.js', 'mode-html.js'])
          this.emitFile({ type: 'asset', fileName: 'ace/' + file, source: readFileSync(path.join(ace, file)) });
      },
      configureServer(server) {
        const ace = path.dirname(fileURLToPath(import.meta.resolve('ace-builds/src-min-noconflict/ace.js')));
        server.middlewares.use((req, res, next) => {
          const match = req.url?.match(/^\/vform3\/ace\/(worker-(?:javascript|json|css|html)|mode-html)\.js$/);
          if (!match) return next();
          res.setHeader('Content-Type', 'text/javascript'); res.setHeader('Access-Control-Allow-Origin', '*'); res.end(readFileSync(path.join(ace, match[1] + '.js')));
        });
      },
      transformIndexHtml: { order: 'post', handler: html => html.replace(/<script type="module" src="[^"]*\/@vite\/client"><\/script>/g, '') },
      transform(code, id) {
        // ElSwitch 会在挂载时把 null 回写为 false；属性面板只显示布尔投影，用户操作时才修改原值。
        if (id.endsWith('/field-switch/switch-defaultValue-editor.vue')) return code.replace('v-model="optionModel.defaultValue"', ':model-value="optionModel.defaultValue ?? false" @update:model-value="optionModel.defaultValue = $event"');
        if (id.endsWith('/utils/sfc-generator.js')) return "import { htmlContentBinding } from 'fullnet-vform-codegen';\n" + code.replace('v-html="${widget.options.htmlContent}"', 'v-html="${htmlContentBinding(widget.options.htmlContent)}"');
        if (id.includes('/upstream/src/') && code.includes('import.meta.globEager')) return code.replace(/import\.meta\.globEager\(([^)]+)\)/g, 'import.meta.glob($1, { eager: true })');
        if (id.endsWith('/form-render/index.vue')) return code.replace('this.$refs.renderForm.validateField(fieldName)', 'this.$nextTick(() => this.$refs.renderForm?.validateField(fieldName))');
        if (id.endsWith('/toolbar-panel/index.vue')) return code.replaceAll('JSON.stringify({widgetList, formConfig}', 'JSON.stringify({...this.designer.fullNetAssetExtras, widgetList, formConfig}');
        if (id.endsWith('/widget-panel/templatesConfig.js')) return code.replaceAll('https://ks3-cn-beijing.ksyuncs.com/vform-static/form-samples/', '/vform3/templates/');
      }
    }, vue(), jsx()
  ],
  resolve: { alias: [
    { find: 'fullnet-vform-codegen', replacement: path.join(here, 'runtime/codegen.js') },
    { find: '@/utils/config', replacement: path.join(here, 'runtime/config.js') },
    { find: '@/utils/beautifierLoader', replacement: path.join(here, 'runtime/beautifier.js') },
    { find: 'vform-draggable', replacement: path.join(here, 'vendor/vform3/upstream/lib/vuedraggable/dist/vuedraggable.common.js') },
    { find: 'vue3-quill', replacement: path.join(here, 'runtime/quill.js') },
    { find: '@', replacement: upstream }
  ], extensions: ['.mjs', '.js', '.ts', '.jsx', '.json', '.vue'], dedupe: ['vue', 'element-plus'] },
  optimizeDeps: { include: ['vform-draggable'] },
  css: { preprocessorOptions: { scss: { additionalData: `@import "${path.join(upstream, 'styles/global.scss').replaceAll('\\', '/')}";`, silenceDeprecations: ['import', 'global-builtin', 'color-functions', 'legacy-js-api'] } } },
  server: { hmr: false, cors: true, headers: { 'Access-Control-Allow-Origin': '*', 'Cross-Origin-Resource-Policy': 'cross-origin' }, fs: { allow: [path.resolve(here, '../..')] } },
  build: { rolldownOptions: { output: { banner: '/*! VForm 3 (c) vformAdmin | https://www.vform666.com | Variant Form License Terms 1.0; see /vform3/LICENSE.txt and THIRD-PARTY-NOTICES.txt */' } }, outDir: path.join(here, 'dist/runtime'), target: 'esnext', emptyOutDir: true, commonjsOptions: { include: [/node_modules/, /vuedraggable/] } }
});
