using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Full.NET.UnitTests")]
// 基准直接复用真实双通道与文件维护实现，避免复制一套算法产生失真的对照。
[assembly: InternalsVisibleTo("Full.NET.Benchmarks")]
