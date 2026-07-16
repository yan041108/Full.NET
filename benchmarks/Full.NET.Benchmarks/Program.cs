using BenchmarkDotNet.Running;
using Full.NET.Benchmarks;

BenchmarkSwitcher
    .FromTypes([typeof(SerializationBenchmarks)])
    .Run(args);
