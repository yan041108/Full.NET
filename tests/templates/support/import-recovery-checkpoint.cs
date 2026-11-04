using Full.NET.Data.Abstractions;
using System.Text.Json;

/// <summary>仅编译到验收应用中，装饰正式执行器，在指定任务的检查点边界建立可观测屏障。</summary>
internal sealed class ImportRecoveryCheckpointProbe(ICommandExecutor inner, string directory, string mode, Guid taskId) : ICommandExecutor
{
    /// <summary>所有 SQL 仍交给正式 Dapper 执行器；不读写其它模块表或制造行结果。</summary>
    public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
    {
        if (statement.Name != "import_export.task.update_execution_progress"
            || parameters is not IReadOnlyDictionary<string, object?> values
            || values["Id"] is not Guid id || id != taskId)
            return await inner.ExecuteAsync(statement, parameters, cancellationToken);

        var next = Convert.ToInt32(values["NextLineNumber"]);
        var receiptId = values["ErrorReceiptFileId"] as Guid?;
        if (mode == "receipt" && next == 3)
        {
            if (receiptId is null) throw new InvalidOperationException("验收要求真实回执先上传完成。");
            Write(next, receiptId);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        var affected = await inner.ExecuteAsync(statement, parameters, cancellationToken);
        if (affected > 0 && mode == "checkpoint" && next == 1)
        {
            Write(next, receiptId);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        if (affected > 0 && (mode is "observe" or "retry") && next == 3) Write(next, receiptId);
        return affected;
    }

    /// <summary>原子写入不含凭据的进程证据，避免强制退出时读取半份记录。</summary>
    private void Write(int next, Guid? receiptId)
    {
        var file = Path.Combine(directory, mode + ".json");
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { pid = Environment.ProcessId, taskId, next, receiptId }));
        File.Move(temporary, file, true);
    }
}
