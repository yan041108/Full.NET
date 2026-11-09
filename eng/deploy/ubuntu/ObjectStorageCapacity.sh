# SeaweedFS 4.52 单副本默认每次增长七卷，业务桶与 Filer 元数据日志分别需要卷。
# 固定该部署预设的槽位、卷大小与磁盘保留量，安装、升级、隔离恢复共用。
object_storage_capacity=(-volume.max=32 -master.volumeSizeLimitMB=1024 -volume.minFreeSpace=4GiB)
