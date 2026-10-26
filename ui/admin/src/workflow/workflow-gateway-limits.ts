import limits from '../../../../contracts/workflow/gateway-limits.json';

/** 条件分支上下限不包含默认分支；并行分支没有默认出口。 */
export const workflowGatewayLimits = limits;

/** 将来源路由类型映射到共享限制，未知类型不能添加分支。 */
export function maximumDesignerGatewayBranches(sourceType: number): number {
  const constraint = sourceType === 10 ? limits['gateway.exclusive']
    : sourceType === 12 ? limits['gateway.parallel']
      : sourceType === 13 ? limits['gateway.inclusive'] : undefined;
  return constraint === undefined ? 0 : constraint.maximumBranches + Number(constraint.hasDefaultBranch);
}
