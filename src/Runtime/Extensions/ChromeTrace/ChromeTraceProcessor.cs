using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;

namespace NanoProfiler
{
    public class ChromeTraceOptions
    {
        public string FileName;
        public Dictionary<string, string> HeaderMetadata;
    }
    
    /// <summary>
    /// ChromeTrace is responsible for json serialization of TraceEvent data
    /// ChromeTrace format:
    /// https://docs.google.com/document/d/1CvAClvFfyA5R-PhYUmn5OOQtYMH4h6I0nSsKchNAySU
    /// </summary>
    public class ChromeTraceProcessor : 
        IProcessor<Protocol.CaptureStart>, 
        IProcessor<Protocol.CaptureStop>, 
        IProcessor<Protocol.RegisterMarker>, 
        IProcessor<Protocol.ScopeBegin>, 
        IProcessor<Protocol.ScopeEnd>,
        IProcessor<Protocol.ActivityBegin>,
        IProcessor<Protocol.ActivityEnd>,
        IProcessor<Protocol.Event>
    {
        private static readonly Encoding UTF8NoBom = new UTF8Encoding(false);
        private static readonly char[] WriteEscapedJsonBuffer = new char[4096];

        private readonly string _fileName;
        private readonly Dictionary<uint, string> _markers;
        private readonly Dictionary<int, string> _generatedIds;
        private TextWriter _output;

        public ChromeTraceProcessor(ChromeTraceOptions options)
        {
            _fileName = options.FileName;
            _markers = new Dictionary<uint, string>();
            _generatedIds = new Dictionary<int, string>();
        }

        public void Dispose()
        {
            _output?.Dispose();
        }
        
        public void Activate()
        {
            
        }
        
        public void HandleMessage(int threadId, ref Protocol.CaptureStart msg)
        {
            _output = new StreamWriter(_fileName, false, UTF8NoBom);
            WriteHeader(_output);
        }
        
        public void HandleMessage(int threadId, ref Protocol.CaptureStop msg)
        {
            WriteFooter(_output);
            _output.Close();
            
            // We expect profiler to broadcast all markers at the start
            _markers.Clear();
        }

        public void HandleMessage(int threadId, ref Protocol.RegisterMarker msg)
        {
            _markers[msg.MarkerId] = msg.Name;
        }

        public void HandleMessage(int threadId, ref Protocol.ScopeBegin msg)
        {
            string name = _markers[msg.MarkerId];
            WriteEvent(_output, 0, threadId, name, "B", new TimeSpan(msg.Timestamp));
        }

        public void HandleMessage(int threadId, ref Protocol.ScopeEnd msg)
        {
            string name = _markers[msg.MarkerId];
            WriteEvent(_output, 0, threadId, name, "E", new TimeSpan(msg.Timestamp));
        }
        
        public void HandleMessage(int threadId, ref Protocol.ActivityBegin msg)
        {
            string name = MakeUniqueId(_markers[msg.MarkerId], msg.Param1, msg.Param2, msg.Param3);
            _generatedIds[msg.UniqueId] = name;
            WriteAsyncEvent(_output, 0, (uint)msg.UniqueId, name, "b", new TimeSpan(msg.Timestamp));
        }

        public void HandleMessage(int threadId, ref Protocol.ActivityEnd msg)
        {
            if (!_generatedIds.Remove(msg.UniqueId, out var name))
                return;
            WriteAsyncEvent(_output, 0, (uint)msg.UniqueId, name, "e", new TimeSpan(msg.Timestamp));
        }

        public void HandleMessage(int threadId, ref Protocol.Event msg)
        {
            string name = MakeUniqueId(_markers[msg.MarkerId], msg.Param1, msg.Param2, msg.Param3);
            WriteEvent(_output, 0, threadId, name, "i", new TimeSpan(msg.Timestamp));
        }

        private static void WriteHeader(TextWriter output)
        {
            output.WriteLine("{\n");
            output.WriteLine("\"Instructions Readme\": \"1) Open Chrome or Edge, 2) go to chrome://tracing, 3) click Load, 4) navigate to this file.\",");
            output.WriteLine();
            output.WriteLine("\"traceEvents\":[");
        }
        
        private static void WriteFooter(TextWriter output)
        {
            output.WriteLine("{}");
            output.WriteLine("\n]");
            output.WriteLine("}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteEvent(TextWriter output, int pid, long tid, string label, string type, TimeSpan timestamp)
        {
            output.Write("{ \"pid\": ");
            output.Write(pid);
            output.Write(", \"tid\": ");
            output.Write(tid);
            output.Write(", \"ts\": ");
            WriteTimespan(output, timestamp);
            output.Write(", \"ph\": \""); 
            WriteEscapedJson(output, type);
            output.Write("\", \"name\": \"");
            WriteEscapedJson(output, label);
            output.WriteLine("\" },");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteAsyncEvent(TextWriter output, int pid, uint id, string label, string type, TimeSpan timestamp)
        {
            output.Write("{ \"pid\": ");
            output.Write(pid);
            output.Write(", \"id\": ");
            output.Write(id);
            output.Write(", \"name\": \"");
            WriteEscapedJson(output, label);
            output.Write("\", \"ts\": ");
            WriteTimespan(output, timestamp);
            output.Write(", \"ph\": \""); 
            WriteEscapedJson(output, type);
            output.WriteLine("\" },");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteTimespan(TextWriter output, TimeSpan time)
        {
            // Write out time as an integer rounded to the nearest microsecond (ticks are in 100ns resolution)
            var timestampToMicroseconds = TimeHelper.TimestampToMicroseconds(time.Ticks);
            output.Write(timestampToMicroseconds.ToString(CultureInfo.InvariantCulture));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteEscapedJson(TextWriter output, string value)
        {
            if (value == null)
                return;

            // We want to optimize for the case where we don't need to escape anything
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"' || c == '\\' || c < 32 || c > 126)
                {
                    WriteEscapedJson(output, value, i);
                    return;
                }
            }
            output.Write(value);
        }

        private const char ESCAPE_CHAR = '\\';
        private static readonly char[] HexLookup = {'0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

        private static void WriteEscapedJson(TextWriter output, string value, int indexOfFirstCharThatNeedsEscaping)
        {
            int maxEncodedStringLen = value.Length * 6;
            // We shouldn't be dealing with very long strings here, and we know that if all chars are unicode the string
            // length will be 6 times longer when encoded
            char[] newStringChars = maxEncodedStringLen <= WriteEscapedJsonBuffer.Length ? WriteEscapedJsonBuffer : new char[maxEncodedStringLen];

            int index = 0;
            for (; index < indexOfFirstCharThatNeedsEscaping; index++)
                newStringChars[index] = value[index];

            int escapedStringIndex = index;
            for (; index < value.Length; index++)
            {
                char c = value[index];
                switch (c)
                {
                    case '"':
                        newStringChars[escapedStringIndex++] = ESCAPE_CHAR;
                        newStringChars[escapedStringIndex++] = '"';
                        break;
                    case '\\':
                        newStringChars[escapedStringIndex++] = ESCAPE_CHAR;
                        newStringChars[escapedStringIndex++] = '\\';
                        break;
                    case '\b':
                        newStringChars[escapedStringIndex++] = ESCAPE_CHAR;
                        newStringChars[escapedStringIndex++] = 'b';
                        break;
                    case '\f':
                        newStringChars[escapedStringIndex++] = ESCAPE_CHAR;
                        newStringChars[escapedStringIndex++] = 'f';
                        break;
                    case '\n':
                        newStringChars[escapedStringIndex++] = ESCAPE_CHAR;
                        newStringChars[escapedStringIndex++] = 'n';
                        break;
                    case '\r':
                        newStringChars[escapedStringIndex++] = ESCAPE_CHAR;
                        newStringChars[escapedStringIndex++] = 'r';
                        break;
                    case '\t':
                        newStringChars[escapedStringIndex++] = ESCAPE_CHAR;
                        newStringChars[escapedStringIndex++] = 't';
                        break;
                    default:
                        if (c < 32 || c > 126)
                        {
                            newStringChars[escapedStringIndex++] = ESCAPE_CHAR;
                            newStringChars[escapedStringIndex++] = 'u';
                            newStringChars[escapedStringIndex++] = HexLookup[(c >> 12) & 0xf];
                            newStringChars[escapedStringIndex++] = HexLookup[(c >> 8) & 0xf];
                            newStringChars[escapedStringIndex++] = HexLookup[(c >> 4) & 0xf];
                            newStringChars[escapedStringIndex++] = HexLookup[(c >> 0) & 0xf];
                            break;
                        }
                        newStringChars[escapedStringIndex++] = c;
                        break;
                }
            }
            output.Write(newStringChars, 0, escapedStringIndex);
        }

        private static string MakeUniqueId(string name, TypedAttribute param1, TypedAttribute param2, TypedAttribute param3)
        {
            var ret = name;
            if (param1.ParamType != TypedAttribute.ValueType.None)
                ret += $"[{FormatTypedAttribute(param1)}]";
            if (param2.ParamType != TypedAttribute.ValueType.None)
                ret += $"[{FormatTypedAttribute(param2)}]";
            if (param3.ParamType != TypedAttribute.ValueType.None)
                ret += $"[{FormatTypedAttribute(param3)}]";
            return ret;
        }

        private static string FormatTypedAttribute(TypedAttribute param)
        {
            switch (param.ParamType)
            {
                case TypedAttribute.ValueType.None:
                    return null;
                case TypedAttribute.ValueType.Bool:
                    return param.AsBool.ToString();
                case TypedAttribute.ValueType.Int:
                    return param.AsInt.ToString();
                case TypedAttribute.ValueType.Float:
                    return param.AsFloat.ToString(CultureInfo.InvariantCulture);
                case TypedAttribute.ValueType.String:
                    return param.AsString;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}