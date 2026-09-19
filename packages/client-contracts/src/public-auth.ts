export interface RegisterAccountRequest {
  email: string;
  displayName: string;
  password: string;
  challengeId: string;
  challengeCode: string;
  registrationWayId?: string;
  invitationId?: string;
  invitationToken?: string;
}

export interface RecoverPasswordConfirmRequest {
  challengeId: string;
  challengeCode: string;
  newPassword: string;
}

export interface VerifyRegistrationInvitationResponse {
  invitationId: string;
  tenantId: string;
  email: string;
  registrationWayId: string;
  expiresAtUtc: string;
}
