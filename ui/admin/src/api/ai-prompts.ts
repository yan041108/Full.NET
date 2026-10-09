import { aiListPrompts, aiGetPromptVersions, aiCreatePrompt, aiAppendPromptVersion, aiPublishPromptVersion, aiRollbackPromptVersion, aiDeactivatePrompt,
  type AiListPromptsParameters, type AiGetPromptVersionsParameters, type AiCreatePromptParameters,
  type AiAppendPromptVersionParameters, type AiPublishPromptVersionParameters, type AiRollbackPromptVersionParameters, type AiDeactivatePromptParameters } from '@fullnet/client-contracts';
import { http } from './http';
export const listPrompts = (p: AiListPromptsParameters, signal?: AbortSignal) => aiListPrompts(http, p, signal);
export const getPromptVersions = (p: AiGetPromptVersionsParameters, signal?: AbortSignal) => aiGetPromptVersions(http, p, signal);
export const createPrompt = (p: AiCreatePromptParameters, signal?: AbortSignal) => aiCreatePrompt(http, p, signal);
export const appendPrompt = (p: AiAppendPromptVersionParameters, signal?: AbortSignal) => aiAppendPromptVersion(http, p, signal);
export const publishPrompt = (p: AiPublishPromptVersionParameters, signal?: AbortSignal) => aiPublishPromptVersion(http, p, signal);
export const rollbackPrompt = (p: AiRollbackPromptVersionParameters, signal?: AbortSignal) => aiRollbackPromptVersion(http, p, signal);
export const deactivatePrompt = (p: AiDeactivatePromptParameters, signal?: AbortSignal) => aiDeactivatePrompt(http, p, signal);
