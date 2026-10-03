namespace Full.NET.Hosting.Observability;

/// <summary>宿主日志快照的可选外部出口；只接收已脱敏、已限定大小的后台快照。</summary>
/// <remarks>宿主管道先停止并排空两个内存通道，再释放实现持有的外部发送资源。</remarks>
public interface IHostLogSnapshotExporter : IDisposable
{
    /// <summary>在后台消费线程上非阻塞提交快照；实现自行记录拒绝及投递终态。</summary>
    /// <param name="snapshot">不得包含受限详情的宿主快照。</param>
    void Emit(HostLogSnapshot snapshot);
}
