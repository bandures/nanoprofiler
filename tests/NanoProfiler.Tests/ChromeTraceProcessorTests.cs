using System.IO;
using NanoProfiler;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Meowtron.NanoProfiler.Tests
{
    internal static class NanoChromeTraceProcessorTests
    {
        [Test]
        public static void ChromeTrace_CaptureStartStop_FileExists()
        {
            var testFileName = Path.GetTempFileName();
            
            var processor = new ChromeTraceProcessor(new ChromeTraceOptions() { FileName = testFileName });
            
            EmitStart(processor);
            EmitStop(processor);

            File.Exists(testFileName);
        }
    
        [Test]
        public static void ChromeTrace_CaptureMultipleMarker_ValidFile()
        {
            var testFileName = Path.GetTempFileName();
            
            var processor = new ChromeTraceProcessor(new ChromeTraceOptions() { FileName = testFileName });
            EmitStart(processor);

            EmitMarkerInfo(processor, new() { MarkerId = 12345, Name = "Test1" });
            EmitMarkerInfo(processor, new() { MarkerId = 56789, Name = "Test2" });
            
            // Nested markers
            EmitMarkerBegin(processor, new() { MarkerId = 12345, Timestamp = 0 });
            EmitMarkerBegin(processor, new() { MarkerId = 56789, Timestamp = 1000 });
            EmitMarkerEnd(processor, new() { MarkerId = 56789, Timestamp = 2000 });
            EmitMarkerEnd(processor, new() { MarkerId = 12345, Timestamp = 3000 });
            
            EmitStop(processor);
            File.Exists(testFileName);

            Assert.IsTrue(ValidateJsonHasMarker(testFileName, "Test1"));
        }
        
        [Test]
        public static void ChromeTrace_WithAutoScope_SendsSamples()
        {
            var markerName = "TEST";
            var fileName = Path.GetTempFileName();

            var testProcessor = new ChromeTraceProcessor(new ChromeTraceOptions() { FileName = fileName });
            var settings = new ProfilerSettingsBuilder()
                .AddProcessor(_ => testProcessor)
                .Build();
            
            // Generate marker begin/end 
            using (var profiler = new Profiler(settings))
            {
                var marker = profiler.CreateScope(markerName);
            
                profiler.Start();
                using (var _ = marker.Auto())
                {
                }
                profiler.Stop();
            }
            
            // Validate that result has the marker
            Assert.IsTrue(File.Exists(fileName));
            Assert.IsTrue(ValidateJsonHasMarker(fileName, markerName));
        }
        
        private static void EmitStart(ChromeTraceProcessor processor)
        {
            var msg = new Protocol.CaptureStart();
            processor.HandleMessage(0, ref msg);
        }
        
        private static void EmitStop(ChromeTraceProcessor processor)
        {
            var msg = new Protocol.CaptureStop();
            processor.HandleMessage(0, ref msg);
        }

        private static void EmitMarkerInfo(ChromeTraceProcessor processor, Protocol.RegisterMarker msg) =>
            processor.HandleMessage(0, ref msg);
        private static void EmitMarkerBegin(ChromeTraceProcessor processor, Protocol.ScopeBegin msg) =>
            processor.HandleMessage(0, ref msg);
        private static void EmitMarkerEnd(ChromeTraceProcessor processor, Protocol.ScopeEnd msg) => 
            processor.HandleMessage(0, ref msg);

        public static bool ValidateJsonHasMarker(string fileName, string markerName)
        {
            using var r = new StreamReader(fileName);
            
            var json = r.ReadToEnd();
            var rootNode = JsonConvert.DeserializeXNode(json, "traceEvents");
            if (rootNode == null)
                return false;

            var trace = rootNode.Element("traceEvents");
            foreach (var evt in trace.Elements())
            {
                if (evt.Element("name")?.Value == markerName)
                    return true;
            }

            return false;
        }
    }
}