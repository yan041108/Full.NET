import {expect,test} from '@playwright/test';
import path from 'node:path';
import {clickMainNavLink,enterDevelopmentTenant,loginAsHostAdmin} from './support/real-stack-auth.mjs';

test('真实业务表单配置发布授权后，从申请记录预览并修正错误定位',async({page})=>{
 const record=process.env.FULLNET_PRINTING_BUSINESS_RECORD_ID,tenant=process.env.FULLNET_PRINTING_BUSINESS_TENANT_ID,evidence=process.env.FULLNET_PRINTING_CONFIGURATION_UI_EVIDENCE;
 if(!record||!tenant||!evidence)throw Error('须通过业务模板双库页面夹具准备记录。');
 await page.addInitScript(()=>localStorage.setItem('fullnet.admin.locale','zh-CN'));
 await loginAsHostAdmin(page);await clickMainNavLink(page,/打印预览/);await page.getByTestId('printing-preview-create').click();
 const editor=page.getByRole('dialog',{name:'新建模板草稿',exact:true});
 await editor.getByTestId('printing-template-key').fill('business-'+Date.now().toString(36));await editor.getByTestId('printing-template-name').fill('真实业务摘要模板');
 await editor.getByTestId('printing-template-layout').fill('<h1>{{title}}</h1><p>{{requestNumber}}</p><p>{{totalAmount}}</p>');
 await expect(page.getByTestId('printing-schema-choice')).toBeEnabled();await page.getByTestId('printing-schema-choice').click();
 await page.getByRole('option',{name:'企业申请摘要 · enterprise_request.request_summary',exact:true}).click();
 const created=page.waitForResponse(response=>new URL(response.url()).pathname==='/api/v1/printing/templates'&&response.request().method()==='POST');
 await page.getByTestId('printing-preview-submit').click();const creation=await created;expect(creation.status(),await creation.text()).toBe(201);const template=await creation.json();expect(template.formSchemaKey).toBe('enterprise_request.request_summary');expect(template.latestPublishedVersionNumber).toBe(0);
 await page.getByTestId('printing-template-publish').click();const published=page.waitForResponse(response=>new URL(response.url()).pathname===`/api/v1/printing/templates/${template.id}/publish`);
 await page.getByTestId('printing-template-confirm-publish').click();const publication=await published;expect(publication.status(),await publication.text()).toBe(200);
 await page.getByTestId('printing-tenant-grants-open').click();const grants=page.getByRole('dialog',{name:'租户版本授权：真实业务摘要模板',exact:true});
 await grants.getByTestId('printing-grant-tenant').fill(tenant);const granted=page.waitForResponse(response=>response.request().method()==='PUT'&&new URL(response.url()).pathname.includes('/tenant-grants/'));
 await grants.getByTestId('printing-grant-save').click();const grant=await granted;expect(grant.status(),await grant.text()).toBe(200);await expect(grants.getByTestId('printing-grant-receipt')).toContainText('操作已确认完成');await grants.getByRole('button',{name:'关闭',exact:true}).click();
 await enterDevelopmentTenant(page,'Acme Corporation');await clickMainNavLink(page,/企业申请/);
 const row=page.locator('.el-table__row').filter({hasText:'真实业务模板预览'});await expect(row).toHaveCount(1);
 let previews=0;page.on('request',request=>{if(request.method()==='POST'&&/\/printing\/published-templates\/[^/]+\/preview$/.test(new URL(request.url()).pathname))previews++;});
 await row.getByTestId('enterprise-request-print').click();await expect(page.getByTestId('printing-published-record')).toHaveValue(record);await expect(page.getByTestId('printing-published-preview')).toBeEnabled();expect(previews).toBe(0);
 // 错误定位不得保留旧结果；用户显式修正后再读取同一业务记录。
 await page.getByTestId('printing-published-record').fill('01980000-0000-7000-8000-000000000099');await page.getByTestId('printing-published-preview').click();await expect(page.getByTestId('printing-published-preview-problem')).toBeVisible();await expect(page.locator('.printing-preview-surface')).toHaveCount(0);
 await page.getByTestId('printing-published-record').fill(record);await page.getByTestId('printing-published-preview').click();await expect(page.locator('.printing-preview-surface h1')).toHaveText('真实业务模板预览');await expect(page.locator('.printing-preview-surface')).toContainText('42.00');expect(previews).toBe(2);
 for(const width of [1440,390]){await page.setViewportSize({width,height:1000});await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);await page.screenshot({path:path.join(evidence,`business-record-${width}.png`),animations:'disabled'});}
});
