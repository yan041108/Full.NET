export interface TenantSubscription {
  id: string;
  tenantId: string;
  packageId?: string | null;
  status: string;
  trialEndsAtUtc?: string | null;
  currentPeriodStartUtc: string;
  currentPeriodEndUtc: string;
  cancelledAtUtc?: string | null;
  version: number;
}

export interface CreateTenantSubscriptionBody {
  packageId?: string | null;
  status: string;
  trialEndsAtUtc?: string | null;
  currentPeriodStartUtc: string;
  currentPeriodEndUtc: string;
}
