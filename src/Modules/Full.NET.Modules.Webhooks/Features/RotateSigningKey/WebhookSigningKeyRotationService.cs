using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions.Persistence;
using Full.NET.Modules.Webhooks.Security;
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Webhooks.Features.RotateSigningKey;

/// <summary>先准备公开身份供接收方配置，再明确激活；每个订阅最多一个准备，完成即清除准备秘密。</summary>
internal sealed class WebhookSigningKeyRotationService(IQueryExecutor queries,ICommandExecutor commands,ICommandTransaction transaction,
    IDatabaseTransactionBoundSessionLock locks,ICurrentTenant tenant,ICurrentSessionAuthorization sessions,IIndependentCurrentSessionAuthorization independent,
    WebhookSigningSecretProtector secrets,IClock clock,IIdGenerator ids)
{
    private static readonly string[] ReadRights=[WebhookPermissions.SubscriptionsRead];
    private static readonly string[] WriteRights=[WebhookPermissions.SubscriptionsRead,WebhookPermissions.SigningKeysRotate];

    internal async Task<Result<WebhookSigningKeyRotationReceipt>> ReadAsync(Guid subscriptionId,Guid? operationId,CancellationToken token)
    {
        var actor=await sessions.AuthorizeAllAsync(ReadRights,token).ConfigureAwait(false);
        if(!Valid(actor))return Denied();
        if(await SubscriptionAsync(subscriptionId,token).ConfigureAwait(false) is null)return NotFound(true);
        var row=await queries.QuerySingleOrDefaultAsync<WebhookSigningKeyRotationRecord>(operationId is null?WebhookSigningKeyRotationSql.Pending:WebhookSigningKeyRotationSql.Find,
            WebhookSqlParameters.Create(("SubscriptionId",subscriptionId),("OperationId",operationId)),token).ConfigureAwait(false);
        return row?.SubscriptionId==subscriptionId?Result<WebhookSigningKeyRotationReceipt>.Success(row.Receipt()):NotFound(false);
    }

    internal async Task<Result<WebhookSigningKeyRotationReceipt>> PrepareAsync(Guid subscriptionId,PrepareWebhookSigningKeyRotationRequest request,CancellationToken token)
    {
        if(subscriptionId==Guid.Empty||request.OperationId==Guid.Empty||request.Version is <=0 or >=int.MaxValue-1||request.SigningSecret is not {Length:>=16 and <=128})return Invalid();
        var actor=await sessions.AuthorizeAllAsync(WriteRights,token).ConfigureAwait(false);if(!Valid(actor))return Denied();
        if(await SubscriptionAsync(subscriptionId,token).ConfigureAwait(false) is null)return NotFound(true);
        if(!independent.HasIndependentReadCapacity)throw new ServiceCapacityExceededException(ServiceCapacityFailureKind.Rejected,TimeSpan.FromMilliseconds(250));
        await using var lease=await locks.TryAcquireAsync(Resource(actor!.TenantId!.Value,request.OperationId),token).ConfigureAwait(false);if(lease is null)return Conflict();
        return await transaction.ExecuteResultAsync(async cancellation=>
        {
            await lease.EnsureHeldAsync(cancellation).ConfigureAwait(false);if(!Valid(actor))return Denied();
            var existing=await FindAsync(request.OperationId,cancellation).ConfigureAwait(false);
            if(existing is not null)return existing.SubscriptionId==subscriptionId&&existing.ActorUserId==actor.UserId&&existing.PreviousVersion==request.Version&&secrets.MatchesFingerprint(request.SigningSecret,existing.ProtectedFingerprint)
                ?Result<WebhookSigningKeyRotationReceipt>.Success(existing.Receipt()):Conflict();
            var current=await SubscriptionAsync(subscriptionId,cancellation).ConfigureAwait(false);if(current is null)return NotFound(true);
            if(current.Version!=request.Version)return Conflict();
            var encrypted=secrets.Protect(request.SigningSecret);var fingerprint=secrets.ProtectFingerprint(request.SigningSecret);if(encrypted.Length>1024||fingerprint.Length>1024)return Invalid();
            var values=WebhookSqlParameters.Create(("Id",ids.NewId()),("OperationId",request.OperationId),("ActorUserId",actor.UserId),("ActorSessionId",actor.SessionId),
                ("SubscriptionId",subscriptionId),("PreviousKeyId",current.SigningKeyId),("PreviousVersion",request.Version),("PreparedVersion",request.Version+1),
                ("ProtectedSecret",encrypted),("ProtectedFingerprint",fingerprint),("Now",clock.UtcNow));
            if(await commands.ExecuteAsync(WebhookSigningKeyRotationSql.Reserve,values,cancellation).ConfigureAwait(false)!=1)return Conflict();
            if(await commands.ExecuteAsync(WebhookSigningKeyRotationSql.Insert,values,cancellation).ConfigureAwait(false)!=1)throw new InvalidOperationException("The prepared key has no rotation audit.");
            if(!await ConfirmAsync(actor,cancellation).ConfigureAwait(false))return Denied();await lease.EnsureHeldAsync(cancellation).ConfigureAwait(false);
            return Result<WebhookSigningKeyRotationReceipt>.Success((await FindAsync(request.OperationId,cancellation).ConfigureAwait(false))!.Receipt());
        },token).ConfigureAwait(false);
    }

    internal async Task<Result<WebhookSigningKeyRotationReceipt>> CompleteAsync(Guid subscriptionId,Guid operationId,CompleteWebhookSigningKeyRotationRequest request,bool activate,CancellationToken token)
    {
        if(subscriptionId==Guid.Empty||operationId==Guid.Empty||request.SigningKeyId==Guid.Empty||request.Version is <=0 or >=int.MaxValue)return Invalid();
        var actor=await sessions.AuthorizeAllAsync(WriteRights,token).ConfigureAwait(false);if(!Valid(actor))return Denied();
        if(await SubscriptionAsync(subscriptionId,token).ConfigureAwait(false) is null)return NotFound(true);
        if(!independent.HasIndependentReadCapacity)throw new ServiceCapacityExceededException(ServiceCapacityFailureKind.Rejected,TimeSpan.FromMilliseconds(250));
        await using var lease=await locks.TryAcquireAsync(Resource(actor!.TenantId!.Value,operationId),token).ConfigureAwait(false);if(lease is null)return Conflict();
        return await transaction.ExecuteResultAsync(async cancellation=>
        {
            await lease.EnsureHeldAsync(cancellation).ConfigureAwait(false);if(!Valid(actor))return Denied();
            var row=await FindAsync(operationId,cancellation).ConfigureAwait(false);if(row?.SubscriptionId!=subscriptionId)return NotFound(false);
            if(row.Id!=request.SigningKeyId)return Conflict();var state=activate?"activated":"cancelled";
            if(row.StateKey!="prepared")return row.StateKey==state&&row.CompletedVersion==request.Version+1&&row.CompletedByUserId==actor.UserId
                ?Result<WebhookSigningKeyRotationReceipt>.Success(row.Receipt()):Conflict();
            if(row.ProtectedSecret is null)throw new InvalidOperationException("The prepared key material is missing.");
            var values=WebhookSqlParameters.Create(("Id",row.Id),("SubscriptionId",subscriptionId),("PreviousKeyId",row.PreviousKeyId),("Version",request.Version),
                ("ProtectedSecret",row.ProtectedSecret),("StateKey",state),("CompletedVersion",request.Version+1),("ActorUserId",actor.UserId),("ActorSessionId",actor.SessionId),("Now",clock.UtcNow));
            if(await commands.ExecuteAsync(activate?WebhookSigningKeyRotationSql.Activate:WebhookSigningKeyRotationSql.Cancel,values,cancellation).ConfigureAwait(false)!=1)return Conflict();
            if(await commands.ExecuteAsync(WebhookSigningKeyRotationSql.Complete,values,cancellation).ConfigureAwait(false)!=1)throw new InvalidOperationException("The completed rotation has no audit.");
            if(!await ConfirmAsync(actor,cancellation).ConfigureAwait(false))return Denied();await lease.EnsureHeldAsync(cancellation).ConfigureAwait(false);
            return Result<WebhookSigningKeyRotationReceipt>.Success((await FindAsync(operationId,cancellation).ConfigureAwait(false))!.Receipt());
        },token).ConfigureAwait(false);
    }

    private bool Valid(AuthorizedSessionActor? actor)=>actor?.TenantId is not null&&!tenant.IsHost&&tenant.Id==actor.TenantId;
    private async Task<bool> ConfirmAsync(AuthorizedSessionActor actor,CancellationToken token)
    {
        // 身份权威复核必须使用独立连接，禁止加入本模块写事务；失败 Result 使整个写入回滚。
        foreach(var permission in WriteRights)if(await independent.AuthorizeAsync(permission,token).ConfigureAwait(false)!=actor)return false;
        return Valid(actor);
    }
    private Task<WebhookSubscriptionRecord?> SubscriptionAsync(Guid id,CancellationToken token)=>queries.QuerySingleOrDefaultAsync<WebhookSubscriptionRecord>(WebhookSubscriptionSql.FindById,WebhookSqlParameters.Create(("Id",id)),token);
    private Task<WebhookSigningKeyRotationRecord?> FindAsync(Guid operation,CancellationToken token)=>queries.QuerySingleOrDefaultAsync<WebhookSigningKeyRotationRecord>(WebhookSigningKeyRotationSql.Find,WebhookSqlParameters.Create(("OperationId",operation)),token);
    private static string Resource(Guid tenantId,Guid operation)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"webhooks.rotation:{tenantId:N}:{operation:N}")));
    private static Result<WebhookSigningKeyRotationReceipt> Invalid()=>Result<WebhookSigningKeyRotationReceipt>.Failure(new(ValidationErrorCodes.Failed,"A bounded original version, operation and signing key are required.",ErrorType.Validation));
    private static Result<WebhookSigningKeyRotationReceipt> Conflict()=>Result<WebhookSigningKeyRotationReceipt>.Failure(new(WebhookErrorCodes.SigningKeyRotationConflict,"The rotation or subscription revision conflicts. Read the original rotation and current subscription before confirming.",ErrorType.Conflict));
    private static Result<WebhookSigningKeyRotationReceipt> Denied()=>Result<WebhookSigningKeyRotationReceipt>.Failure(new("authorization.forbidden","The current tenant session and signing key permissions are required.",ErrorType.Forbidden));
    private static Result<WebhookSigningKeyRotationReceipt> NotFound(bool subscription)=>Result<WebhookSigningKeyRotationReceipt>.Failure(new(subscription?WebhookErrorCodes.SubscriptionNotFound:WebhookErrorCodes.SigningKeyRotationNotFound,"The original subscription or rotation was not found.",ErrorType.NotFound));
}
