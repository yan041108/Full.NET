using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.SelfServiceProfile;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class SelfServiceProfileServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Get_rejects_non_host_actor_scope()
    {
        var service = CreateService(new TestQueryExecutor(), Substitute.For<ICommandExecutor>());

        var result = await service.GetAsync(UserId, "tenant", default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.SelfServiceProfileHostOnly, result.Error!.Code);
    }

    [TestMethod]
    public async Task Get_masks_phone_number_for_self_service()
    {
        var query = new TestQueryExecutor
        {
            User = CreateUser(),
            Profile = CreateProfile(phoneNumber: "+8613800138000"),
        };
        var service = CreateService(query, Substitute.For<ICommandExecutor>());

        var result = await service.GetAsync(UserId, "host", default);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("****8000", result.Value!.Profile!.PhoneNumber);
        CollectionAssert.Contains(result.Value.WritableFieldKeys.ToList(), "nickname");
        CollectionAssert.DoesNotContain(result.Value.WritableFieldKeys.ToList(), "phone_number");
    }

    [TestMethod]
    public async Task Get_returns_avatar_and_signature_file_ids()
    {
        var avatarId = Guid.CreateVersion7();
        var signatureId = Guid.CreateVersion7();
        var query = new TestQueryExecutor
        {
            User = CreateUser(),
            Profile = CreateProfile(avatarFileId: avatarId, signatureFileId: signatureId),
        };
        var service = CreateService(query, Substitute.For<ICommandExecutor>());

        var result = await service.GetAsync(UserId, "host", default);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(avatarId, result.Value!.AvatarFileId);
        Assert.AreEqual(signatureId, result.Value.SignatureFileId);
    }

    [TestMethod]
    public async Task Update_rejects_read_only_profile_field_keys()
    {
        var query = new TestQueryExecutor
        {
            User = CreateUser(),
            Profile = CreateProfile(),
        };
        var service = CreateService(query, Substitute.For<ICommandExecutor>());

        var result = await service.UpdateAsync(
            UserId,
            "host",
            new UpdateSelfServiceProfileRequest(
                DisplayName: null,
                UserVersion: null,
                Profile: new HostUserProfileWriteRequest(
                    FieldKeys: ["phone_number"],
                    Nickname: null,
                    PhoneNumber: "+8613800138001",
                    Email: null,
                    EmployeeNumber: null,
                    Gender: null,
                    JoinDateUtc: null,
                    SortOrder: null,
                    IdCardType: null,
                    IdCardNumber: null,
                    BirthDate: null,
                    Ethnicity: null,
                    Address: null,
                    GraduatedSchool: null,
                    EducationLevel: null,
                    PoliticalStatus: null,
                    OfficePhone: null,
                    EmergencyContact: null,
                    EmergencyContactRelation: null,
                    EmergencyContactPhone: null,
                    EmergencyContactAddress: null,
                    Remark: null,
                    Version: 0)),
            default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            IdentityErrorCodes.SelfServiceProfileReadOnlyFieldRejected,
            result.Error!.Code);
    }

    [TestMethod]
    public async Task Update_display_name_increments_user_version()
    {
        var user = CreateUser(version: 3);
        var query = new TestQueryExecutor
        {
            User = user,
        };
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                IdentitySql.UpdateSelfServiceDisplayName,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                user.DisplayName = "新的显示名";
                user.Version = 4;
                return 1;
            });
        var service = CreateService(query, command);

        var result = await service.UpdateAsync(
            UserId,
            "host",
            new UpdateSelfServiceProfileRequest(
                DisplayName: "新的显示名",
                UserVersion: 3,
                Profile: null),
            default);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("新的显示名", result.Value!.DisplayName);
        Assert.AreEqual(4, result.Value.UserVersion);
    }

    private static SelfServiceProfileService CreateService(
        IQueryExecutor queryExecutor,
        ICommandExecutor commandExecutor)
    {
        var projectionResolver = Substitute.For<IUserFieldProjectionResolver>();
        projectionResolver.ResolveAsync(
                UserId,
                null,
                FieldProjectionResourceKeys.HostUsers,
                Arg.Any<CancellationToken>())
            .Returns(new UserFieldProjection(
                FieldProjectionResourceKeys.HostUsers,
                [
                    "nickname",
                    "phone_number",
                    "email",
                    "gender",
                    "address",
                    "employee_number",
                ]));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.UtcDateTime);
        return new SelfServiceProfileService(
            queryExecutor,
            commandExecutor,
            projectionResolver,
            clock);
    }

    private static IdentityUserRecord CreateUser(int version = 1) => new()
    {
        Id = UserId,
        Username = "demo.user",
        DisplayName = "演示用户",
        AccountType = "normal_user",
        IsActive = true,
        Version = version,
    };

    private static HostUserProfileRecord CreateProfile(
        string? phoneNumber = null,
        Guid? avatarFileId = null,
        Guid? signatureFileId = null) => new()
    {
        UserId = UserId,
        Nickname = "昵称",
        PhoneNumber = phoneNumber,
        Email = "demo@example.com",
        SortOrder = 100,
        Version = 1,
        AvatarFileId = avatarFileId,
        SignatureFileId = signatureFileId,
    };

    private sealed class TestQueryExecutor : IQueryExecutor
    {
        public IdentityUserRecord? User { get; init; }
        public HostUserProfileRecord? Profile { get; init; }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement == IdentitySql.ListHostUserProfilesByIds)
            {
                IReadOnlyList<T> profiles = Profile is null
                    ? Array.Empty<T>()
                    : new[] { (T)(object)Profile };
                return Task.FromResult(profiles);
            }

            return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
        }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement == IdentitySql.FindHostUserById && User is not null)
            {
                return Task.FromResult((T?)(object)User);
            }

            return Task.FromResult<T?>(default);
        }

        public Task<T> QuerySingleAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> ExecuteScalarAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
