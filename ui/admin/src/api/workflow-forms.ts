import {
  workflowCreateForm,
  workflowGetForm,
  workflowGetFormComponentCatalog,
  workflowListForms,
  workflowPublishForm,
  workflowUpdateFormDraft,
  isWorkflowFormSchema,
  type PublishWorkflowFormRequest,
  type WorkflowFormComponentCatalogResponse,
  type WorkflowFormResponse as GeneratedWorkflowFormResponse,
  type WorkflowFormSchema,
  type WorkflowFormVersionResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export type WorkflowFormResponse = Omit<GeneratedWorkflowFormResponse, 'draft'> & {
  readonly draft: WorkflowFormSchema;
};

export async function listWorkflowForms(signal?: AbortSignal): Promise<WorkflowFormResponse[]> {
  return (await workflowListForms(http, {}, signal)).map(readSafeForm);
}

export async function getWorkflowForm(formId: string, signal?: AbortSignal): Promise<WorkflowFormResponse> {
  return readSafeForm(await workflowGetForm(http, { formId }, signal));
}

export async function getWorkflowFormComponentCatalog(
  signal?: AbortSignal
): Promise<WorkflowFormComponentCatalogResponse> {
  return workflowGetFormComponentCatalog(http, {}, signal);
}

export async function createWorkflowForm(
  formKey: string,
  draft: WorkflowFormSchema,
  signal?: AbortSignal
): Promise<WorkflowFormResponse> {
  return readSafeForm(await workflowCreateForm(http, {
    body: { formKey, draft: toGeneratedDraft(draft) }
  }, signal));
}

export async function updateWorkflowFormDraft(
  formId: string,
  expectedRevision: number,
  draft: WorkflowFormSchema,
  signal?: AbortSignal
): Promise<WorkflowFormResponse> {
  return readSafeForm(await workflowUpdateFormDraft(http, {
    formId,
    body: { expectedRevision, draft: toGeneratedDraft(draft) }
  }, signal));
}

export async function publishWorkflowForm(
  formId: string,
  body: PublishWorkflowFormRequest,
  signal?: AbortSignal
): Promise<WorkflowFormVersionResponse> {
  return workflowPublishForm(http, { formId, body }, signal);
}

export type {
  PublishWorkflowFormRequest,
  WorkflowFormComponentCatalogResponse,
  WorkflowFormVersionResponse
};

function readSafeForm(value: GeneratedWorkflowFormResponse): WorkflowFormResponse {
  if (!isWorkflowFormSchema(value.draft)) {
    throw new Error('client.invalid_workflow_form_draft');
  }
  return { ...value, draft: value.draft };
}

function toGeneratedDraft(draft: WorkflowFormSchema) {
  return {
    schemaVersion: draft.schemaVersion,
    adapterVersion: draft.adapterVersion,
    sections: draft.sections.map(section => ({
      sectionKey: section.sectionKey,
      fields: section.fields.map(field => ({
        fieldKey: field.fieldKey,
        fieldTypeKey: field.fieldTypeKey,
        required: field.required,
        constraints: { ...field.constraints }
      }))
    }))
  };
}
