import {
  isCodeGenerationPreviewRequest
} from '@fullnet/client-contracts';
import {
  createCodeGenerationTemplatesApi
} from './code-generation-templates.js';
import {
  createCodeGenerationRunsApi
} from './code-generation-runs.js';

/**
 * 装配 Host 代码生成预览工作台；全部产物使用文本节点呈现，禁止执行生成内容。
 */
export function createCodeGenerationPreviewsController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const form = root.querySelector('[data-codegen-form]');
  const summary = root.querySelector('[data-codegen-summary]');
  const artifacts = root.querySelector('[data-codegen-artifacts]');
  const content = root.querySelector('[data-codegen-content] code');
  const templateForm = root.querySelector('[data-codegen-template-form]');
  const templateDirectory = root.querySelector(
    '[data-codegen-template-directory]'
  );
  const templateUpdate = root.querySelector(
    '[data-codegen-template-update]'
  );
  const templateDelete = root.querySelector(
    '[data-codegen-template-delete]'
  );
  const runHistory = root.querySelector('[data-codegen-run-history]');
  const applyButton = root.querySelector('[data-codegen-apply]');
  const templatesApi = createCodeGenerationTemplatesApi(request);
  const runsApi = createCodeGenerationRunsApi(request);
  const canReadTemplates =
    options.hasPermission?.('codegen.templates.read') === true;
  const canWriteTemplates =
    options.hasPermission?.('codegen.templates.write') === true;
  const canReadRuns =
    options.hasPermission?.('codegen.runs.read') === true;
  const canExecuteRuns =
    options.hasPermission?.('codegen.runs.execute') === true;
  const canApplyRuns =
    options.hasPermission?.('codegen.runs.apply') === true;
  const confirm = options.confirm ?? confirmAction;
  let changing = false;
  let currentArtifacts = [];
  let templates = [];
  let selectedTemplate;
  let reviewedPreview;

  const invalidateReviewedPreview = () => {
    reviewedPreview = undefined;
    if (applyButton) applyButton.disabled = true;
  };

  const readSchema = () => {
    const schemaInput = form?.querySelector('[name="schema"]');
    let input;
    try {
      input = JSON.parse(schemaInput?.value ?? '');
    } catch {
      showProblem(
        root,
        { code: 'client.codegen_invalid_json' },
        translation().t('codeGeneration.invalidInput')
      );
      return undefined;
    }
    if (!isCodeGenerationPreviewRequest(input)) {
      showProblem(
        root,
        { code: 'client.codegen_invalid_schema' },
        translation().t('codeGeneration.invalidInput')
      );
      return undefined;
    }
    return input;
  };

  const selectTemplate = template => {
    invalidateReviewedPreview();
    selectedTemplate = template;
    const name = templateForm?.querySelector('[name="templateName"]');
    const description = templateForm?.querySelector(
      '[name="templateDescription"]'
    );
    const schema = form?.querySelector('[name="schema"]');
    if (name) name.value = template.name;
    if (description) description.value = template.description ?? '';
    if (schema) schema.value = JSON.stringify(template.schema, null, 2);
  };

  const renderTemplates = () => {
    if (!templateDirectory) return;
    const fragment = templateDirectory.ownerDocument.createDocumentFragment();
    templates.forEach(template => {
      const button = templateDirectory.ownerDocument.createElement('button');
      button.type = 'button';
      button.dataset.codegenTemplateLoad = template.id;
      button.className = 'layui-btn layui-btn-primary layui-btn-sm';
      button.textContent = `${template.name} · v${template.version}`;
      fragment.append(button);
    });
    templateDirectory.replaceChildren(fragment);
  };

  const loadTemplates = async () => {
    if (!canReadTemplates) return;
    try {
      templates = (await templatesApi.list()).items;
      renderTemplates();
    } catch (problem) {
      showProblem(
        root,
        problem,
        translation().t('codeGeneration.invalidInput')
      );
    }
  };

  const loadRuns = async () => {
    if (!canReadRuns || !runHistory) return;
    try {
      const page = await runsApi.list();
      const fragment = runHistory.ownerDocument.createDocumentFragment();
      page.items.forEach(run => {
        const article = runHistory.ownerDocument.createElement('article');
        const title = runHistory.ownerDocument.createElement('strong');
        title.textContent =
          `${run.moduleKey ?? '—'} / ${run.entityKey ?? '—'}`;
        const status = runHistory.ownerDocument.createElement('span');
        status.textContent = `${run.status} · ${run.operationKind}`;
        const id = runHistory.ownerDocument.createElement('code');
        id.textContent = run.id;
        const summary = runHistory.ownerDocument.createElement('small');
        summary.textContent =
          `${run.artifactCount} · ${run.startedAtUtc}`;
        const hashes = runHistory.ownerDocument.createElement('small');
        hashes.textContent =
          `schema ${run.schemaSha256?.slice(0, 12) ?? '—'} · `
          + `manifest ${run.manifestSha256?.slice(0, 12) ?? '—'}`;
        const actor = runHistory.ownerDocument.createElement('small');
        actor.textContent =
          `${run.requestedByUserId} · ${run.finishedAtUtc}`;
        article.append(title, status, id, summary, hashes, actor);
        fragment.append(article);
      });
      runHistory.replaceChildren(fragment);
    } catch (problem) {
      showProblem(
        root,
        problem,
        translation().t('codeGeneration.invalidInput')
      );
    }
  };

  const templateInput = version => {
    const schema = readSchema();
    const name = templateForm
      ?.querySelector('[name="templateName"]')
      ?.value
      ?.trim();
    if (!schema || !name) return undefined;
    const description = templateForm
      ?.querySelector('[name="templateDescription"]')
      ?.value
      ?.trim() || null;
    return version === undefined
      ? { name, description, schema }
      : { name, description, schema, version };
  };

  const upsertTemplate = template => {
    const index = templates.findIndex(item => item.id === template.id);
    if (index < 0) {
      templates.unshift(template);
    } else {
      templates.splice(index, 1, template);
    }
    renderTemplates();
    selectTemplate(template);
  };

  const onTemplateSubmit = async event => {
    event.preventDefault();
    if (!canWriteTemplates || changing) return;
    const input = templateInput();
    if (!input) return;
    changing = true;
    try {
      upsertTemplate(await templatesApi.create(input));
      hideProblem(root);
    } catch (problem) {
      showProblem(
        root,
        problem,
        translation().t('codeGeneration.invalidInput')
      );
    } finally {
      changing = false;
    }
  };

  const onTemplateUpdate = async () => {
    if (!canWriteTemplates || changing || !selectedTemplate) return;
    const input = templateInput(selectedTemplate.version);
    if (!input) return;
    changing = true;
    try {
      upsertTemplate(await templatesApi.update(selectedTemplate.id, input));
      hideProblem(root);
    } catch (problem) {
      showProblem(
        root,
        problem,
        translation().t('codeGeneration.invalidInput')
      );
    } finally {
      changing = false;
    }
  };

  const onTemplateDelete = async () => {
    if (!canWriteTemplates || changing || !selectedTemplate) return;
    changing = true;
    try {
      const deletedId = selectedTemplate.id;
      await templatesApi.remove(deletedId, selectedTemplate.version);
      templates = templates.filter(template => template.id !== deletedId);
      selectedTemplate = undefined;
      renderTemplates();
      hideProblem(root);
    } catch (problem) {
      showProblem(
        root,
        problem,
        translation().t('codeGeneration.invalidInput')
      );
    } finally {
      changing = false;
    }
  };

  const onTemplateDirectoryClick = event => {
    const button = event.target instanceof Element
      ? event.target.closest('[data-codegen-template-load]')
      : undefined;
    if (!button) return;
    const template = templates.find(
      item => item.id === button.dataset.codegenTemplateLoad
    );
    if (template) selectTemplate(template);
  };

  const renderSelected = path => {
    const artifact = currentArtifacts.find(item => item.path === path);
    if (!artifact || !content) return;
    content.textContent = artifact.content;
    artifacts?.querySelectorAll('button').forEach(button => {
      button.classList.toggle(
        'is-active',
        button.dataset.codegenArtifact === path
      );
    });
  };

  const renderPreview = preview => {
    if (summary) {
      const fragment = summary.ownerDocument.createDocumentFragment();
      [
        preview.databaseTableName,
        preview.readPermission,
        preview.writePermission
      ].forEach(value => {
        const code = summary.ownerDocument.createElement('code');
        code.textContent = value;
        fragment.append(code);
      });
      summary.replaceChildren(fragment);
    }

    currentArtifacts = preview.artifacts;
    if (artifacts) {
      const fragment = artifacts.ownerDocument.createDocumentFragment();
      currentArtifacts.forEach(artifact => {
        const button = artifacts.ownerDocument.createElement('button');
        button.type = 'button';
        button.dataset.codegenArtifact = artifact.path;
        const kind = artifacts.ownerDocument.createElement('span');
        kind.textContent = artifact.kind;
        const path = artifacts.ownerDocument.createElement('strong');
        path.textContent = artifact.path;
        const hash = artifacts.ownerDocument.createElement('small');
        hash.textContent = artifact.sha256.slice(0, 12);
        button.append(kind, path, hash);
        fragment.append(button);
      });
      artifacts.replaceChildren(fragment);
    }

    const first = currentArtifacts[0];
    if (first) {
      renderSelected(first.path);
    } else if (content) {
      content.textContent = '';
    }
  };

  const onSubmit = async event => {
    event.preventDefault();
    if (!form || changing || !canExecuteRuns) return;

    const input = readSchema();
    if (!input) {
      return;
    }

    changing = true;
    const submit = form.querySelector('[type="submit"]');
    if (submit) submit.disabled = true;
    try {
      const source = selectedTemplate
        && JSON.stringify(input) === JSON.stringify(selectedTemplate.schema)
        ? {
            templateId: selectedTemplate.id,
            templateVersion: selectedTemplate.version
          }
        : { schema: input };
      const tracked = await runsApi.preview(source);
      const preview = tracked.preview;
      if (source.templateId) {
        reviewedPreview = {
          runId: tracked.runId,
          templateId: source.templateId,
          templateVersion: source.templateVersion,
          artifactCount: preview.artifacts.length
        };
        if (applyButton) applyButton.disabled = !canApplyRuns;
      }

      renderPreview(preview);
      await loadRuns();
      hideProblem(root);
    } catch (problem) {
      showProblem(
        root,
        problem,
        translation().t('codeGeneration.invalidInput')
      );
    } finally {
      changing = false;
      if (submit) submit.disabled = false;
    }
  };

  const onApply = async () => {
    const reviewed = reviewedPreview;
    if (!canApplyRuns
      || changing
      || !reviewed
      || !selectedTemplate
      || selectedTemplate.id !== reviewed.templateId
      || selectedTemplate.version !== reviewed.templateVersion) {
      return;
    }

    const accepted = await confirm(translation().t(
      'codeGeneration.applyConfirm',
      {
        name: selectedTemplate.name,
        version: selectedTemplate.version,
        count: reviewed.artifactCount
      }
    ));
    if (!accepted) return;

    changing = true;
    if (applyButton) applyButton.disabled = true;
    try {
      await runsApi.apply({ previewRunId: reviewed.runId });
      invalidateReviewedPreview();
      await loadRuns();
      hideProblem(root);
    } catch (problem) {
      showProblem(
        root,
        problem,
        translation().t('codeGeneration.invalidInput')
      );
    } finally {
      changing = false;
      if (applyButton && reviewedPreview) applyButton.disabled = false;
    }
  };

  const onArtifactClick = event => {
    const button = event.target instanceof Element
      ? event.target.closest('[data-codegen-artifact]')
      : undefined;
    if (button) {
      renderSelected(button.dataset.codegenArtifact);
    }
  };

  form?.addEventListener('submit', onSubmit);
  form?.querySelector('[name="schema"]')
    ?.addEventListener('input', invalidateReviewedPreview);
  applyButton?.addEventListener('click', onApply);
  artifacts?.addEventListener('click', onArtifactClick);
  templateForm?.addEventListener('submit', onTemplateSubmit);
  templateUpdate?.addEventListener('click', onTemplateUpdate);
  templateDelete?.addEventListener('click', onTemplateDelete);
  templateDirectory?.addEventListener('click', onTemplateDirectoryClick);

  return {
    async load() {
      await Promise.all([loadTemplates(), loadRuns()]);
    },
    dispose() {
      form?.removeEventListener('submit', onSubmit);
      form?.querySelector('[name="schema"]')
        ?.removeEventListener('input', invalidateReviewedPreview);
      applyButton?.removeEventListener('click', onApply);
      artifacts?.removeEventListener('click', onArtifactClick);
      templateForm?.removeEventListener('submit', onTemplateSubmit);
      templateUpdate?.removeEventListener('click', onTemplateUpdate);
      templateDelete?.removeEventListener('click', onTemplateDelete);
      templateDirectory?.removeEventListener(
        'click',
        onTemplateDirectoryClick
      );
    }
  };
}

function confirmAction(message) {
  if (globalThis.layui?.layer?.confirm) {
    return new Promise(resolve => {
      globalThis.layui.layer.confirm(message, { icon: 3 }, index => {
        globalThis.layui.layer.close(index);
        resolve(true);
      }, () => resolve(false));
    });
  }
  return Promise.resolve(globalThis.confirm(message));
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-codegen-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent =
    problem?.code ?? 'client.codegen_preview_failed';
  panel.querySelector('span').textContent =
    problem?.title ?? problem?.detail ?? fallback;
}

function hideProblem(root) {
  const panel = root.querySelector('[data-codegen-problem]');
  if (panel) panel.hidden = true;
}
