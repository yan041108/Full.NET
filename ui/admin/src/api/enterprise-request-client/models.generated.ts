// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 数据模型。

export interface CreateEnterpriseRequestAggregateRequest {
  readonly details: Array<EnterpriseRequestDetailInput>;
  readonly master: CreateEnterpriseRequestRequest;
}

export interface CreateEnterpriseRequestDetailRequest {
  readonly itemDescription: string;
  readonly lineAmount: string;
  readonly lineNumber: number;
  readonly quantity: string;
  readonly requestId: string;
  readonly unitPrice: string;
}

export interface CreateEnterpriseRequestRequest {
  readonly applicantUserId: string;
  readonly requestNumber: string;
  readonly status: string;
  readonly title: string;
  readonly totalAmount: string;
}

export interface DeleteEnterpriseRequestDetailRequest {
  readonly version: string;
}

export interface DeleteEnterpriseRequestRequest {
  readonly version: string;
}

export interface EnterpriseRequestAggregateResponse {
  readonly details: Array<EnterpriseRequestDetailResponse>;
  readonly master: EnterpriseRequestResponse;
}

export interface EnterpriseRequestDetailEdit {
  readonly id: string;
  readonly input: EnterpriseRequestDetailInput;
  readonly version: string;
}

export interface EnterpriseRequestDetailInput {
  readonly itemDescription: string;
  readonly lineAmount: string;
  readonly lineNumber: number;
  readonly quantity: string;
  readonly unitPrice: string;
}

export interface EnterpriseRequestDetailRemoval {
  readonly id: string;
  readonly version: string;
}

export interface EnterpriseRequestDetailResponse {
  readonly createdAtUtc: string;
  readonly createdById: string;
  readonly deletedAtUtc: null | string;
  readonly deletedById: null | string;
  readonly id: string;
  readonly isDeleted: boolean;
  readonly itemDescription: string;
  readonly lineAmount: string;
  readonly lineNumber: number;
  readonly organizationUnitId: string;
  readonly quantity: string;
  readonly requestId: string;
  readonly tenantId: string;
  readonly unitPrice: string;
  readonly updatedAtUtc: null | string;
  readonly updatedById: null | string;
  readonly version: string;
}

export interface EnterpriseRequestResponse {
  readonly applicantUserId: string;
  readonly createdAtUtc: string;
  readonly createdById: string;
  readonly deletedAtUtc: null | string;
  readonly deletedById: null | string;
  readonly id: string;
  readonly isDeleted: boolean;
  readonly organizationUnitId: string;
  readonly requestNumber: string;
  readonly status: string;
  readonly tenantId: string;
  readonly title: string;
  readonly totalAmount: string;
  readonly updatedAtUtc: null | string;
  readonly updatedById: null | string;
  readonly version: string;
}

export interface PagedResultOfEnterpriseRequestDetailResponse {
  readonly items: Array<EnterpriseRequestDetailResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PagedResultOfEnterpriseRequestResponse {
  readonly items: Array<EnterpriseRequestResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface ProblemDetails {
  readonly detail?: null | string;
  readonly instance?: null | string;
  readonly status?: null | number;
  readonly title?: null | string;
  readonly type?: null | string;
}

export interface UpdateEnterpriseRequestAggregateRequest {
  readonly added: Array<EnterpriseRequestDetailInput>;
  readonly master: UpdateEnterpriseRequestRequest;
  readonly removed: Array<EnterpriseRequestDetailRemoval>;
  readonly updated: Array<EnterpriseRequestDetailEdit>;
}

export interface UpdateEnterpriseRequestDetailRequest {
  readonly itemDescription: string;
  readonly lineAmount: string;
  readonly lineNumber: number;
  readonly quantity: string;
  readonly requestId: string;
  readonly unitPrice: string;
  readonly version: string;
}

export interface UpdateEnterpriseRequestRequest {
  readonly applicantUserId: string;
  readonly requestNumber: string;
  readonly status: string;
  readonly title: string;
  readonly totalAmount: string;
  readonly version: string;
}
