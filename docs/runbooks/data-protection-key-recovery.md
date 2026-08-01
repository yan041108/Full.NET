# Runbook: Data Protection key recovery

## Scope

Shared RWX Data Protection Key Ring + historical X.509 certificates for cookie/auth token protection across API/Worker replicas.

## Preconditions

- Existing RWX PVC/Claim or verified StorageClass with RWX + snapshot + backup.
- Certificate private keys stored in the platform Secret manager (never Redis / Pod-local / emptyDir).

## Restore steps

1. Freeze rolling deploys that would rotate ApplicationName.
2. Restore Key Ring directory from the latest verified snapshot/backup to the RWX volume.
3. Restore historical certificates and private keys into `fullnet-dp-cert` (or configured Secret).
4. Restart API then Worker pods so they reload the Key Ring.
5. Verify login/refresh and existing auth cookies decrypt on at least two API pods.
6. Record RPO/RTO evidence; target RPO 0 / RTO 15 minutes per ADR-0005.

## Failure modes

- Missing historical cert: old cookies fail closed; force re-login only after explicit change window.
- Wrong ApplicationName: treat as new ring; do not silently merge unrelated key stores.
