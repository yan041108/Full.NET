namespace Full.NET.Modules.Workflow.Domain;

/// <summary>发布时拒绝能够同时执行的自动节点已知写集合重叠；动态人工补丁仍由运行时汇合裁定。</summary>
internal static class WorkflowParallelWritePolicy
{
    public static bool Validate(WorkflowDefinitionDraft draft,IReadOnlyDictionary<string,string[]> edges)
    {
        var nodes=draft.Nodes.ToDictionary(node=>node.NodeKey,StringComparer.Ordinal);
        foreach(var fork in draft.Nodes)
        {
            string? join=null; IReadOnlyList<string> branches=[];
            if(fork.NodeTypeKey=="gateway.parallel" && WorkflowParallelGatewayConfiguration.TryRead(fork.Config,out var parallel) && parallel!.Role==WorkflowParallelGatewayRole.Fork)
            {join=parallel.JoinNodeKey;branches=parallel.Branches.Select(branch=>branch.NextNodeKey).ToArray();}
            else if(fork.NodeTypeKey=="gateway.inclusive" && WorkflowInclusiveGatewayConfiguration.TryRead(fork.Config,null,out var inclusive) && inclusive!.Role==WorkflowInclusiveGatewayRole.Fork)
            {
                // 默认分支只在全部条件未命中时执行，不会与条件分支同时写入。
                join=inclusive.JoinNodeKey;branches=inclusive.Branches.Select(branch=>branch.NextNodeKey).ToArray();
            }
            if(join is null)continue;
            var combined=new HashSet<string>(StringComparer.Ordinal);
            foreach(var branch in branches)
            {
                var visited=new HashSet<string>(StringComparer.Ordinal);var writes=new HashSet<string>(StringComparer.Ordinal);var pending=new Stack<string>();pending.Push(branch);
                while(pending.TryPop(out var key))
                {
                    if(key==join || !visited.Add(key))continue;
                    var node=nodes[key];
                    if(node.NodeTypeKey is "data.modify" or "data.clear" && WorkflowFieldMutationPolicy.TryRead(node.NodeTypeKey,node.Config,null,out var mutation))
                        writes.UnionWith(mutation!.WrittenFields);
                    if(node.NodeTypeKey is "integration.trigger" or "integration.async_trigger" && WorkflowConnectorPolicy.TryRead(node.Config,null,out var connector))
                        writes.UnionWith(connector!.OutputFields.Keys);
                    if (WorkflowResultCallbackPolicy.TryRead(node.NodeTypeKey, node.Config, null, out var callback) && callback is not null)
                        writes.Add(callback.FieldKey);
                    foreach(var next in edges[key])pending.Push(next);
                }
                if(combined.Overlaps(writes))return false;
                combined.UnionWith(writes);
            }
        }
        return true;
    }
}
