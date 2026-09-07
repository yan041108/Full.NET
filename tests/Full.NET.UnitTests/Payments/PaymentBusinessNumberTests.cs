using System.Reflection;
using Full.NET.Modules.Payments.Features.ManageOrders;
using Full.NET.Modules.Payments.Features.ManageRefunds;

namespace Full.NET.UnitTests.Payments;

/// <summary>验证固定时钟和 UUID v7 下支付业务编号仍保持唯一且可重放。</summary>
[TestClass]
public sealed class PaymentBusinessNumberTests
{
    /// <summary>同一毫秒的不同业务标识不得生成相同渠道编号。</summary>
    /// <param name="refund">是否验证退款编号。</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Different_ids_at_the_same_instant_produce_distinct_numbers(bool refund)
    {
        var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 100; index++)
        {
            var id = Guid.CreateVersion7(now);
            var number = BuildNumber(refund, id, now);
            Assert.IsTrue(numbers.Add(number), "同一时刻的不同业务标识发生编号冲突。");
            Assert.IsTrue(number.Length <= 32);
            Assert.AreEqual(number, BuildNumber(refund, id, now));
        }
    }

    /// <summary>直接运行生产编号逻辑，避免在测试中复制算法造成假绿。</summary>
    /// <param name="refund">是否选择退款服务。</param>
    /// <param name="id">业务标识。</param>
    /// <param name="now">固定创建时间。</param>
    /// <returns>实际生产方法生成的业务编号。</returns>
    private static string BuildNumber(bool refund, Guid id, DateTimeOffset now)
    {
        var type = refund ? typeof(PaymentRefundManagementService) : typeof(PaymentOrderManagementService);
        var method = type.GetMethod(refund ? "BuildOutRefundNo" : "BuildOutTradeNo",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, [id, now])!;
    }
}
