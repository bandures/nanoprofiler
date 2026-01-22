using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace NanoProfiler.Tests
{
    public static class TestHelper
    {
        public class TempFile : IDisposable
        {
            public TempFile()
            {
                TracePath = Path.GetTempFileName() + ".pftrace";
            }
            
            public void Dispose()
            {
                if (!File.Exists(TracePath))
                    return;
                File.Delete(TracePath);
            }
            
            public string TracePath { get; }
        }

        // Perfetto collapses markers with exactly the same timestamps, so we need to ensure some time passes
        public static void FakeWork() => Thread.Sleep(10);
        
        public static string GetProjectDir()
        {
            var projectDir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (projectDir != null && projectDir.Name != "NanoProfiler.Tests")
            {
                projectDir = projectDir.Parent;
            }

            if (projectDir == null)
                throw new Exception($"Failed to locate project directory from {TestContext.CurrentContext.TestDirectory}");

            return projectDir.FullName;
        }

        public static PerfettoProcessor SetupPerfettoProcessor(string tracePath)
        {
            var process = Process.GetCurrentProcess();
            var processor = new PerfettoProcessor(new PerfettoProcessorSettings 
            { 
                FileName = tracePath,
                ProcessName = "PrimitivesTestProc",
                ProcessId = process.Id,
                CountersSamplingRate = TimeSpan.Zero
            });

            return processor;
        }
        
        public static void RunPythonVerification(string scriptName, string tracePath)
        {
            var projectDir = GetProjectDir();
            var pythonScriptsDir = Path.Combine(projectDir, "Python");
            var scriptPath = Path.Combine(pythonScriptsDir, scriptName);
            if (!File.Exists(scriptPath))
            {
                Assert.Fail($"Python script not found: {scriptPath}");
                return;
            }

            System.Threading.Thread.Sleep(200);

            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" \"{tracePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            Assert.IsNotNull(process, "Failed to start Python process");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            
            process.WaitForExit();

            Console.WriteLine($"--- Python Output ({scriptName}) ---");
            Console.WriteLine(output);
            
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"--- Python Error ({scriptName}) ---");
                Console.WriteLine(error);
            }

            Assert.AreEqual(0, process.ExitCode, $"Python verification script {scriptName} failed.");
        }
    }
}
