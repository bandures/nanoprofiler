```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26100.6899)
Intel Core i5-14600KF, 1 CPU, 20 logical and 14 physical cores
.NET SDK 9.0.103
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2


```
| Method    | Mean      | Error     | StdDev    | Allocated |
|---------- |----------:|----------:|----------:|----------:|
| BeginEnd  | 12.658 ns | 0.0766 ns | 0.0716 ns |         - |
| AutoScope |  8.150 ns | 0.0358 ns | 0.0335 ns |         - |
