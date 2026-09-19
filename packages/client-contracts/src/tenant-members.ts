export interface TenantMemberListQuery {
  page?: number;
  pageSize?: number;
  status?: string;
}

export interface TenantMember {
  id: string;
  tenantId: string;
  userId: string;
  username: string;
  displayName: string;
  memberRole: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
}

export interface TenantMemberPage {
  items: TenantMember[];
  page: number;
  pageSize: number;
  total: number;
}

export interface TenantInvitation {
  id: string;
  tenantId: string;
  targetEmail: string;
  targetUserId?: string | null;
  invitedByUserId: string;
  memberRole: string;
  status: string;
  expiresAtUtc: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
}

export interface TenantInvitationPage {
  items: TenantInvitation[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateTenantInvitationRequest {
  targetEmail: string;
  memberRole: string;
  expiresInHours?: number;
}
