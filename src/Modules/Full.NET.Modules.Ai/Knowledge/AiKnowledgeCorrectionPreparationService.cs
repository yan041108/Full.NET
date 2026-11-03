using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>Worker 本地来源准备；没有模型、预算或索引依赖，解析预览不能隐式产生收费。</summary>
internal sealed class AiKnowledgeCorrectionPreparationService(AiKnowledgeCorrectionAuthority authority, AiKnowledgeCorrectionStore store,
    IDataTransactionState transactions, IHostFileContentReader hostFiles, ITenantResourceFileStore tenantFiles,
    AiKnowledgePdfSandboxOptions? pdf = null, AiKnowledgeDocxSandboxOptions? docx = null,
    AiKnowledgeDocumentSandboxOptions? documents = null, AiKnowledgeDocumentParseGate? parseGate = null,
    IOptions<AiKnowledgePdfRuntimeOptions>? pdfRuntime = null, IOptions<AiKnowledgeDocxRuntimeOptions>? docxRuntime = null)
{
    private const string Permission = AiKnowledgeCorrectionPermissions.Prepare;
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(5);

    /// <summary>只消费原交互会话和当前授权版本；重新解析写独立来源，不改人工头、索引或发布。</summary>
    internal async Task<AiKnowledgeCorrectionSourceRecord?> PrepareAsync(AiKnowledgeCorrectionStore.Binding binding,
        SessionBindingSnapshot actor, Guid requestKey, CancellationToken token, AiKnowledgeCorrectionPreparationLease? preparation = null)
    {
        authority.Check(token);
        if (requestKey == Guid.Empty || binding.OwnerUserId != actor.UserId || binding.DocumentVersion < 1) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(documents is { Enabled: true } ? documents.TimeoutMs + 25000 : 50000); var bounded = deadline.Token;
        var context = await authority.ReadAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, actor, Permission, bounded, binding.DocumentVersion).ConfigureAwait(false);
        if (context is null || context.Binding != binding) return null;
        var runtime = RuntimeFor(context.Version.ContentType);
        if (runtime is null || preparation is not null && (preparation.Id != requestKey || preparation.ParserRuntimeId != runtime)) return null;
        if (preparation is not null && preparation.RequestHash != RequestHash(context, actor, "queue:knowledge-correction-v1", bounded)) return null;
        var documentRuntime = documents is { Enabled: true } && AiKnowledgeDocumentFormats.IsDocument(context.Version.ContentType);
        var requestHash = RequestHash(context, actor, runtime, bounded);
        var existing = await store.ReadRequestedSourceAsync(binding, requestKey, bounded).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash || existing.ActorUserId != actor.UserId || !Valid(existing, context, bounded)) return null;
            if (!await authority.StillCurrentAsync(context, actor, Permission, bounded).ConfigureAwait(false)) return null;
            if (preparation is null) return existing;
            var v = context.Version;
            var snapshot = AiKnowledgeCorrectionSnapshot.Verify(existing, null, AiKnowledgeDocumentFormats.Format(v.ContentType)!.Value, v.ContentHash,
                checked((int)v.SizeBytes), AiKnowledgeDocumentFormats.Policy(v.ContentType, v.ParsingPolicyKey)!, bounded);
            if (snapshot is null) return null;
            var recovered = await store.CreateSourceAsync(binding, requestKey, requestHash, snapshot.Document.Source, runtime, bounded, preparation).ConfigureAwait(false);
            return recovered.IsSuccess && await authority.StillCurrentAsync(context, actor, Permission, bounded).ConfigureAwait(false) ? recovered.Value : null;
        }
        if (documentRuntime && parseGate is null) throw new InvalidOperationException("Correction document parsing requires admission.");
        using var slot = documentRuntime ? await parseGate!.EnterAsync(bounded).ConfigureAwait(false) : null;
        if (!await authority.StillCurrentAsync(context, actor, Permission, bounded).ConfigureAwait(false)) return null;
        using var cleanup = AiKnowledgePdfIoBoundary.ReserveCleanup(); Stream? owned = null;
        try
        {
            var opened = await OpenAsync(context, actor, cleanup, bounded).ConfigureAwait(false);
            if (opened is null) return null; owned = opened.Content;
            if (opened.ContentType != context.Version.ContentType || opened.FileName != context.Version.OriginalFileName
                || !await authority.StillCurrentAsync(context, actor, Permission, bounded).ConfigureAwait(false)) return null;
            var transferred = owned; owned = null;
            var maximum = context.Version.ContentType == "application/pdf" && !documentRuntime ? 5242880 : checked((int)AiKnowledgeFilePolicy.Limit(context.Version.ContentType));
            using var bytes = await new AiKnowledgePdfSourceReader(transactions, 25000, maximum)
                .ReadAsync(transferred, context.Version.ContentHash, context.Version.SizeBytes, bounded, cleanup).ConfigureAwait(false);
            if (bytes is null || !await authority.StillCurrentAsync(context, actor, Permission, bounded).ConfigureAwait(false)) return null;
            var parsed = await ParseAsync(context, bytes.Bytes, documentRuntime, bounded).ConfigureAwait(false);
            if (parsed is null || !AiKnowledgeDocumentFormats.Matches(parsed, context.Version.ContentType, context.Version.ParsingPolicyKey)
                || !await authority.StillCurrentAsync(context, actor, Permission, bounded).ConfigureAwait(false)) return null;
            // 原解析、分块身份和首次工作台头在短 AI 事务内保存，外部授权已在事务外完成。
            var saved = await store.CreateSourceAsync(binding, requestKey, requestHash, parsed, runtime, bounded, preparation).ConfigureAwait(false);
            authority.Check(bounded);
            return saved.IsSuccess && Valid(saved.Value!, context, bounded)
                && await authority.StillCurrentAsync(context, actor, Permission, bounded).ConfigureAwait(false) ? saved.Value : null;
        }
        finally { if (owned is not null) await Close(owned, cleanup).ConfigureAwait(false); }
    }

    internal string? RuntimeFor(string contentType)
    {
        if (AiKnowledgeDocumentFormats.Format(contentType) is AiKnowledgeParsedFormat.PlainText or AiKnowledgeParsedFormat.Markdown) return "managed:utf8-text-v1";
        if (documents is { Enabled: true } && AiKnowledgeDocumentFormats.IsDocument(contentType)) return Image(documents.ImageId) ? documents.ImageId : null;
        return contentType switch
        {
            "application/pdf" when pdfRuntime?.Value.Enabled == true && pdf is not null && Image(pdf.ImageId) => pdf.ImageId,
            AiKnowledgeFilePolicy.DocxContentType when docxRuntime?.Value.Enabled == true && docx is not null && Image(docx.ImageId) => docx.ImageId,
            _ => null
        };
    }
    private static bool Image(string id) => id is { Length: 71 } && id.StartsWith("sha256:", StringComparison.Ordinal) && id.Skip(7).All(char.IsAsciiHexDigit);
    private static bool Valid(AiKnowledgeCorrectionSourceRecord source, AiKnowledgeCorrectionContext context, CancellationToken token) =>
        source.VersionId == context.Binding.VersionId && AiKnowledgeCorrectionSnapshot.Verify(source, null,
            AiKnowledgeDocumentFormats.Format(context.Version.ContentType)!.Value, context.Version.ContentHash,
            checked((int)context.Version.SizeBytes), AiKnowledgeDocumentFormats.Policy(context.Version.ContentType, context.Version.ParsingPolicyKey)!, token) is not null;

    private async Task<AiKnowledgeParsedDocument?> ParseAsync(AiKnowledgeCorrectionContext context, ReadOnlyMemory<byte> bytes, bool documentRuntime, CancellationToken token)
    {
        authority.Check(token); var v = context.Version;
        if (AiKnowledgeDocumentFormats.Format(v.ContentType) is AiKnowledgeParsedFormat.PlainText or AiKnowledgeParsedFormat.Markdown)
        {
            var captured = bytes.ToArray();
            try
            {
                using var stream = new MemoryStream(captured, false);
                var parsed = await AiKnowledgeTextParser.ParseAsync(stream, v.OriginalFileName, v.ContentType, v.ContentHash, token).ConfigureAwait(false);
                return parsed.Document is { } text ? AiKnowledgeParsedDocument.FromText(text,
                    v.ContentType == "text/plain" ? AiKnowledgeTextFormat.PlainText : AiKnowledgeTextFormat.Markdown, token) : null;
            }
            finally { CryptographicOperations.ZeroMemory(captured); }
        }
        if (documentRuntime) return await new AiKnowledgeDocumentSandboxParser(transactions, documents!).ParseAsync(bytes, v.ContentHash,
            checked((int)v.SizeBytes), v.ContentType, v.ParsingPolicyKey, token).ConfigureAwait(false);
        if (v.ContentType == "application/pdf")
        {
            var parsed = await new AiKnowledgePdfSandboxParser(transactions, pdf!).ParseAsync(bytes, v.ContentHash, checked((int)v.SizeBytes), token).ConfigureAwait(false);
            return parsed is null ? null : AiKnowledgeParsedDocument.FromPdf(parsed, token);
        }
        var word = await new AiKnowledgeDocxSandboxParser(transactions, docx!).ParseAsync(bytes, v.ContentHash, checked((int)v.SizeBytes), token).ConfigureAwait(false);
        return word is null ? null : AiKnowledgeParsedDocument.FromDocx(word, token);
    }

    private sealed record Opened(Stream Content, string ContentType, string FileName);
    private async Task<Opened?> OpenAsync(AiKnowledgeCorrectionContext context, SessionBindingSnapshot actor, AiKnowledgePdfIoScheduler.Reservation cleanup, CancellationToken token)
    {
        authority.Check(token); var io = new CancellationTokenSource(); var delivered = false; var retained = cleanup.Retain();
        var pending = AiKnowledgePdfIoBoundary.RunAsync<Opened?>(async () =>
        {
            authority.Check(io.Token); var v = context.Version;
            if (actor.TenantId.HasValue)
            {
                var opened = await tenantFiles.OpenReadyContentAsync("ai", v.Id, v.FileId, io.Token).ConfigureAwait(false);
                return opened.IsSuccess ? new(opened.Value!.Content, opened.Value.ContentType, opened.Value.OriginalFileName) : null;
            }
            var host = await hostFiles.OpenReadyContentAsync(v.FileId, io.Token).ConfigureAwait(false);
            return host.IsSuccess ? new(host.Value!.Content, host.Value.ContentType, host.Value.OriginalFileName) : null;
        });
        try
        {
            var result = await pending.WaitAsync(token).ConfigureAwait(false);
            authority.Check(token); delivered = true; return result;
        }
        finally
        {
            if (delivered) { retained.Dispose(); io.Dispose(); }
            else
            {
                // 开流迟到也由保留的清理准入接管；超时不意味着外部操作已经停止。
                async Task Complete()
                {
                    try
                    {
                        Opened? result;
                        try { result = await pending.ConfigureAwait(false); } catch { return; }
                        if (result is not null) await Close(result.Content, cleanup).ConfigureAwait(false);
                    }
                    finally { retained.Dispose(); }
                }
                var ending = Task.WhenAll(Observe(io.CancelAsync()), Complete());
                try { await ending.WaitAsync(CleanupLimit).ConfigureAwait(false); }
                catch (TimeoutException) { throw new IOException("Correction source opening cleanup was not confirmed."); }
                finally { if (ending.IsCompleted) io.Dispose(); else _ = DisposeLater(ending, io); }
            }
        }
    }
    private static async Task Close(Stream stream, AiKnowledgePdfIoScheduler.Reservation cleanup)
    {
        var closing = cleanup.RunAsync(async () => await stream.DisposeAsync().ConfigureAwait(false));
        try { await closing.WaitAsync(CleanupLimit).ConfigureAwait(false); }
        catch (TimeoutException) { _ = Observe(closing); throw new IOException("Correction source disposal was not confirmed."); }
    }
    private static async Task Observe(Task task) { try { await task.ConfigureAwait(false); } catch { } }
    private static async Task DisposeLater(Task ending, CancellationTokenSource io) { await Observe(ending).ConfigureAwait(false); io.Dispose(); }
    internal static string RequestHash(AiKnowledgeCorrectionContext context, SessionBindingSnapshot actor, string runtime, CancellationToken token)
    {
        // BinaryWriter 字符串自带长度；完整原会话参与幂等，新的登录不能接管旧准备请求。
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, new UTF8Encoding(false, true), leaveOpen: true);
        var b = context.Binding; var v = context.Version;
        try
        {
            writer.Write("knowledge-correction-prepare-v1"); writer.Write(b.KnowledgeBaseId.ToString("N")); writer.Write(b.DocumentId.ToString("N"));
            writer.Write(b.VersionId.ToString("N")); writer.Write(b.DocumentVersion); writer.Write(v.FileId.ToString("N")); writer.Write(v.ContentHash);
            writer.Write(v.VersionNumber); writer.Write(v.OriginalFileName); writer.Write(v.UploadedByUserId.ToString("N"));
            writer.Write(v.SizeBytes); writer.Write(v.ContentType); writer.Write(v.ParsingPolicyKey); writer.Write(runtime);
            writer.Write(actor.UserId.ToString("N")); writer.Write(actor.TenantId?.ToString("N") ?? "host"); writer.Write(actor.SessionId.ToString("N"));
            writer.Write(actor.SessionKind); writer.Write(actor.SecurityStamp); writer.Write(actor.ActorScope); writer.Write(actor.EffectiveScope); writer.Flush();
            token.ThrowIfCancellationRequested(); return Convert.ToHexStringLower(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
        }
        finally { CryptographicOperations.ZeroMemory(bytes.GetBuffer()); }
    }
}
