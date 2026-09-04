using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.UnitTests.Identity;

/// <summary>
/// 验证 Host 用户更新仅在数据库回滚死锁事务后重放完整事务单元。
/// </summary>
[TestClass]
public sealed class HostUserManagementDeadlockRetryTests
{
    /// <summary>
    /// 验证首次事务死锁后会重新执行整个事务，并返回下一次执行的业务结果。
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_retries_complete_transaction_after_deadlock()
    {
        var transaction = Substitute.For<ICommandTransaction>();
        var deadlock = new DataCommandException(
            DataCommandFailureKind.Deadlock,
            new InvalidOperationException("deadlock victim"));
        var expected = Result<HostUserResponse>.Failure(new Error(
            IdentityErrorCodes.UserEmailExists,
            "Email is already assigned to another host user.",
            ErrorType.Conflict));
        transaction.ExecuteResultAsync<HostUserResponse>(
                Arg.Any<Func<CancellationToken, Task<Result<HostUserResponse>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<Result<HostUserResponse>>(deadlock),
                Task.FromResult(expected));
        var service = CreateService(transaction);

        var result = await service.UpdateAsync(
            Guid.CreateVersion7(),
            new UpdateHostUserRequest("并发更新", 1),
            allowedProfileFieldKeys: null,
            CancellationToken.None);

        Assert.AreSame(expected, result);
        _ = transaction.Received(2).ExecuteResultAsync<HostUserResponse>(
            Arg.Any<Func<CancellationToken, Task<Result<HostUserResponse>>>>(),
            CancellationToken.None);
    }

    /// <summary>
    /// 创建只执行事务外层行为所需的 Host 用户管理服务。
    /// </summary>
    /// <param name="transaction">用于验证完整事务重放次数的事务替身。</param>
    /// <returns>Host 用户管理服务。</returns>
    private static HostUserManagementService CreateService(ICommandTransaction transaction) =>
        new(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            transaction,
            new StubPasswordHasher(),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

    /// <summary>
    /// 提供当前测试不会进入的密码哈希依赖，避免为内部领域类型生成动态代理。
    /// </summary>
    private sealed class StubPasswordHasher : IPasswordHasher<IdentityUser>
    {
        /// <summary>
        /// 当前测试不应执行密码哈希。
        /// </summary>
        /// <param name="user">领域用户。</param>
        /// <param name="password">明文密码。</param>
        /// <returns>该路径不返回结果。</returns>
        public string HashPassword(IdentityUser user, string password) =>
            throw new NotSupportedException();

        /// <summary>
        /// 当前测试不应执行密码校验。
        /// </summary>
        /// <param name="user">领域用户。</param>
        /// <param name="hashedPassword">已哈希密码。</param>
        /// <param name="providedPassword">待校验的明文密码。</param>
        /// <returns>该路径不返回结果。</returns>
        public PasswordVerificationResult VerifyHashedPassword(
            IdentityUser user,
            string hashedPassword,
            string providedPassword) =>
            throw new NotSupportedException();
    }
}
