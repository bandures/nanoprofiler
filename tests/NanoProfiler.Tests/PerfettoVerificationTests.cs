#nullable disable
using System;
using System.Threading;
using NUnit.Framework;

namespace NanoProfiler.Tests
{
    public class PerfettoVerificationTests
    {
        [Test]
        public void Scope_WithStaticDeclaration_PresentInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .Build();

            using (var profiler = new Profiler(settings))
            {
                var scope = profiler.CreateScope("RootScope");
                
                profiler.Start();

                using (scope.Auto())
                {
                    TestHelper.FakeWork();
                }

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("Scope_WithStaticDeclaration_PresentInStream.py", tempFile.TracePath);
        }

        [Test]
        public void Scope_WithDynamicDeclaration_PresentInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .Build();
            
            using (var profiler = new Profiler(settings))
            {
                profiler.Start();

                for (var i = 0; i < 5; i++)
                {
                    var scope = profiler.CreateScope($"DynamicScope_{i}");
                    using (scope.Auto())
                    {
                        TestHelper.FakeWork();
                    }
                }

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("Scope_WithDynamicDeclaration_PresentInStream.py", tempFile.TracePath);
        }

        [Test]
        public void Group_WithStaticDeclaration_PresentInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .EnableGroups("MyGroup")
                .Build();
            
            using (var profiler = new Profiler(settings))
            {
                var group = profiler.CreateGroup("MyGroup");
                // Nb! Should be activity, scope can't be put into a group
                var groupActivity = group.CreateActivity("GroupActivity");

                profiler.Start();

                using (groupActivity.Auto())
                {
                    TestHelper.FakeWork();
                }

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("Group_WithStaticDeclaration_PresentInStream.py", tempFile.TracePath);
        }

        [Test]
        public void Group_WithDynamicDeclaration_PresentInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .EnableGroups("MyGroup")
                .Build();
            
            using (var profiler = new Profiler(settings))
            {
                profiler.Start();

                var group = profiler.CreateGroup("MyGroup");
                // Nb! Should be activity, scope can't be put into a group
                var groupActivity = group.CreateActivity("GroupActivity");

                using (groupActivity.Auto())
                {
                    TestHelper.FakeWork();
                }

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("Group_WithDynamicDeclaration_PresentInStream.py", tempFile.TracePath);
        }

        [Test]
        public void Counter_WithStaticDeclaration_PresentInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .Build();
            
            using (var profiler = new Profiler(settings))
            {
                var counter = profiler.CreateCounter("MyCounter");
                
                profiler.Start();

                counter.Sample(1.0f);
                TestHelper.FakeWork();
                counter.Sample(2.0f);
                TestHelper.FakeWork();
                counter.Sample(5.0f);

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("Counter_WithStaticDeclaration_PresentInStream.py", tempFile.TracePath);
        }

        [Test]
        public void Event_WithGroup_PresentInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .EnableGroups("MyGroup")
                .Build();

            using (var profiler = new Profiler(settings))
            {
                var group = profiler.CreateGroup("MyGroup");
                var groupEvent = group.CreateEvent("GroupEvent");

                profiler.Start();

                groupEvent.Record();
                TestHelper.FakeWork();
                groupEvent.Record(42, "state");

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("Event_WithGroup_PresentInStream.py", tempFile.TracePath);
        }

        [Test]
        public void Activity_WithStaticDeclaration_PresentInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .Build();
            
            using (var profiler = new Profiler(settings))
            {
                var activity = profiler.CreateActivity("MyActivity");
                
                profiler.Start();

                using (activity.Auto())
                {
                    TestHelper.FakeWork();
                }

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("Activity_WithStaticDeclaration_PresentInStream.py", tempFile.TracePath);
        }

        [Test]
        public void Activity_WithDynamicDeclaration_PresentInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .Build();
            
            using (var profiler = new Profiler(settings))
            {
                profiler.Start();

                var activity = profiler.CreateActivity("MyActivity");
                using (activity.Auto())
                {
                    TestHelper.FakeWork();
                }

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("Activity_WithDynamicDeclaration_PresentInStream.py", tempFile.TracePath);
        }

        [Test]
        public void MultiThread_WithStaticScope_PresentInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .Build();
            
            using (var profiler = new Profiler(settings))
            {
                var scope = profiler.CreateScope("RootScope");
                
                profiler.Start();

                var t = new Thread(() =>
                {
                    using (scope.Auto()) 
                    {
                        TestHelper.FakeWork();
                    }
                })
                {
                    Name = "WorkerThread"
                };

                t.Start();
                t.Join();

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("MultiThread_WithStaticScope_PresentInStream.py", tempFile.TracePath);
        }

        [Test]
        public void MultiThread_WithDynamicScope_PresentInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .Build();
            
            using (var profiler = new Profiler(settings))
            {
                profiler.Start();

                var t = new Thread(() =>
                {
                    var scope = profiler.CreateScope("RootScope");
                    using (scope.Auto()) 
                    {
                        TestHelper.FakeWork();
                    }
                    
                    profiler.Flush();
                })
                {
                    Name = "WorkerThread"
                };

                t.Start();
                t.Join();

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("MultiThread_WithDynamicScope_PresentInStream.py", tempFile.TracePath);
        }

        [Test]
        public void MultiThread_WithGroupScopeAndActivity_ValidStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .EnableGroups("MyGroup")
                .Build();
            
            using (var profiler = new Profiler(settings))
            {
                var threadScope = profiler.CreateScope("RootScope");
                var group = profiler.CreateGroup("MyGroup");
                var groupScope = group.CreateScope("MyGroupScope");
                var groupActivity = group.CreateActivity("MyActivity");
                
                profiler.Start();

                var t = new Thread(() =>
                {
                    using (threadScope.Auto()) 
                    {
                        TestHelper.FakeWork();
                    }
                    
                    using (groupScope.Auto()) 
                    {
                        TestHelper.FakeWork();
                    }
                    
                    using (groupActivity.Auto()) 
                    {
                        TestHelper.FakeWork();
                    }
                })
                {
                    Name = "WorkerThread"
                };

                t.Start();
                t.Join();

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("MultiThread_WithGroupScopeAndActivity_ValidStream.py", tempFile.TracePath);
        }

        [Test]
        public void Scope_Unclosed_ClosedInStream()
        {
            using var tempFile = new TestHelper.TempFile();
            var processor = TestHelper.SetupPerfettoProcessor(tempFile.TracePath);
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => processor)
                .Build();
            
            using (var profiler = new Profiler(settings))
            {
                var scope = profiler.CreateScope("UnclosedScope");
                
                profiler.Start();

                // Open scope but don't close it explicitly
                scope.Begin();
                
                TestHelper.FakeWork();

                profiler.Stop();
            }

            TestHelper.RunPythonVerification("Scope_Unclosed_ClosedInStream.py", tempFile.TracePath);
        }
    }
}
