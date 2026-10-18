import type { GoViewProject, GoViewProjectVersion, GoViewProjectPreview } from '@fullnet/client-contracts';
import { outputId } from './data-output-fixtures';
export const canvasJson = '{"width":1920,"height":1080,"backgroundColor":"#0a1628","components":[]}';
export const goViewProject: GoViewProject = { id: '019bc2b1-2a40-7cc3-8992-a80de51bf292', projectKey: 'demo', name: '大屏示例', canvasJson, latestPublishedVersionNumber: 0, isEnabled: true, createdAtUtc: '2026-10-16T00:00:00Z', updatedAtUtc: null, version: 1 };
export const goViewVersion = (project = goViewProject): GoViewProjectVersion => ({ id: '019bc2b1-2a40-7cc3-8992-a80de51bf293', projectId: project.id, versionNumber: project.latestPublishedVersionNumber + 1, canvasJson: project.canvasJson, changeNote: null, publishedByUserId: outputId, publishedAtUtc: '2026-10-16T00:00:00Z' });
export const goViewPreview = (project = goViewProject): GoViewProjectPreview => ({ projectId: project.id, projectKey: project.projectKey, projectName: project.name, versionNumber: Math.max(1, project.latestPublishedVersionNumber), canvasJson: project.canvasJson, generatedAtUtc: '2026-10-16T00:00:00Z' });
