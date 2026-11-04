using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Errors = Full.NET.Modules.Document.Contracts.DocumentErrorCodes;

namespace Full.NET.UnitTests.Document;

/// <summary>实际字节预算与借用流所有权，不靠调用方或 HTTP 声明长度放行。</summary>
[TestClass]
public sealed class DocumentPreviewContentBudgetTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Oversize_actual_output_is_rejected_even_if_length_is_unknown_or_understated(bool understated)
    {
        using var source = new MemoryStream([1, 2, 3, 4]);
        var exception = await Assert.ThrowsExactlyAsync<DocumentPreviewContentLimitException>(() =>
            DocumentPreviewContentBudget.ReadOutputAsync(source, understated ? 1 : null, 3, default));
        Assert.AreEqual(Errors.OfficePreviewOutputTooLarge, exception.Code); Assert.IsTrue(source.CanRead);
        Assert.AreEqual(4L, source.Position);
    }

    [TestMethod]
    public async Task Declared_oversize_rejects_before_reading_source()
    {
        using var source = new MemoryStream([1]);
        await Assert.ThrowsExactlyAsync<DocumentPreviewContentLimitException>(() => DocumentPreviewContentBudget.ReadOutputAsync(source, 4, 3, default));
        Assert.AreEqual(0L, source.Position);
    }

    [TestMethod]
    public async Task Exact_boundary_returns_independent_output_and_preserves_input_owner()
    {
        using var source = new MemoryStream([1, 2, 3]);
        await using var output = await DocumentPreviewContentBudget.ReadOutputAsync(source, 3, 3, default);
        source.Dispose(); CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, output.ToArray());
        Assert.AreEqual(0L, output.Position); Assert.IsTrue(output.CanRead);
    }

    [TestMethod]
    public async Task Early_cancel_does_not_consume_or_dispose_borrowed_source()
    {
        using var source = new MemoryStream([1]); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => DocumentPreviewContentBudget.ReadOutputAsync(source, null, 3, cancellation.Token));
        Assert.AreEqual(0L, source.Position); Assert.IsTrue(source.CanRead);
    }

    [TestMethod]
    public async Task Late_cancel_from_source_cannot_return_successful_output()
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new LateCancelStream(cancellation);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => DocumentPreviewContentBudget.ReadOutputAsync(source, null, 3, cancellation.Token));
        Assert.IsTrue(source.CanRead);
    }

    [TestMethod]
    public async Task Empty_output_is_not_a_successful_preview()
    {
        using var source = new MemoryStream();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => DocumentPreviewContentBudget.ReadOutputAsync(source, 0, 3, default));
    }

    [TestMethod]
    public void Sync_reader_obeys_budget_and_wrapper_disposal_keeps_original_open()
    {
        using var source = new MemoryStream([1, 2, 3]);
        using (var input = new DocumentPreviewBudgetStream(source, 2, Errors.OfficePreviewInputTooLarge))
        {
            Assert.AreEqual(1, input.ReadByte()); Assert.AreEqual(2, input.ReadByte());
            Assert.AreEqual(Errors.OfficePreviewInputTooLarge, Assert.ThrowsExactly<DocumentPreviewContentLimitException>(() => input.ReadByte()).Code);
        }
        Assert.IsTrue(source.CanRead);
    }

    [TestMethod]
    public void Large_input_budget_does_not_overflow_counter_arithmetic()
    {
        using var source = new MemoryStream([1]); using var input = new DocumentPreviewBudgetStream(source, long.MaxValue, Errors.OfficePreviewInputTooLarge);
        Assert.AreEqual(1, input.ReadByte()); Assert.AreEqual(-1, input.ReadByte());
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(2147483648L)]
    public async Task Invalid_output_budget_rejects_before_allocation(long maximum)
    {
        using var source = new MemoryStream([1]);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => DocumentPreviewContentBudget.ReadOutputAsync(source, null, maximum, default));
        Assert.AreEqual(0L, source.Position);
    }

    private sealed class LateCancelStream(CancellationTokenSource cancellation) : MemoryStream([1])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            cancellation.Cancel(); return base.ReadAsync(buffer, CancellationToken.None);
        }
    }
}
