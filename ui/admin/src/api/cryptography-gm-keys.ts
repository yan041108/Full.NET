import {
  isCryptographyKey,
  isCryptographyStatus,
  isSm2SignResponse,
  isSm2VerifyResponse,
  type CryptographyKey,
  type CryptographyStatus,
  type Sm2SignRequest,
  type Sm2SignResponse,
  type Sm2VerifyRequest,
  type Sm2VerifyResponse
} from '@fullnet/client-contracts';
import { request } from './http';

/** 读取国密部署状态。 */
export async function getCryptographyStatus(
  signal?: AbortSignal
): Promise<CryptographyStatus> {
  const value = await request<unknown>(
    '/api/v1/cryptography/status',
    { method: 'GET' },
    signal
  );
  if (!isCryptographyStatus(value)) {
    throw new Error('client.invalid_cryptography_status');
  }

  return value;
}

/** 列出国密密钥目录。 */
export async function listCryptographyKeys(
  signal?: AbortSignal
): Promise<CryptographyKey[]> {
  const value = await request<unknown>(
    '/api/v1/cryptography/keys',
    { method: 'GET' },
    signal
  );
  if (!Array.isArray(value) || !value.every(isCryptographyKey)) {
    throw new Error('client.invalid_cryptography_keys');
  }

  return value;
}

/** 执行 SM2 签名。 */
export async function sm2Sign(
  body: Sm2SignRequest,
  signal?: AbortSignal
): Promise<Sm2SignResponse> {
  const value = await request<unknown>(
    '/api/v1/cryptography/sm2/sign',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isSm2SignResponse(value)) {
    throw new Error('client.invalid_sm2_sign_response');
  }

  return value;
}

/** 执行 SM2 验签。 */
export async function sm2Verify(
  body: Sm2VerifyRequest,
  signal?: AbortSignal
): Promise<Sm2VerifyResponse> {
  const value = await request<unknown>(
    '/api/v1/cryptography/sm2/verify',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isSm2VerifyResponse(value)) {
    throw new Error('client.invalid_sm2_verify_response');
  }

  return value;
}

export type {
  CryptographyKey,
  CryptographyStatus,
  Sm2SignRequest,
  Sm2SignResponse,
  Sm2VerifyRequest,
  Sm2VerifyResponse
};
