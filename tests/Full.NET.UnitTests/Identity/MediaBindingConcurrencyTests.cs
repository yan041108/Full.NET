using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.SelfServiceProfile;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.TenantBranding;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>旧媒体快照不能清除或覆盖并发赢家；失败确认不能阻止提交后的缓存失效。</summary>
[TestClass]
public sealed class MediaBindingConcurrencyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Identity_delete_rejects_a_changed_profile(bool signature)
    {
        var f = new Fixture(signature) { CurrentVersion = 5 };
        var result = await f.DeleteIdentityAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.ProfileVersionConflict, result.Error!.Code);
        Assert.AreEqual(f.OldFile, f.CurrentFile);
        Assert.AreEqual(0, f.Claims.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Identity_delete_rejects_a_different_observed_file(bool signature)
    {
        var f = new Fixture(signature); var winner = Guid.CreateVersion7(); f.CurrentFile = winner;
        var result = await f.DeleteIdentityAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(winner, f.CurrentFile);
        Assert.AreEqual(0, f.Claims.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Identity_upload_rejects_a_changed_profile_without_confirming(bool signature)
    {
        var f = new Fixture(signature) { CurrentVersion = 5 };
        var result = await f.UploadIdentityAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.ProfileVersionConflict, result.Error!.Code);
        Assert.AreEqual(f.OldFile, f.CurrentFile);
        await f.Claims.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await f.Claims.Received(1).ReleaseAsync(f.NewClaimKey, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Identity_missing_profile_does_not_overwrite_a_concurrently_created_shell(bool signature)
    {
        var f = new Fixture(signature) { MissingProfileSnapshot = true };
        var result = await f.UploadIdentityAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.ProfileVersionConflict, result.Error!.Code);
        Assert.AreEqual(f.OldFile, f.CurrentFile);
        await f.Claims.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await f.Claims.Received(1).ReleaseAsync(f.NewClaimKey, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Identity_first_media_binding_creates_a_profile_shell(bool signature)
    {
        var f = new Fixture(signature) { MissingProfileSnapshot = true, RowExists = false, CurrentFile = null };
        var result = await f.UploadIdentityAsync();
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(f.NewFile, f.CurrentFile);
        Assert.AreEqual(1, f.CurrentVersion);
        await f.Claims.Received(1).ConfirmAsync(f.NewClaimKey, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Identity_delete_matching_snapshot_releases_only_the_observed_claim(bool signature)
    {
        var f = new Fixture(signature);
        var result = await f.DeleteIdentityAsync();
        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(f.CurrentFile);
        await f.Claims.Received(1).ReleaseAsync(f.OldClaimKey, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Identity_upload_matching_version_confirms_and_releases_the_old_claim(bool signature)
    {
        var f = new Fixture(signature);
        Assert.IsTrue((await f.UploadIdentityAsync()).IsSuccess);
        Assert.AreEqual(f.NewFile, f.CurrentFile);
        await f.Claims.Received(1).ConfirmAsync(f.NewClaimKey, Arg.Any<CancellationToken>());
        await f.Claims.Received(1).ReleaseAsync(f.OldClaimKey, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_logo_delete_rejects_a_changed_version_in_both_scopes(bool current)
    {
        var f = new Fixture(false) { CurrentVersion = 5 };
        var result = await f.DeleteLogoAsync(current);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.VersionConflict, result.Error!.Code);
        Assert.AreEqual(f.OldFile, f.CurrentFile);
        Assert.AreEqual(0, f.Claims.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Cache.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_logo_delete_rejects_a_different_observed_file(bool current)
    {
        var f = new Fixture(false); var winner = Guid.CreateVersion7(); f.CurrentFile = winner;
        var result = await f.DeleteLogoAsync(current);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(winner, f.CurrentFile);
        Assert.AreEqual(0, f.Claims.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_logo_committed_binding_invalidates_cache_even_when_confirmation_fails(bool current)
    {
        var f = new Fixture(false);
        f.Claims.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            Result<HostFileReferenceClaimResult>.Failure(new Error("test.confirmation_unavailable", "Unavailable", ErrorType.Conflict)));
        using var stream = new MemoryStream([1]);
        var result = current
            ? await f.TenantMedia.UploadLogoCurrentAsync(f.Subject, f.Subject, "image.png", "image/png", stream, 1)
            : await f.TenantMedia.UploadLogoByTenantIdAsync(f.Subject, f.Subject, "image.png", "image/png", stream, 1);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(f.NewFile, f.CurrentFile);
        Assert.IsTrue(f.Cache.ReceivedCalls().Any(), "已提交品牌数据仍须执行既有缓存失效边界。");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_logo_delete_matching_snapshot_releases_the_claim_and_invalidates_cache(bool current)
    {
        var f = new Fixture(false);
        var result = await f.DeleteLogoAsync(current);
        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(f.CurrentFile);
        await f.Claims.Received(1).ReleaseAsync(HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(f.Subject, f.OldFile), Arg.Any<CancellationToken>());
        Assert.IsTrue(f.Cache.ReceivedCalls().Any());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_logo_confirmation_exception_does_not_prevent_committed_cache_invalidation(bool current)
    {
        var f = new Fixture(false);
        f.Claims.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<HostFileReferenceClaimResult>>(new IOException("Confirmation unavailable")));
        await Assert.ThrowsExactlyAsync<IOException>(() => f.UploadLogoAsync(current));
        Assert.AreEqual(f.NewFile, f.CurrentFile);
        Assert.IsTrue(f.Cache.ReceivedCalls().Any());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_logo_release_exception_does_not_prevent_committed_cache_invalidation(bool current)
    {
        var f = new Fixture(false);
        f.Claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<bool>>(new IOException("Release unavailable")));
        await Assert.ThrowsExactlyAsync<IOException>(() => f.DeleteLogoAsync(current));
        Assert.IsNull(f.CurrentFile);
        Assert.IsTrue(f.Cache.ReceivedCalls().Any());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_logo_request_cancelled_after_commit_still_invalidates_cache(bool delete)
    {
        var f = new Fixture(false); using var cancellation = new CancellationTokenSource();
        f.AfterCommit = cancellation.Cancel;
        f.Claims.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<CancellationToken>().ThrowIfCancellationRequested();
            return Result<HostFileReferenceClaimResult>.Failure(new Error("test.unexpected", "Unexpected", ErrorType.Conflict));
        });
        f.Claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<CancellationToken>().ThrowIfCancellationRequested(); return Result<bool>.Success(true);
        });
        if (delete) await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.TenantMedia.DeleteLogoByTenantIdAsync(f.Subject, cancellation.Token));
        else
        {
            using var content = new MemoryStream([1]);
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.TenantMedia.UploadLogoByTenantIdAsync(f.Subject, f.Subject, "image.png", "image/png", content, 1, cancellation.Token));
        }
        Assert.IsTrue(f.Cache.ReceivedCalls().Any());
    }

    private sealed class Fixture : IQueryExecutor, ICommandExecutor, ICommandTransaction
    {
        public readonly Guid Subject = Guid.CreateVersion7(), OldFile = Guid.CreateVersion7(), NewFile = Guid.CreateVersion7();
        public Guid? CurrentFile { get; set; }
        public int CurrentVersion { get; set; } = 4;
        public bool MissingProfileSnapshot { get; init; }
        public bool RowExists { get; set; } = true;
        public Action? AfterCommit { get; set; }
        private readonly bool _signature;
        private int _profileReads, _brandingReads;
        public readonly IHostFileReferenceClaimService Claims = Substitute.For<IHostFileReferenceClaimService>();
        public readonly ICacheInvalidator Cache = Substitute.For<ICacheInvalidator>();
        public SelfServiceProfileMediaService IdentityMedia { get; }
        public TenantBrandingMediaService TenantMedia { get; }
        public string NewClaimKey => _signature ? HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(Subject, NewFile)
            : HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(Subject, NewFile);
        public string OldClaimKey => _signature ? HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(Subject, OldFile)
            : HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(Subject, OldFile);

        public Fixture(bool signature)
        {
            _signature = signature; CurrentFile = OldFile;
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var writer = Substitute.For<ICurrentTenantContextWriter>();
            var projection = Substitute.For<IUserFieldProjectionResolver>();
            projection.ResolveAsync(Arg.Any<Guid>(), null, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new UserFieldProjection(FieldProjectionResourceKeys.HostUsers, ["nickname"]));
            var profile = new SelfServiceProfileService(this, this, projection, clock, writer);
            var upload = Substitute.For<IHostFileUploadWriter>();
            upload.UploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(Result<HostFileUploadResult>.Success(new(NewFile, 1, null)));
            var descriptor = Substitute.For<IHostFileDescriptorReader>();
            descriptor.GetReadyDescriptorAsync(NewFile, Arg.Any<CancellationToken>()).Returns(new HostFileDescriptor(NewFile, "image.png", "image/png", 1, null, Subject));
            var claim = Result<HostFileReferenceClaimResult>.Success(new(Guid.CreateVersion7(), "pending", new(NewFile, 1, null)));
            Claims.ClaimAsync(Arg.Any<HostFileReferenceClaimRequest>(), Arg.Any<CancellationToken>()).Returns(claim);
            Claims.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(claim);
            Claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            IdentityMedia = new(this, this, this, profile, writer, upload, Claims, descriptor);
            var environment = Substitute.For<IHostEnvironment>(); environment.EnvironmentName.Returns("Test");
            var invalidator = new TenantCacheInvalidator(Cache, environment, CachePolicyRegistry.Create(new CacheOptions()), NullLogger<TenantCacheInvalidator>.Instance);
            var tenant = Substitute.For<ICurrentTenant>(); tenant.Id.Returns(Subject); tenant.IsAvailable.Returns(true);
            var branding = new TenantBrandingService(this, this, this, clock, tenant, invalidator);
            TenantMedia = new(this, this, this, clock, writer, branding, invalidator, upload, Claims, descriptor);
            Claims.ClearReceivedCalls(); Cache.ClearReceivedCalls();
        }

        public Task<Result<SelfServiceProfileResponse>> DeleteIdentityAsync() => _signature
            ? IdentityMedia.DeleteSignatureAsync(Subject, "host") : IdentityMedia.DeleteAvatarAsync(Subject, "host");
        public async Task<Result<SelfServiceProfileResponse>> UploadIdentityAsync()
        {
            using var content = new MemoryStream([1]);
            return _signature ? await IdentityMedia.UploadSignatureAsync(Subject, "host", "image.png", "image/png", content, 1)
                : await IdentityMedia.UploadAvatarAsync(Subject, "host", "image.png", "image/png", content, 1);
        }
        public Task<Result<TenantBrandingResponse>> DeleteLogoAsync(bool current) => current
            ? TenantMedia.DeleteLogoCurrentAsync(Subject) : TenantMedia.DeleteLogoByTenantIdAsync(Subject);
        public async Task<Result<TenantBrandingResponse>> UploadLogoAsync(bool current)
        {
            using var content = new MemoryStream([1]);
            return current ? await TenantMedia.UploadLogoCurrentAsync(Subject, Subject, "image.png", "image/png", content, 1)
                : await TenantMedia.UploadLogoByTenantIdAsync(Subject, Subject, "image.png", "image/png", content, 1);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (statement == IdentitySql.ListHostUserProfilesByIds)
            {
                var snapshot = _profileReads++ == 0;
                if (snapshot && MissingProfileSnapshot) return Task.FromResult<IReadOnlyList<T>>([]);
                var profile = new HostUserProfileRecord { UserId = Subject, Version = snapshot ? 4 : CurrentVersion,
                    AvatarFileId = _signature ? null : snapshot ? OldFile : CurrentFile,
                    SignatureFileId = _signature ? snapshot ? OldFile : CurrentFile : null, SortOrder = 100 };
                return Task.FromResult<IReadOnlyList<T>>([(T)(object)profile]);
            }
            return Task.FromResult<IReadOnlyList<T>>([]);
        }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object? value = statement == IdentitySql.FindHostUserById
                ? new IdentityUserRecord { Id = Subject, Username = "media", DisplayName = "媒体", AccountType = "normal_user", IsActive = true, Version = 1 }
                : statement == TenantSql.FindById ? new TenantResolutionRecord(Subject, "media", "媒体", "media.test", true, CurrentVersion, "zh-CN") : null;
            if (statement == TenantSql.FindTenantBrandingById || statement == TenantSql.FindTenantBrandingCurrent)
            {
                var snapshot = _brandingReads++ == 0;
                value = new TenantBrandingRecord(Subject, "媒体", snapshot ? OldFile : CurrentFile, null, null, null, null, snapshot ? 4 : CurrentVersion);
            }
            return Task.FromResult(value is null ? default : (T?)value);
        }
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (statement == IdentitySql.InsertHostUserProfileMediaShell)
            {
                if (RowExists) return Task.FromException<int>(new DataCommandException(DataCommandFailureKind.UniqueConstraint, new InvalidOperationException("Concurrent profile shell")));
                RowExists = true; CurrentFile = NewFile; CurrentVersion = 1; return Task.FromResult(1);
            }
            if (!RowExists) return Task.FromResult(0);
            var p = (Dictionary<string, object?>)parameters!;
            // 替身只模拟当前 SQL 的条件更新；最终由真实双库验证并发与参数行为。
            if (statement.Text.Contains("Version = @Version", StringComparison.Ordinal) && (!p.TryGetValue("Version", out var version) || !Equals(version, CurrentVersion)))
                return Task.FromResult(0);
            if (statement.Text.Contains("= @FileId", StringComparison.Ordinal) && (!p.TryGetValue("FileId", out var file) || !Equals(file, CurrentFile)))
                return Task.FromResult(0);
            var clear = statement == IdentitySql.ClearHostUserProfileAvatar || statement == IdentitySql.ClearHostUserProfileSignature
                || statement == TenantSql.ClearTenantLogo || statement == TenantSql.ClearTenantLogoCurrent;
            CurrentFile = clear ? null : NewFile; CurrentVersion++;
            if (clear) AfterCommit?.Invoke();
            return Task.FromResult(1);
        }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            var result = await action(cancellationToken);
            if (result is Result<bool> { IsSuccess: true }) AfterCommit?.Invoke();
            return result;
        }
    }
}
