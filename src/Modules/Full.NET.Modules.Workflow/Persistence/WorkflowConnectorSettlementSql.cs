namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>取消及子根收敛共用R3证据口径；原尝试保持不可变，对账凭据只解除已证明的逻辑不确定性。</summary>
internal static class WorkflowConnectorSettlementSql
{
    internal const string Unresolved = """
        (connector.StatusKey='outcome_unknown' OR EXISTS (
            SELECT 1 FROM fn_workflow_connector_attempt AS attempt
            WHERE attempt.CallId=connector.Id AND attempt.TenantScopeKey=@TenantScopeKey
                AND (attempt.StatusKey='inflight' OR attempt.OutcomeKey='outcome_unknown' OR (connector.CancelRequestedAtUtc IS NOT NULL AND attempt.OutcomeKey='retryable'))
                AND connector.StatusKey<>'applied' AND NOT EXISTS (
                    SELECT 1 FROM fn_workflow_connector_resolution AS resolution
                    WHERE connector.StatusKey='cancelled' AND resolution.CallId=connector.Id AND resolution.InstanceId=connector.InstanceId AND resolution.TenantScopeKey=@TenantScopeKey
                        AND resolution.CreatedAtUtc>=attempt.StartedAtUtc
                        AND NOT EXISTS (SELECT 1 FROM fn_workflow_connector_resolution AS newer WHERE newer.CallId=resolution.CallId AND newer.TenantScopeKey=@TenantScopeKey
                            AND (newer.CreatedAtUtc>resolution.CreatedAtUtc OR (newer.CreatedAtUtc=resolution.CreatedAtUtc AND newer.Id>resolution.Id)))
                        AND ((resolution.ActionKey='confirm_no_effect' AND resolution.EvidenceReference IS NOT NULL AND resolution.EvidenceReference<>''
                            AND NOT EXISTS (SELECT 1 FROM fn_workflow_connector_attempt AS known WHERE known.CallId=connector.Id AND known.TenantScopeKey=@TenantScopeKey
                                AND (known.OutcomeKey='succeeded' OR known.HttpStatusCode BETWEEN 200 AND 299))) OR
                            (resolution.ActionKey='accept_result' AND resolution.AttemptId=connector.SelectedAttemptId AND EXISTS (
                                SELECT 1 FROM fn_workflow_connector_attempt AS selected WHERE selected.Id=resolution.AttemptId AND selected.CallId=connector.Id AND selected.TenantScopeKey=@TenantScopeKey
                                    AND selected.StatusKey='received' AND selected.OutcomeKey='succeeded' AND selected.RequestHash=connector.RequestHash AND selected.ResponseHash IS NOT NULL))))))
        """;
}
