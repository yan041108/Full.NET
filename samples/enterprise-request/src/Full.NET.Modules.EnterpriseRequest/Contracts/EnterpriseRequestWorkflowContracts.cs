namespace Full.NET.Modules.EnterpriseRequest.Contracts;

public static class EnterpriseRequestWorkflowConstants
{
    public const string BusinessType = "demo.enterprise_request";
    public const string DefinitionKey = "demo.enterprise_request.approval";
}

public static class EnterpriseRequestStatusKeys
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
}

public static class EnterpriseRequestWorkflowErrorCodes
{
    public const string InvalidStatus = "enterprise_request.enterprise_requests.invalid_status";
    public const string WorkflowDefinitionMissing = "enterprise_request.enterprise_requests.workflow_definition_missing";
    public const string WorkflowStartFailed = "enterprise_request.enterprise_requests.workflow_start_failed";
}

public static class EnterpriseRequestStatusTransition
{
    public static bool IsTerminal(string status) =>
        string.Equals(status, EnterpriseRequestStatusKeys.Approved, StringComparison.Ordinal)
        || string.Equals(status, EnterpriseRequestStatusKeys.Rejected, StringComparison.Ordinal)
        || string.Equals(status, EnterpriseRequestStatusKeys.Cancelled, StringComparison.Ordinal);
}