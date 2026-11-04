/** 行操作超出直接展示预算时，通过该行的“更多”定位到当前可见操作，避免点击其他行。 */
export async function clickEnterpriseRequestAction(page,row,name) {
  const inline=row.getByRole('button',{name,exact:true});
  if(await inline.isVisible()){await inline.click();return;}
  await row.getByTestId('art-table-action-more').click();
  await page.locator('.art-table-action-group__overflow-panel:visible').getByRole('button',{name,exact:true}).click();
}
