using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>本人待办命令在本地事务成功返回前独立复核原凭据，失败沿正式 Result 边界回滚。</summary>
internal static class WorkflowTodoAuthorizedTransaction
{
    /// <summary>冻结精确权限；包括回执重放在内的成功结果均须保持原办理主体。</summary>
    /// <typeparam name="T">业务成功值。</typeparam>
    /// <param name="authorization">事务之外的权威凭据读取。</param>
    /// <param name="transaction">本地业务事务，不能被授权查询复用。</param>
    /// <param name="originalActor">外部准备前已验证的原主体。</param>
    /// <param name="permissions">与初始授权相同的精确权限。</param>
    /// <param name="action">本地命令及回执处理。</param>
    /// <param name="token">请求取消令牌。</param>
    internal static Task<Result<T>> ExecuteAsync<T>(IWorkflowTodoActorAuthorization authorization,
        ICommandTransaction transaction, WorkflowTodoActor originalActor, IReadOnlyList<string> permissions,
        Func<CancellationToken, Task<Result<T>>> action, CancellationToken token)
    {
        var required = permissions.ToArray();
        return transaction.ExecuteResultAsync(async inner =>
        {
            var result = await action(inner).ConfigureAwait(false);
            if (!result.IsSuccess) return result;
            var current = await authorization.AuthorizeAsync(originalActor.UserId, required, inner).ConfigureAwait(false);
            return current == originalActor ? result : Result<T>.Failure(new(
                WorkflowErrorCodes.TodoForbidden, "The original todo credential is no longer authorized.", ErrorType.Forbidden));
        }, token);
    }
}
