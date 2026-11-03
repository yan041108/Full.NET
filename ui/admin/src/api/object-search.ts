import { http } from './http';
import { documentHostSearchItems, enterpriseRequestSearchRequests } from '@fullnet/client-contracts';
import { readObjectSearchPage } from '../search/objectSearch';

/** 只传受控关键词和有界页码；主体与时间范围始终由来源决定。 */
export const searchHostDocuments = async (query: string, page: number, signal: AbortSignal) =>
  readObjectSearchPage(await documentHostSearchItems(http, { q: query, page }, signal), page);
export const searchEnterpriseRequests = async (query: string, page: number, signal: AbortSignal) =>
  readObjectSearchPage(await enterpriseRequestSearchRequests(http, { q: query, page }, signal), page);
