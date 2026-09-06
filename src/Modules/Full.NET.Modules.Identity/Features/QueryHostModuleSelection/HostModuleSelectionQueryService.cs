using Full.NET.Abstractions.Results;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Configuration;

namespace Full.NET.Modules.Identity.Features.QueryHostModuleSelection;

/// <summary>
/// 提供部署期模块启用配置的只读分析与校验，不修改运行时 DI 注册。
/// </summary>
internal sealed class HostModuleSelectionQueryService(
    IConfiguration configuration,
    IFullNetModuleSelectionPreview selectionPreview)
{
    /// <summary>
    /// 返回当前进程 <c>FullNet:Modules</c> 配置的分析结果。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<Result<ModuleSelectionAnalysisResponse>> GetRuntimeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var analysis = selectionPreview.AnalyzeRuntime(configuration);
        return Task.FromResult(
            Result<ModuleSelectionAnalysisResponse>.Success(ToResponse(analysis)));
    }

    /// <summary>
    /// 校验候选预设或显式启用列表是否满足官方模块 DAG。
    /// </summary>
    /// <param name="request">候选配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<Result<ModuleSelectionAnalysisResponse>> ValidateAsync(
        ModuleSelectionValidateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var analysis = selectionPreview.AnalyzeCandidate(
            request.Preset,
            request.Enabled);
        return Task.FromResult(
            Result<ModuleSelectionAnalysisResponse>.Success(ToResponse(analysis)));
    }

    private static ModuleSelectionAnalysisResponse ToResponse(ModuleSelectionAnalysis analysis) =>
        new(
            analysis.IsValid,
            analysis.SourceKind,
            analysis.Preset,
            analysis.EnabledModuleKeys,
            analysis.OfficialModuleKeys,
            analysis.Issues
                .Select(issue => new ModuleSelectionIssueResponse(
                    issue.Code,
                    issue.Message,
                    issue.ModuleKey,
                    issue.RelatedModuleKey))
                .ToArray(),
            analysis.ModuleStates
                .Select(state => new ModuleSelectionModuleStateResponse(
                    state.ModuleKey,
                    state.IsEnabled,
                    state.Dependencies,
                    state.MissingDependencies))
                .ToArray(),
            analysis.DeploymentNotice);
}
