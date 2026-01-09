using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace NanoProfiler
{
    public struct TypedAttribute
    {
        public enum ValueType : int
        {
            None = 0,
            Bool,
            Int,
            Float,
            String
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct Union
        {
            [FieldOffset(0)] public bool Bool;
            [FieldOffset(0)] public int Int;
            [FieldOffset(0)] public float Float;
        }
        
        private ValueType _type;
        private Union _union;
        private string _stringValue;

        public TypedAttribute(bool value)
        {
            _type = ValueType.Bool;
            _union = new Union() { Bool = value };
            _stringValue = string.Empty;
        }

        public TypedAttribute(int value)
        {
            _type = ValueType.Int;
            _union = new Union() { Int = value };
            _stringValue = string.Empty;
        }

        public TypedAttribute(float value)
        {
            _type = ValueType.Float;
            _union = new Union() { Float = value };
            _stringValue = string.Empty;
        }

        public TypedAttribute(string value)
        {
            _type = ValueType.String;
            _union = new Union();
            _stringValue = value;
        }

        public ValueType ParamType => _type;
        public bool AsBool => _union.Bool;
        public int AsInt => _union.Int;
        public float AsFloat => _union.Float;
        public string AsString => _stringValue;
        
        public static implicit operator TypedAttribute(bool value) { return new TypedAttribute(value); }
        public static implicit operator TypedAttribute(int value) { return new TypedAttribute(value); }
        public static implicit operator TypedAttribute(float value) { return new TypedAttribute(value); }
        public static implicit operator TypedAttribute(string value) { return new TypedAttribute(value); }

        public void Transfer(ISerializer serializer)
        {
            serializer.Transfer(ref _type);
            switch (_type)
            {
                case ValueType.None:
                    break;
                case ValueType.Bool:
                    serializer.Transfer(ref _union.Bool);
                    break;
                case ValueType.Int:
                    serializer.Transfer(ref _union.Int);
                    break;
                case ValueType.Float:
                    serializer.Transfer(ref _union.Float);
                    break;
                case ValueType.String:
                    serializer.Transfer(ref _stringValue);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public override string ToString()
        {
            switch (_type)
            {
                case ValueType.None:
                    return string.Empty;
                case ValueType.Bool:
                    return _union.Bool.ToString();
                case ValueType.Int:
                    return _union.Int.ToString();
                case ValueType.Float:
                    return _union.Float.ToString(CultureInfo.CurrentCulture);
                case ValueType.String:
                    return _stringValue;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}