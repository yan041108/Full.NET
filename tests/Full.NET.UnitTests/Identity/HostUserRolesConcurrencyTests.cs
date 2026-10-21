using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Features.ManageHostRoles;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>用户侧赋权必须推进角色编辑版本，阻止旧候选页覆盖另一入口的授权。</summary>
[TestClass]
public sealed class HostUserRolesConcurrencyTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task User_assignment_invalidates_old_role_candidate_version(DatabaseProvider provider)
    {
        var userId=Guid.CreateVersion7(); var roleId=Guid.CreateVersion7(); var version=3;
        var queries=Substitute.For<IQueryExecutor>(); var commands=Substitute.For<ICommandExecutor>();
        var clock=Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids=Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var user=new IdentityUserRecord { Id=userId, ScopeKey="host", IsActive=true, Version=2 };
        IdentityRoleRecord Role() => new(roleId,null,"host","auditor","Auditor",false,true,false,
            RoleDataScopeKinds.All,clock.UtcNow,null,version);
        queries.QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindHostUserById,Arg.Any<object>(),Arg.Any<CancellationToken>()).Returns(user);
        queries.QuerySingleOrDefaultAsync<IdentityRoleRecord>(IdentitySql.FindHostRoleById,Arg.Any<object>(),Arg.Any<CancellationToken>()).Returns(_ => Role());
        queries.QueryAsync<Guid>(IdentitySql.GetUserAssignableRoleIds,Arg.Any<object>(),Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        queries.QueryAsync<IdentityRoleRecord>(Arg.Any<SqlStatement>(),Arg.Any<object>(),Arg.Any<CancellationToken>()).Returns(_ => new[] { Role() });
        commands.ExecuteAsync(Arg.Any<SqlStatement>(),Arg.Any<object>(),Arg.Any<CancellationToken>()).Returns(call => {
            var statement=call.Arg<SqlStatement>()!;
            if (statement.Name.StartsWith("identity.user_roles.touch_roles",StringComparison.Ordinal)) ++version;
            if (statement == HostRoleMembersSql.UpdateRoleVersion) {
                var values=(IReadOnlyDictionary<string,object?>)call.Arg<object>()!;
                if ((int)values["Version"]! != version) return 0;
                ++version;
            }
            return 1;
        });
        var transaction=new Transaction(); var options=Options.Create(new DatabaseOptions { Provider=provider });
        var users=new HostUserRolesService(queries,commands,transaction,clock,ids,options);
        Assert.IsTrue((await users.ReplaceAsync(userId,new ReplaceHostUserRolesRequest([roleId],2))).IsSuccess);
        Assert.AreEqual(4,version);
        var roles=new HostRoleMembersService(queries,commands,transaction,clock,ids,options);
        var stale=await roles.ReplaceAsync(roleId,new ReplaceHostRoleMembersRequest([],3) { CandidateUserIds=[userId] });
        Assert.IsFalse(stale.IsSuccess);
        await commands.DidNotReceive().ExecuteAsync(IdentitySql.DeleteHostRoleMemberAssignments,Arg.Any<object>(),Arg.Any<CancellationToken>());
    }
    private sealed class Transaction : ICommandTransaction {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken,Task<T>> action,CancellationToken cancellationToken) => action(cancellationToken);
    }
}
