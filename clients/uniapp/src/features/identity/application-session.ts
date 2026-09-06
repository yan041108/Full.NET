import type { ConfigurableHttpClient } from '../../api/http';
import type { IdentitySessionController } from './identity-session-types';

// #ifdef H5
import {
  h5HttpClient,
  h5IdentitySession,
  restoreH5IdentitySession
} from './h5-application-session';
// #endif

// #ifdef MP-WEIXIN
import {
  mpWeixinHttpClient,
  mpWeixinIdentitySession,
  restoreMpWeixinIdentitySession
} from './mp-weixin-application-session';
// #endif

let businessRuntimeAvailable = false;
// #ifdef H5
businessRuntimeAvailable = true;
// #endif
// #ifdef MP-WEIXIN
businessRuntimeAvailable = true;
// #endif

/** 当前编译目标是否支持工作流与站内信业务运行时。 */
export const isBusinessRuntimeAvailable = businessRuntimeAvailable;

export const httpClient: ConfigurableHttpClient =
  // #ifdef H5
  h5HttpClient
  // #endif
  // #ifdef MP-WEIXIN
  mpWeixinHttpClient
  // #endif
  ;

export const identitySession: IdentitySessionController =
  // #ifdef H5
  h5IdentitySession
  // #endif
  // #ifdef MP-WEIXIN
  mpWeixinIdentitySession
  // #endif
  ;

export async function restoreIdentitySession(): Promise<boolean> {
  // #ifdef H5
  return await restoreH5IdentitySession();
  // #endif
  // #ifdef MP-WEIXIN
  return await restoreMpWeixinIdentitySession();
  // #endif
  return false;
}
