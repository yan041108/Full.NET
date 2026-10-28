using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>仅沿实际父步骤读取祖先；所有可达路径必须闭合到真实Start身份，缺边或环不能用历史顺序补齐。</summary>
internal static class WorkflowCausalGraph
{
    public static bool TryReadAncestors(Guid instanceId,string scope,Guid currentStepId,IReadOnlyList<WorkflowStepCauseRecord> edges,
        out IReadOnlySet<Guid> ancestors)
    {
        var found=new HashSet<Guid>();ancestors=new HashSet<Guid>();
        var maximum=WorkflowActivityScheduler.InstanceActivityBudget;
        var branchMaximum=Math.Max(WorkflowGatewayLimits.MaximumBranches("gateway.parallel"),WorkflowGatewayLimits.MaximumBranches("gateway.inclusive"));
        if(instanceId==Guid.Empty || currentStepId==Guid.Empty || string.IsNullOrWhiteSpace(scope) || edges.Count>maximum*branchMaximum ||
            edges.Any(edge=>edge.InstanceId!=instanceId || edge.TenantScopeKey!=scope || edge.StepId==Guid.Empty || edge.ParentStepId==Guid.Empty ||
                edge.StepId==edge.ParentStepId || edge.KindKey is not ("sequence" or "fork" or "join" or "region" or "return"))) return false;
        var pairs=new HashSet<(Guid,Guid)>();
        foreach(var edge in edges)if(!pairs.Add((edge.StepId,edge.ParentStepId)))return false;
        var parents=edges.GroupBy(edge=>edge.StepId).ToDictionary(group=>group.Key,group=>group.Select(edge=>edge.ParentStepId).ToArray());
        if(parents.ContainsKey(instanceId))return false;
        var colors=new Dictionary<Guid,byte>();var stack=new Stack<(Guid Step,bool Exiting)>();stack.Push((currentStepId,false));
        while(stack.TryPop(out var item))
        {
            if(item.Step==instanceId){found.Add(item.Step);continue;}
            if(item.Exiting){colors[item.Step]=2;continue;}
            if(colors.TryGetValue(item.Step,out var color)){if(color==1)return false;continue;}
            if(colors.Count>=maximum-1 || !parents.TryGetValue(item.Step,out var previous) || previous.Length==0 || previous.Length>branchMaximum)return false;
            colors.Add(item.Step,1);found.Add(item.Step);stack.Push((item.Step,true));
            foreach(var parent in previous)stack.Push((parent,false));
        }
        found.Remove(currentStepId);ancestors=found;return true;
    }
}
