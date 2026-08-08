using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Security.Claims;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserFieldProjectionTests
{
    private static readonly Guid ActorUserId = Guid.CreateVersion7();
    private static readonly Guid TargetUserId = Guid.CreateVersion7();

    [TestMethod]
    public async Task Restricted_columns_are_not_queried_without_effective_grants()
    {
        var query = new RecordingQueryExecutor();
        var service = CreateService(query, MandatoryProjection());

        var result = await service.ListAsync(ActorUserId, 1, 20, includeProfile: false);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserPreferredLocales));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserFailedLoginCounts));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserLockoutEnds));
        Assert.IsNull(result.Value!.Items.Single().ProjectedFields!.PreferredLocale);
    }

    [TestMethod]
    public async Task Only_the_explicitly_effective_restricted_column_is_queried()
    {
        var query = new RecordingQueryExecutor();
        var projection = MandatoryProjection() with
        {
            FieldKeys = [.. MandatoryProjection().FieldKeys, "preferred_locale"],
        };
        var service = CreateService(query, projection);

        var result = await service.GetByIdAsync(
            ActorUserId,
            TargetUserId,
            includeProfile: false);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("zh-CN", result.Value!.ProjectedFields!.PreferredLocale);
        CollectionAssert.Contains(
            result.Value.ProjectedFields.EffectiveFieldKeys.ToArray(),
            "preferred_locale");
        Assert.IsTrue(query.Statements.Contains(IdentitySql.ListHostUserPreferredLocales));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserFailedLoginCounts));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserLockoutEnds));
    }

    [TestMethod]
    public async Task Export_uses_the_same_effective_projection_as_list_and_detail()
    {
        var query = new RecordingQueryExecutor();
        var projection = MandatoryProjection() with
        {
            FieldKeys = [.. MandatoryProjection().FieldKeys, "failed_login_count"],
        };
        var service = CreateService(query, projection);

        var result = await service.ExportAsync(ActorUserId, includeProfile: false);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(query.Statements.Contains(IdentitySql.ListHostUserFailedLoginCounts));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserPreferredLocales));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserLockoutEnds));
        CollectionAssert.Contains(
            result.Value!.Single().ProjectedFields!.EffectiveFieldKeys.ToArray(),
            "failed_login_count");
    }

    [TestMethod]
    public async Task Profile_query_is_skipped_without_effective_profile_field_grants()
    {
        var query = new RecordingQueryExecutor();
        var service = CreateService(query, MandatoryProjection());

        var result = await service.GetByIdAsync(
            ActorUserId,
            TargetUserId,
            includeProfile: true);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Value!.Profile);
        Assert.IsFalse(query.Statements.Any(statement =>
            statement.Name == "identity.list_host_user_profiles_by_ids.projected"));
    }

    [TestMethod]
    public async Task Profile_query_selects_only_granted_columns()
    {
        var query = new RecordingQueryExecutor();
        var projection = MandatoryProjection() with
        {
            FieldKeys = [.. MandatoryProjection().FieldKeys, "phone_number", "remark"],
        };
        var service = CreateService(query, projection);

        var result = await service.GetByIdAsync(
            ActorUserId,
            TargetUserId,
            includeProfile: true);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("13800000000", result.Value!.Profile!.PhoneNumber);
        Assert.AreEqual("投影备注", result.Value.Profile.Remark);
        Assert.IsNull(result.Value.Profile.IdCardNumber);
        var profileStatement = query.Statements.Single(statement =>
            statement.Name == "identity.list_host_user_profiles_by_ids.projected");
        StringAssert.Contains(profileStatement.Text, "PhoneNumber");
        StringAssert.Contains(profileStatement.Text, "Remark");
        Assert.IsFalse(profileStatement.Text.Contains("IdCardNumber", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Profile_query_respects_endpoint_gate()
    {
        var query = new RecordingQueryExecutor();
        var projection = MandatoryProjection() with
        {
            FieldKeys = [.. MandatoryProjection().FieldKeys, "phone_number"],
        };
        var service = CreateService(query, projection);

        var result = await service.GetByIdAsync(
            ActorUserId,
            TargetUserId,
            includeProfile: false);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Value!.Profile);
        Assert.IsFalse(query.Statements.Any(statement =>
            statement.Name == "identity.list_host_user_profiles_by_ids.projected"));
    }

    [TestMethod]
    public async Task Resolve_allowed_profile_field_keys_requires_requested_fields_to_be_granted()
    {
        var resolver = Substitute.For<IUserFieldProjectionResolver>();
        resolver.ResolveAsync(
                ActorUserId,
                null,
                FieldProjectionResourceKeys.HostUsers,
                Arg.Any<CancellationToken>())
            .Returns(new UserFieldProjection(
                FieldProjectionResourceKeys.HostUsers,
                [.. MandatoryProjection().FieldKeys, "phone_number", "remark"]));

        var allowed = await Endpoint.ResolveAllowedProfileFieldKeysAsync(
            ActorUserId,
            new HostUserProfileWriteRequest(
                FieldKeys: ["phone_number", "remark"],
                Nickname: null,
                PhoneNumber: "13800000000",
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
                Remark: "允许",
                Version: null),
            resolver,
            default);

        CollectionAssert.AreEqual(new[] { "phone_number", "remark" }, allowed);

        var denied = await Endpoint.ResolveAllowedProfileFieldKeysAsync(
            ActorUserId,
            new HostUserProfileWriteRequest(
                FieldKeys: ["phone_number", "id_card_number"],
                Nickname: null,
                PhoneNumber: "13800000000",
                Email: null,
                EmployeeNumber: null,
                Gender: null,
                JoinDateUtc: null,
                SortOrder: null,
                IdCardType: null,
                IdCardNumber: "123456",
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
                Version: null),
            resolver,
            default);

        Assert.AreEqual(0, denied.Length);
    }

    private static HostUserQueryService CreateService(
        IQueryExecutor query,
        UserFieldProjection projection)
    {
        var resolver = Substitute.For<IUserFieldProjectionResolver>();
        resolver.ResolveAsync(
                ActorUserId,
                null,
                FieldProjectionResourceKeys.HostUsers,
                Arg.Any<CancellationToken>())
            .Returns(projection);
        return new HostUserQueryService(
            query,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            resolver);
    }

    private static UserFieldProjection MandatoryProjection() =>
        new(
            FieldProjectionResourceKeys.HostUsers,
            [
                "created_at_utc",
                "display_name",
                "id",
                "is_active",
                "updated_at_utc",
                "username",
                "version",
            ]);

    private sealed class RecordingQueryExecutor : IQueryExecutor
    {
        public List<SqlStatement> Statements { get; } = [];

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            object? value = statement == IdentitySql.CountHostUsers
                ? 1L
                : statement == IdentitySql.FindHostUserProjectionBaseById
                    ? CreateRow()
                    : null;
            return Task.FromResult((T?)value);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            object[] values = statement == IdentitySql.ListHostUsersSqlServer
                ? [CreateRow()]
                : statement == IdentitySql.ListHostUserPreferredLocales
                    ? [new HostUserPreferredLocaleRow { Id = TargetUserId, Value = "zh-CN" }]
                    : statement == IdentitySql.ListHostUserFailedLoginCounts
                        ? [new HostUserFailedLoginCountRow { Id = TargetUserId, Value = 2 }]
                    : statement.Name == "identity.list_host_user_profiles_by_ids.projected"
                        ? [new HostUserProfileRecord
                        {
                            UserId = TargetUserId,
                            PhoneNumber = "13800000000",
                            Remark = "投影备注",
                            IdCardNumber = "440101",
                            SortOrder = 7,
                            Version = 3,
                        }]
                    : [];
            return Task.FromResult<IReadOnlyList<T>>(values.Cast<T>().ToArray());
        }

        private static HostUserListRow CreateRow() => new()
        {
            Id = TargetUserId,
            Username = "projection-user",
            DisplayName = "Projection User",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            Version = 1,
        };
    }
}
