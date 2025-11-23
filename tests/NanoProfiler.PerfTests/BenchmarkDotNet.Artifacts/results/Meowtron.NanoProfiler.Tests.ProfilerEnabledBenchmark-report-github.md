```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26100.6899)
Intel Core i5-14600KF, 1 CPU, 20 logical and 14 physical cores
.NET SDK 9.0.103
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2


```
| Method    | Mean     | Error   | StdDev  | Allocated |
|---------- |---------:|--------:|--------:|----------:|
| BeginEnd  | 259.0 ns | 1.91 ns | 1.69 ns |         - |
| AutoScope | 251.9 ns | 2.28 ns | 2.14 ns |         - |
