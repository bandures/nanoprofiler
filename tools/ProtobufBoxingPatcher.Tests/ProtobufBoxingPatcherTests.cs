using NUnit.Framework;

namespace BoxingPatcher.Tests;

[TestFixture]
public class ProtobufBoxingPatcherTests
{
    private ProtobufBoxingPatcher _patcher = null!;

    [SetUp]
    public void SetUp()
    {
        _patcher = new ProtobufBoxingPatcher();
    }

    #region SameValueType

    [Test]
    public void SameValueType_FieldDeclaration_ReplacedWithTypedField()
    {
        var input = BuildSameTypeOneof("timestamp", "long",
            ("TimestampDeltaUs", "0L"),
            ("TimestampAbsoluteUs", "0L"));

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("private long timestamp_;"));
        Assert.That(result, Does.Not.Contain("private object timestamp_;"));
    }

    [Test]
    public void SameValueType_Getter_CastRemoved()
    {
        var input = BuildSameTypeOneof("timestamp", "long",
            ("TimestampDeltaUs", "0L"),
            ("TimestampAbsoluteUs", "0L"));

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("HasTimestampDeltaUs ? timestamp_ : 0L"));
        Assert.That(result, Does.Contain("HasTimestampAbsoluteUs ? timestamp_ : 0L"));
        Assert.That(result, Does.Not.Contain("(long) timestamp_"));
    }

    [Test]
    public void SameValueType_Setter_Unchanged()
    {
        var input = BuildSameTypeOneof("timestamp", "long",
            ("TimestampDeltaUs", "0L"),
            ("TimestampAbsoluteUs", "0L"));

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("timestamp_ = value;"));
    }

    [Test]
    public void SameValueType_Clear_NullAssignmentRemoved()
    {
        var input = BuildSameTypeOneof("timestamp", "long",
            ("TimestampDeltaUs", "0L"),
            ("TimestampAbsoluteUs", "0L"));

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("timestamp_ = null;"));
        Assert.That(result, Does.Contain("timestampCase_ = TimestampOneofCase.None;"));
    }

    [Test]
    public void SameValueType_SingleMember_Works()
    {
        var input = BuildSameTypeOneof("optionalTrustedPacketSequenceId", "uint",
            ("TrustedPacketSequenceId", "0"));

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("private uint optionalTrustedPacketSequenceId_;"));
        Assert.That(result, Does.Not.Contain("private object optionalTrustedPacketSequenceId_;"));
        Assert.That(result, Does.Not.Contain("(uint) optionalTrustedPacketSequenceId_"));
        Assert.That(result, Does.Not.Contain("optionalTrustedPacketSequenceId_ = null;"));
    }

    [Test]
    public void SameValueType_UlongMembers_Works()
    {
        var input = BuildSameTypeOneof("id", "ulong",
            ("UnscopedId", "0UL"),
            ("LocalId", "0UL"),
            ("GlobalId", "0UL"));

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("private ulong id_;"));
        Assert.That(result, Does.Not.Contain("(ulong) id_"));
        Assert.That(result, Does.Contain("HasUnscopedId ? id_ : 0UL"));
        Assert.That(result, Does.Contain("HasLocalId ? id_ : 0UL"));
        Assert.That(result, Does.Contain("HasGlobalId ? id_ : 0UL"));
    }

    #endregion

    #region MultiValueType (union struct)

    [Test]
    public void MultiValueType_GeneratesUnionStruct()
    {
        var input = BuildMultiTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain(
            "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]"));
        Assert.That(result, Does.Contain("private struct CounterValueFieldUnion"));
        Assert.That(result, Does.Contain("[System.Runtime.InteropServices.FieldOffset(0)] public long Int64;"));
        Assert.That(result, Does.Contain("[System.Runtime.InteropServices.FieldOffset(0)] public double Double;"));
    }

    [Test]
    public void MultiValueType_FieldDeclaration_UsesUnionType()
    {
        var input = BuildMultiTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("private CounterValueFieldUnion counterValueField_;"));
        Assert.That(result, Does.Not.Contain("private object counterValueField_;"));
    }

    [Test]
    public void MultiValueType_Getter_UsesUnionMemberAccess()
    {
        var input = BuildMultiTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("HasCounterValue ? counterValueField_.Int64 : 0L"));
        Assert.That(result, Does.Contain("HasDoubleCounterValue ? counterValueField_.Double : 0D"));
        Assert.That(result, Does.Not.Contain("(long) counterValueField_"));
        Assert.That(result, Does.Not.Contain("(double) counterValueField_"));
    }

    [Test]
    public void MultiValueType_Setter_UsesUnionMemberAccess()
    {
        var input = BuildMultiTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("counterValueField_.Int64 = value;"));
        Assert.That(result, Does.Contain("counterValueField_.Double = value;"));
    }

    [Test]
    public void MultiValueType_Clear_NullAssignmentRemoved()
    {
        var input = BuildMultiTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("counterValueField_ = null;"));
        Assert.That(result, Does.Contain(
            "counterValueFieldCase_ = CounterValueFieldOneofCase.None;"));
    }

    [Test]
    public void MultiValueType_CaseEnumAssignment_Preserved()
    {
        var input = BuildMultiTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain(
            "counterValueFieldCase_ = CounterValueFieldOneofCase.CounterValue;"));
        Assert.That(result, Does.Contain(
            "counterValueFieldCase_ = CounterValueFieldOneofCase.DoubleCounterValue;"));
    }

    #endregion

    #region MixedTypes (single value type → separate field)

    [Test]
    public void MixedTypes_SingleValueType_ObjectRemovedAndTypedFieldsAdded()
    {
        var input = BuildMixedTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("private object nameField_;"));
        Assert.That(result, Does.Contain("private ulong nameField_UInt64_;"));
        Assert.That(result, Does.Contain("private string nameField_String_;"));
    }

    [Test]
    public void MixedTypes_SingleValueType_ValueGetter_RedirectedToTypedField()
    {
        var input = BuildMixedTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("HasNameIid ? nameField_UInt64_ : 0UL"));
        Assert.That(result, Does.Not.Contain("(ulong) nameField_"));
    }

    [Test]
    public void MixedTypes_SingleValueType_ReferenceGetter_RedirectedToTypedField()
    {
        var input = BuildMixedTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("HasName ? nameField_String_ : \"\""));
        Assert.That(result, Does.Not.Contain("(string) nameField_"));
    }

    [Test]
    public void MixedTypes_SingleValueType_ValueSetter_RedirectedToTypedField()
    {
        var input = BuildMixedTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("nameField_UInt64_ = value;"));
    }

    [Test]
    public void MixedTypes_SingleValueType_ReferenceSetter_RedirectedToTypedField()
    {
        var input = BuildMixedTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain(
            "nameField_String_ = pb::ProtoPreconditions.CheckNotNull(value, \"value\");"));
        Assert.That(result, Does.Not.Contain(
            "nameField_ = pb::ProtoPreconditions.CheckNotNull(value, \"value\");"));
    }

    [Test]
    public void MixedTypes_SingleValueType_Clear_NullRedirectedToRefField()
    {
        var input = BuildMixedTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("nameField_ = null;"));
        Assert.That(result, Does.Contain("nameField_String_ = null;"));
    }

    #endregion

    #region MixedTypes (multiple value types → union struct)

    [Test]
    public void MixedTypes_MultiValueType_GeneratesUnionStruct()
    {
        var input = BuildComplexMixedOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("private struct ValueUnion"));
        Assert.That(result, Does.Contain("[System.Runtime.InteropServices.FieldOffset(0)] public bool Bool;"));
        Assert.That(result, Does.Contain("[System.Runtime.InteropServices.FieldOffset(0)] public ulong UInt64;"));
        Assert.That(result, Does.Contain("[System.Runtime.InteropServices.FieldOffset(0)] public long Int64;"));
        Assert.That(result, Does.Contain("[System.Runtime.InteropServices.FieldOffset(0)] public double Double;"));
    }

    [Test]
    public void MixedTypes_MultiValueType_ObjectRemovedAndTypedFieldsAdded()
    {
        var input = BuildComplexMixedOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("private object value_;"));
        Assert.That(result, Does.Contain("private ValueUnion valueUnion_;"));
        Assert.That(result, Does.Contain("private string value_String_;"));
    }

    [Test]
    public void MixedTypes_MultiValueType_ValueGetters_UseUnionMemberAccess()
    {
        var input = BuildComplexMixedOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("HasBoolValue ? valueUnion_.Bool : false"));
        Assert.That(result, Does.Contain("HasUintValue ? valueUnion_.UInt64 : 0UL"));
        Assert.That(result, Does.Contain("HasIntValue ? valueUnion_.Int64 : 0L"));
        Assert.That(result, Does.Contain("HasDoubleValue ? valueUnion_.Double : 0D"));
    }

    [Test]
    public void MixedTypes_MultiValueType_ReferenceGetter_RedirectedToTypedField()
    {
        var input = BuildComplexMixedOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("HasStringValue ? value_String_ : \"\""));
        Assert.That(result, Does.Not.Contain("(string) value_"));
    }

    [Test]
    public void MixedTypes_MultiValueType_ValueSetters_UseUnionMemberAccess()
    {
        var input = BuildComplexMixedOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("valueUnion_.Bool = value;"));
        Assert.That(result, Does.Contain("valueUnion_.UInt64 = value;"));
        Assert.That(result, Does.Contain("valueUnion_.Int64 = value;"));
        Assert.That(result, Does.Contain("valueUnion_.Double = value;"));
    }

    [Test]
    public void MixedTypes_MultiValueType_ReferenceSetter_RedirectedToTypedField()
    {
        var input = BuildComplexMixedOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain(
            "value_String_ = pb::ProtoPreconditions.CheckNotNull(value, \"value\");"));
        Assert.That(result, Does.Not.Contain(
            "value_ = pb::ProtoPreconditions.CheckNotNull(value, \"value\");"));
    }

    [Test]
    public void MixedTypes_MultiValueType_Clear_NullRedirectedToRefField()
    {
        var input = BuildComplexMixedOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("value_ = null;"));
        Assert.That(result, Does.Contain("value_String_ = null;"));
    }

    #endregion

    #region ReferenceOnly

    [Test]
    public void ReferenceOnly_NoChanges()
    {
        var input = BuildReferenceOnlyOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Is.EqualTo(input));
    }

    #endregion

    #region Analysis

    [Test]
    public void AnalyzeFields_SameValueType_StrategyCorrect()
    {
        var input = BuildSameTypeOneof("timestamp", "long",
            ("TimestampDeltaUs", "0L"),
            ("TimestampAbsoluteUs", "0L"));

        var fields = _patcher.AnalyzeFields(input);

        Assert.That(fields, Has.Count.EqualTo(1));
        Assert.That(fields[0].Strategy, Is.EqualTo(OneofStrategy.SameValueType));
        Assert.That(fields[0].DistinctValueTypes, Is.EqualTo(new[] { "long" }));
    }

    [Test]
    public void AnalyzeFields_MultiValueType_StrategyCorrect()
    {
        var input = BuildMultiTypeOneof();

        var fields = _patcher.AnalyzeFields(input);

        Assert.That(fields, Has.Count.EqualTo(1));
        Assert.That(fields[0].Strategy, Is.EqualTo(OneofStrategy.MultiValueType));
        Assert.That(fields[0].DistinctValueTypes, Has.Count.EqualTo(2));
    }

    [Test]
    public void AnalyzeFields_MixedTypes_StrategyCorrect()
    {
        var input = BuildMixedTypeOneof();

        var fields = _patcher.AnalyzeFields(input);

        Assert.That(fields, Has.Count.EqualTo(1));
        Assert.That(fields[0].Strategy, Is.EqualTo(OneofStrategy.MixedTypes));
        Assert.That(fields[0].ValueTypeMembers, Has.Count.EqualTo(1));
        Assert.That(fields[0].RefTypeMembers, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnalyzeFields_ReferenceOnly_NoFields()
    {
        var input = BuildReferenceOnlyOneof();

        var fields = _patcher.AnalyzeFields(input);

        Assert.That(fields, Is.Empty);
    }

    [Test]
    public void AnalyzeFields_NoObjectFields_NoFields()
    {
        var input = @"
    private long timestamp_;
    private string name_;
";

        var fields = _patcher.AnalyzeFields(input);

        Assert.That(fields, Is.Empty);
    }

    [Test]
    public void AnalyzeFields_ObjectFieldWithoutGetters_NoFields()
    {
        var input = @"
    private object mystery_;
    public void DoStuff() { mystery_ = 42; }
";

        var fields = _patcher.AnalyzeFields(input);

        Assert.That(fields, Is.Empty);
    }

    [Test]
    public void AnalyzeFields_MultipleOneofs_AllDetected()
    {
        var input = BuildSameTypeOneof("timestamp", "long",
                        ("TimestampDeltaUs", "0L"),
                        ("TimestampAbsoluteUs", "0L"))
                    + "\n"
                    + BuildMixedTypeOneof();

        var fields = _patcher.AnalyzeFields(input);

        Assert.That(fields, Has.Count.EqualTo(2));
    }

    #endregion

    #region Idempotency

    [Test]
    public void PatchContent_Idempotent_SameValueType()
    {
        var input = BuildSameTypeOneof("timestamp", "long",
            ("TimestampDeltaUs", "0L"),
            ("TimestampAbsoluteUs", "0L"));

        var first = _patcher.PatchContent(input);
        var second = _patcher.PatchContent(first);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void PatchContent_Idempotent_MultiValueType()
    {
        var input = BuildMultiTypeOneof();

        var first = _patcher.PatchContent(input);
        var second = _patcher.PatchContent(first);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void PatchContent_Idempotent_MixedTypes_SingleValueType()
    {
        var input = BuildMixedTypeOneof();

        var first = _patcher.PatchContent(input);
        var second = _patcher.PatchContent(first);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void PatchContent_Idempotent_MixedTypes_MultiValueType()
    {
        var input = BuildComplexMixedOneof();

        var first = _patcher.PatchContent(input);
        var second = _patcher.PatchContent(first);

        Assert.That(second, Is.EqualTo(first));
    }

    #endregion

    #region DataOneofStyle Getter (caseField_ == Enum.Member pattern)

    [Test]
    public void DataOneofStyleGetter_ValueType_Patched()
    {
        var input = @"
    private object optionalId_;
    public enum OptionalIdOneofCase {
      None = 0,
      Id = 5,
    }
    private OptionalIdOneofCase optionalIdCase_ = OptionalIdOneofCase.None;

    public int Id {
      get { return optionalIdCase_ == OptionalIdOneofCase.Id ? (int) optionalId_ : 0; }
      set {
        optionalId_ = value;
        optionalIdCase_ = OptionalIdOneofCase.Id;
      }
    }
    public bool HasId {
      get { return optionalIdCase_ == OptionalIdOneofCase.Id; }
    }

    public void ClearOptionalId() {
      optionalIdCase_ = OptionalIdOneofCase.None;
      optionalId_ = null;
    }
";

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("private int optionalId_;"));
        Assert.That(result, Does.Not.Contain("private object optionalId_;"));
        Assert.That(result, Does.Not.Contain("(int) optionalId_"));
        Assert.That(result, Does.Not.Contain("optionalId_ = null;"));
    }

    [Test]
    public void DataOneofStyleGetter_ReferenceType_Skipped()
    {
        var input = @"
    private object data_;
    public enum DataOneofCase {
      None = 0,
      TrackEvent = 11,
    }
    private DataOneofCase dataCase_ = DataOneofCase.None;

    public global::Perfetto.Protos.TrackEvent TrackEvent {
      get { return dataCase_ == DataOneofCase.TrackEvent ? (global::Perfetto.Protos.TrackEvent) data_ : null; }
      set {
        data_ = value;
        dataCase_ = value == null ? DataOneofCase.None : DataOneofCase.TrackEvent;
      }
    }

    public void ClearData() {
      dataCase_ = DataOneofCase.None;
      data_ = null;
    }
";

        var result = _patcher.PatchContent(input);

        Assert.That(result, Is.EqualTo(input));
    }

    #endregion

    #region PatchFile

    [Test]
    public void PatchFile_WritesPatched_ReturnsTrue()
    {
        var input = BuildSameTypeOneof("ts", "long", ("TsDelta", "0L"));
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.cs");
        File.WriteAllText(path, input);

        try
        {
            var result = _patcher.PatchFile(path);
            var content = File.ReadAllText(path);

            Assert.That(result, Is.True);
            Assert.That(content, Does.Contain("private long ts_;"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void PatchFile_NoChanges_ReturnsFalse()
    {
        var input = "public class Empty { }";
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.cs");
        File.WriteAllText(path, input);

        try
        {
            var result = _patcher.PatchFile(path);

            Assert.That(result, Is.False);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void PatchFile_Idempotent_SecondCallReturnsFalse()
    {
        var input = BuildSameTypeOneof("ts", "long", ("TsDelta", "0L"));
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.cs");
        File.WriteAllText(path, input);

        try
        {
            _patcher.PatchFile(path);
            var secondResult = _patcher.PatchFile(path);

            Assert.That(secondResult, Is.False);
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion

    #region Real Protobuf Pattern Fragments

    [Test]
    public void RealPattern_TracePacketTrustedPacketSequenceId_Patched()
    {
        var input = @"
    private object optionalTrustedPacketSequenceId_;
    public enum OptionalTrustedPacketSequenceIdOneofCase {
      None = 0,
      TrustedPacketSequenceId = 10,
    }
    private OptionalTrustedPacketSequenceIdOneofCase optionalTrustedPacketSequenceIdCase_ = OptionalTrustedPacketSequenceIdOneofCase.None;

    public uint TrustedPacketSequenceId {
      get { return HasTrustedPacketSequenceId ? (uint) optionalTrustedPacketSequenceId_ : 0; }
      set {
        optionalTrustedPacketSequenceId_ = value;
        optionalTrustedPacketSequenceIdCase_ = OptionalTrustedPacketSequenceIdOneofCase.TrustedPacketSequenceId;
      }
    }
    public bool HasTrustedPacketSequenceId {
      get { return optionalTrustedPacketSequenceIdCase_ == OptionalTrustedPacketSequenceIdOneofCase.TrustedPacketSequenceId; }
    }

    public void ClearOptionalTrustedPacketSequenceId() {
      optionalTrustedPacketSequenceIdCase_ = OptionalTrustedPacketSequenceIdOneofCase.None;
      optionalTrustedPacketSequenceId_ = null;
    }
";

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("private uint optionalTrustedPacketSequenceId_;"));
        Assert.That(result, Does.Not.Contain("private object optionalTrustedPacketSequenceId_;"));
        Assert.That(result, Does.Contain(
            "HasTrustedPacketSequenceId ? optionalTrustedPacketSequenceId_ : 0"));
        Assert.That(result, Does.Not.Contain("optionalTrustedPacketSequenceId_ = null;"));
    }

    [Test]
    public void RealPattern_TrackEventCounterValueField_UsesUnion()
    {
        var input = @"
    private object counterValueField_;
    public enum CounterValueFieldOneofCase {
      None = 0,
      CounterValue = 30,
      DoubleCounterValue = 44,
    }
    private CounterValueFieldOneofCase counterValueFieldCase_ = CounterValueFieldOneofCase.None;

    public long CounterValue {
      get { return HasCounterValue ? (long) counterValueField_ : 0L; }
      set {
        counterValueField_ = value;
        counterValueFieldCase_ = CounterValueFieldOneofCase.CounterValue;
      }
    }
    public bool HasCounterValue {
      get { return counterValueFieldCase_ == CounterValueFieldOneofCase.CounterValue; }
    }

    public double DoubleCounterValue {
      get { return HasDoubleCounterValue ? (double) counterValueField_ : 0D; }
      set {
        counterValueField_ = value;
        counterValueFieldCase_ = CounterValueFieldOneofCase.DoubleCounterValue;
      }
    }
    public bool HasDoubleCounterValue {
      get { return counterValueFieldCase_ == CounterValueFieldOneofCase.DoubleCounterValue; }
    }

    public void ClearCounterValueField() {
      counterValueFieldCase_ = CounterValueFieldOneofCase.None;
      counterValueField_ = null;
    }
";

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("private struct CounterValueFieldUnion"));
        Assert.That(result, Does.Contain("private CounterValueFieldUnion counterValueField_;"));
        Assert.That(result, Does.Not.Contain("private object counterValueField_;"));

        Assert.That(result, Does.Contain("HasCounterValue ? counterValueField_.Int64 : 0L"));
        Assert.That(result, Does.Contain("HasDoubleCounterValue ? counterValueField_.Double : 0D"));

        Assert.That(result, Does.Contain("counterValueField_.Int64 = value;"));
        Assert.That(result, Does.Contain("counterValueField_.Double = value;"));

        Assert.That(result, Does.Not.Contain("counterValueField_ = null;"));
    }

    [Test]
    public void RealPattern_TrackEventNameField_UsesSeparateField()
    {
        var input = @"
    private object nameField_;
    public enum NameFieldOneofCase {
      None = 0,
      NameIid = 10,
      Name = 23,
    }
    private NameFieldOneofCase nameFieldCase_ = NameFieldOneofCase.None;

    public ulong NameIid {
      get { return HasNameIid ? (ulong) nameField_ : 0UL; }
      set {
        nameField_ = value;
        nameFieldCase_ = NameFieldOneofCase.NameIid;
      }
    }
    public bool HasNameIid {
      get { return nameFieldCase_ == NameFieldOneofCase.NameIid; }
    }

    public string Name {
      get { return HasName ? (string) nameField_ : """"; }
      set {
        nameField_ = pb::ProtoPreconditions.CheckNotNull(value, ""value"");
        nameFieldCase_ = NameFieldOneofCase.Name;
      }
    }
    public bool HasName {
      get { return nameFieldCase_ == NameFieldOneofCase.Name; }
    }

    public void ClearNameField() {
      nameFieldCase_ = NameFieldOneofCase.None;
      nameField_ = null;
    }
";

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("private object nameField_;"));
        Assert.That(result, Does.Contain("private ulong nameField_UInt64_;"));
        Assert.That(result, Does.Contain("private string nameField_String_;"));
        Assert.That(result, Does.Not.Contain("NameFieldUnion"));

        Assert.That(result, Does.Contain("HasNameIid ? nameField_UInt64_ : 0UL"));
        Assert.That(result, Does.Contain("HasName ? nameField_String_ : \"\""));

        Assert.That(result, Does.Contain("nameField_UInt64_ = value;"));
        Assert.That(result, Does.Contain(
            "nameField_String_ = pb::ProtoPreconditions.CheckNotNull(value, \"value\");"));

        Assert.That(result, Does.Not.Contain("nameField_ = null;"));
        Assert.That(result, Does.Contain("nameField_String_ = null;"));
    }

    [Test]
    public void RealPattern_TracePacketData_Skipped()
    {
        var input = @"
    private object data_;
    public enum DataOneofCase {
      None = 0,
      TrackEvent = 11,
      TrackDescriptor = 60,
      ProcessDescriptor = 43,
    }
    private DataOneofCase dataCase_ = DataOneofCase.None;

    public global::Perfetto.Protos.TrackEvent TrackEvent {
      get { return dataCase_ == DataOneofCase.TrackEvent ? (global::Perfetto.Protos.TrackEvent) data_ : null; }
      set {
        data_ = value;
        dataCase_ = value == null ? DataOneofCase.None : DataOneofCase.TrackEvent;
      }
    }

    public global::Perfetto.Protos.TrackDescriptor TrackDescriptor {
      get { return dataCase_ == DataOneofCase.TrackDescriptor ? (global::Perfetto.Protos.TrackDescriptor) data_ : null; }
      set {
        data_ = value;
        dataCase_ = value == null ? DataOneofCase.None : DataOneofCase.TrackDescriptor;
      }
    }

    public global::Perfetto.Protos.ProcessDescriptor ProcessDescriptor {
      get { return dataCase_ == DataOneofCase.ProcessDescriptor ? (global::Perfetto.Protos.ProcessDescriptor) data_ : null; }
      set {
        data_ = value;
        dataCase_ = value == null ? DataOneofCase.None : DataOneofCase.ProcessDescriptor;
      }
    }

    public void ClearData() {
      dataCase_ = DataOneofCase.None;
      data_ = null;
    }
";

        var result = _patcher.PatchContent(input);

        Assert.That(result, Is.EqualTo(input));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void EmptyInput_NoChanges()
    {
        var result = _patcher.PatchContent("");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SharedValueType_MultipleMembers_SingleField()
    {
        var input = BuildSameTypeOneof("id", "ulong",
            ("UnscopedId", "0UL"),
            ("LocalId", "0UL"),
            ("GlobalId", "0UL"));

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("private ulong id_;"));
        Assert.That(result, Does.Not.Contain("private object id_;"));

        var count = CountOccurrences(result, "private ulong id_;");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void MultipleOneofsInSameFile_AllPatched()
    {
        var input = BuildSameTypeOneof("timestamp", "long",
                ("TimestampDeltaUs", "0L"),
                ("TimestampAbsoluteUs", "0L"))
            + "\n"
            + BuildMultiTypeOneof()
            + "\n"
            + BuildMixedTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("private long timestamp_;"));
        Assert.That(result, Does.Contain("private CounterValueFieldUnion counterValueField_;"));
        Assert.That(result, Does.Contain("private ulong nameField_UInt64_;"));
        Assert.That(result, Does.Contain("private string nameField_String_;"));
        Assert.That(result, Does.Not.Contain("private object nameField_;"));
    }

    [Test]
    public void PreservesIndentation()
    {
        var input = @"        private object counterValueField_;
        public long CounterValue {
          get { return HasCounterValue ? (long) counterValueField_ : 0L; }
          set {
            counterValueField_ = value;
            counterValueFieldCase_ = CounterValueFieldOneofCase.CounterValue;
          }
        }
        public double DoubleCounterValue {
          get { return HasDoubleCounterValue ? (double) counterValueField_ : 0D; }
          set {
            counterValueField_ = value;
            counterValueFieldCase_ = CounterValueFieldOneofCase.DoubleCounterValue;
          }
        }
        public void ClearCounterValueField() {
          counterValueFieldCase_ = CounterValueFieldOneofCase.None;
          counterValueField_ = null;
        }
";

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Contain("        private struct CounterValueFieldUnion"));
        Assert.That(result, Does.Contain("        private CounterValueFieldUnion counterValueField_;"));
    }

    [Test]
    public void MixedTypes_ProtoTypeDescriptor_UsesSeparateField()
    {
        var input = @"
    private object protoTypeDescriptor_;
    public enum ProtoTypeDescriptorOneofCase {
      None = 0,
      ProtoTypeName = 16,
      ProtoTypeNameIid = 13,
    }
    private ProtoTypeDescriptorOneofCase protoTypeDescriptorCase_ = ProtoTypeDescriptorOneofCase.None;

    public string ProtoTypeName {
      get { return HasProtoTypeName ? (string) protoTypeDescriptor_ : """"; }
      set {
        protoTypeDescriptor_ = pb::ProtoPreconditions.CheckNotNull(value, ""value"");
        protoTypeDescriptorCase_ = ProtoTypeDescriptorOneofCase.ProtoTypeName;
      }
    }
    public bool HasProtoTypeName {
      get { return protoTypeDescriptorCase_ == ProtoTypeDescriptorOneofCase.ProtoTypeName; }
    }

    public ulong ProtoTypeNameIid {
      get { return HasProtoTypeNameIid ? (ulong) protoTypeDescriptor_ : 0UL; }
      set {
        protoTypeDescriptor_ = value;
        protoTypeDescriptorCase_ = ProtoTypeDescriptorOneofCase.ProtoTypeNameIid;
      }
    }
    public bool HasProtoTypeNameIid {
      get { return protoTypeDescriptorCase_ == ProtoTypeDescriptorOneofCase.ProtoTypeNameIid; }
    }

    public void ClearProtoTypeDescriptor() {
      protoTypeDescriptorCase_ = ProtoTypeDescriptorOneofCase.None;
      protoTypeDescriptor_ = null;
    }
";

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("private object protoTypeDescriptor_;"));
        Assert.That(result, Does.Contain("private ulong protoTypeDescriptor_UInt64_;"));
        Assert.That(result, Does.Contain("private string protoTypeDescriptor_String_;"));

        Assert.That(result, Does.Contain("HasProtoTypeName ? protoTypeDescriptor_String_ : \"\""));
        Assert.That(result, Does.Contain("HasProtoTypeNameIid ? protoTypeDescriptor_UInt64_ : 0UL"));

        Assert.That(result, Does.Contain("protoTypeDescriptor_UInt64_ = value;"));
        Assert.That(result, Does.Contain(
            "protoTypeDescriptor_String_ = pb::ProtoPreconditions.CheckNotNull(value, \"value\");"));

        Assert.That(result, Does.Not.Contain("protoTypeDescriptor_ = null;"));
        Assert.That(result, Does.Contain("protoTypeDescriptor_String_ = null;"));
    }

    [Test]
    public void MixedTypes_NullableRefSetter_RedirectedToTypedField()
    {
        var input = @"
    private object value_;
    public enum ValueOneofCase {
      None = 0,
      IntValue = 1,
      NestedValue = 8,
    }
    private ValueOneofCase valueCase_ = ValueOneofCase.None;

    public long IntValue {
      get { return HasIntValue ? (long) value_ : 0L; }
      set {
        value_ = value;
        valueCase_ = ValueOneofCase.IntValue;
      }
    }
    public bool HasIntValue {
      get { return valueCase_ == ValueOneofCase.IntValue; }
    }

    public global::Foo.NestedMsg NestedValue {
      get { return valueCase_ == ValueOneofCase.NestedValue ? (global::Foo.NestedMsg) value_ : null; }
      set {
        value_ = value;
        valueCase_ = value == null ? ValueOneofCase.None : ValueOneofCase.NestedValue;
      }
    }

    public void ClearValue() {
      valueCase_ = ValueOneofCase.None;
      value_ = null;
    }
";

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("private object value_;"));
        Assert.That(result, Does.Contain("private long value_Int64_;"));
        Assert.That(result, Does.Contain("private global::Foo.NestedMsg value_NestedMsg_;"));

        Assert.That(result, Does.Contain("value_NestedMsg_ = value;"));
        Assert.That(result, Does.Contain(
            "valueCase_ = value == null ? ValueOneofCase.None : ValueOneofCase.NestedValue;"));

        Assert.That(result, Does.Not.Contain("value_ = null;"));
        Assert.That(result, Does.Contain("value_NestedMsg_ = null;"));
    }

    [Test]
    public void MixedTypes_ObjectFieldFullyRemoved_NoRemainingObjectReferences()
    {
        var input = BuildMixedTypeOneof();

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("private object "));
        Assert.That(result, Does.Not.Contain("(string) nameField_"));
        Assert.That(result, Does.Not.Contain("(ulong) nameField_"));

        var nameFieldRawPattern = new System.Text.RegularExpressions.Regex(
            @"(?<!_)nameField_(?!UInt64_|String_|Case)(?!\w)");
        Assert.That(nameFieldRawPattern.IsMatch(result), Is.False,
            "No bare nameField_ references should remain after patching");
    }

    [Test]
    public void MixedTypes_MultipleRefTypes_AllGetTypedFields()
    {
        var input = @"
    private object value_;
    public enum ValueOneofCase {
      None = 0,
      IntValue = 1,
      NameValue = 2,
      DescValue = 3,
    }
    private ValueOneofCase valueCase_ = ValueOneofCase.None;

    public long IntValue {
      get { return HasIntValue ? (long) value_ : 0L; }
      set {
        value_ = value;
        valueCase_ = ValueOneofCase.IntValue;
      }
    }
    public bool HasIntValue {
      get { return valueCase_ == ValueOneofCase.IntValue; }
    }

    public string NameValue {
      get { return HasNameValue ? (string) value_ : """"; }
      set {
        value_ = pb::ProtoPreconditions.CheckNotNull(value, ""value"");
        valueCase_ = ValueOneofCase.NameValue;
      }
    }
    public bool HasNameValue {
      get { return valueCase_ == ValueOneofCase.NameValue; }
    }

    public global::Foo.Bar.Nested DescValue {
      get { return HasDescValue ? (global::Foo.Bar.Nested) value_ : null; }
      set {
        value_ = value;
        valueCase_ = ValueOneofCase.DescValue;
      }
    }
    public bool HasDescValue {
      get { return valueCase_ == ValueOneofCase.DescValue; }
    }

    public void ClearValue() {
      valueCase_ = ValueOneofCase.None;
      value_ = null;
    }
";

        var result = _patcher.PatchContent(input);

        Assert.That(result, Does.Not.Contain("private object value_;"));
        Assert.That(result, Does.Contain("private long value_Int64_;"));
        Assert.That(result, Does.Contain("private string value_String_;"));
        Assert.That(result, Does.Contain("private global::Foo.Bar.Nested value_Nested_;"));

        Assert.That(result, Does.Contain("HasIntValue ? value_Int64_ : 0L"));
        Assert.That(result, Does.Contain("HasNameValue ? value_String_ : \"\""));
        Assert.That(result, Does.Contain("HasDescValue ? value_Nested_ : null"));

        Assert.That(result, Does.Contain("value_Int64_ = value;"));
        Assert.That(result, Does.Contain("value_String_ = pb::ProtoPreconditions.CheckNotNull(value, \"value\");"));
        Assert.That(result, Does.Contain("value_Nested_ = value;"));

        Assert.That(result, Does.Not.Contain("value_ = null;"));
        Assert.That(result, Does.Contain("value_String_ = null;"));
        Assert.That(result, Does.Contain("value_Nested_ = null;"));
    }

    #endregion

    #region Helpers

    private static string BuildSameTypeOneof(
        string fieldName,
        string type,
        params (string memberName, string defaultValue)[] members)
    {
        var caseName = char.ToUpper(fieldName[0]) + fieldName[1..];
        var caseEnumName = $"{caseName}OneofCase";

        var enumMembers = string.Join("\n",
            members.Select((m, i) => $"      {m.memberName} = {i + 1},"));

        var properties = string.Join("\n\n",
            members.Select(m => $@"    public {type} {m.memberName} {{
      get {{ return Has{m.memberName} ? ({type}) {fieldName}_ : {m.defaultValue}; }}
      set {{
        {fieldName}_ = value;
        {fieldName}Case_ = {caseEnumName}.{m.memberName};
      }}
    }}
    public bool Has{m.memberName} {{
      get {{ return {fieldName}Case_ == {caseEnumName}.{m.memberName}; }}
    }}"));

        return $@"
    private object {fieldName}_;
    public enum {caseEnumName} {{
      None = 0,
{enumMembers}
    }}
    private {caseEnumName} {fieldName}Case_ = {caseEnumName}.None;

{properties}

    public void Clear{caseName}() {{
      {fieldName}Case_ = {caseEnumName}.None;
      {fieldName}_ = null;
    }}
";
    }

    private static string BuildMultiTypeOneof()
    {
        return @"
    private object counterValueField_;
    public enum CounterValueFieldOneofCase {
      None = 0,
      CounterValue = 30,
      DoubleCounterValue = 44,
    }
    private CounterValueFieldOneofCase counterValueFieldCase_ = CounterValueFieldOneofCase.None;

    public long CounterValue {
      get { return HasCounterValue ? (long) counterValueField_ : 0L; }
      set {
        counterValueField_ = value;
        counterValueFieldCase_ = CounterValueFieldOneofCase.CounterValue;
      }
    }
    public bool HasCounterValue {
      get { return counterValueFieldCase_ == CounterValueFieldOneofCase.CounterValue; }
    }

    public double DoubleCounterValue {
      get { return HasDoubleCounterValue ? (double) counterValueField_ : 0D; }
      set {
        counterValueField_ = value;
        counterValueFieldCase_ = CounterValueFieldOneofCase.DoubleCounterValue;
      }
    }
    public bool HasDoubleCounterValue {
      get { return counterValueFieldCase_ == CounterValueFieldOneofCase.DoubleCounterValue; }
    }

    public void ClearCounterValueField() {
      counterValueFieldCase_ = CounterValueFieldOneofCase.None;
      counterValueField_ = null;
    }
";
    }

    private static string BuildMixedTypeOneof()
    {
        return @"
    private object nameField_;
    public enum NameFieldOneofCase {
      None = 0,
      NameIid = 10,
      Name = 23,
    }
    private NameFieldOneofCase nameFieldCase_ = NameFieldOneofCase.None;

    public ulong NameIid {
      get { return HasNameIid ? (ulong) nameField_ : 0UL; }
      set {
        nameField_ = value;
        nameFieldCase_ = NameFieldOneofCase.NameIid;
      }
    }
    public bool HasNameIid {
      get { return nameFieldCase_ == NameFieldOneofCase.NameIid; }
    }

    public string Name {
      get { return HasName ? (string) nameField_ : """"; }
      set {
        nameField_ = pb::ProtoPreconditions.CheckNotNull(value, ""value"");
        nameFieldCase_ = NameFieldOneofCase.Name;
      }
    }
    public bool HasName {
      get { return nameFieldCase_ == NameFieldOneofCase.Name; }
    }

    public void ClearNameField() {
      nameFieldCase_ = NameFieldOneofCase.None;
      nameField_ = null;
    }
";
    }

    private static string BuildComplexMixedOneof()
    {
        return @"
    private object value_;
    public enum ValueOneofCase {
      None = 0,
      BoolValue = 2,
      UintValue = 3,
      IntValue = 4,
      DoubleValue = 5,
      StringValue = 6,
    }
    private ValueOneofCase valueCase_ = ValueOneofCase.None;

    public bool BoolValue {
      get { return HasBoolValue ? (bool) value_ : false; }
      set {
        value_ = value;
        valueCase_ = ValueOneofCase.BoolValue;
      }
    }
    public bool HasBoolValue {
      get { return valueCase_ == ValueOneofCase.BoolValue; }
    }

    public ulong UintValue {
      get { return HasUintValue ? (ulong) value_ : 0UL; }
      set {
        value_ = value;
        valueCase_ = ValueOneofCase.UintValue;
      }
    }
    public bool HasUintValue {
      get { return valueCase_ == ValueOneofCase.UintValue; }
    }

    public long IntValue {
      get { return HasIntValue ? (long) value_ : 0L; }
      set {
        value_ = value;
        valueCase_ = ValueOneofCase.IntValue;
      }
    }
    public bool HasIntValue {
      get { return valueCase_ == ValueOneofCase.IntValue; }
    }

    public double DoubleValue {
      get { return HasDoubleValue ? (double) value_ : 0D; }
      set {
        value_ = value;
        valueCase_ = ValueOneofCase.DoubleValue;
      }
    }
    public bool HasDoubleValue {
      get { return valueCase_ == ValueOneofCase.DoubleValue; }
    }

    public string StringValue {
      get { return HasStringValue ? (string) value_ : """"; }
      set {
        value_ = pb::ProtoPreconditions.CheckNotNull(value, ""value"");
        valueCase_ = ValueOneofCase.StringValue;
      }
    }
    public bool HasStringValue {
      get { return valueCase_ == ValueOneofCase.StringValue; }
    }

    public void ClearValue() {
      valueCase_ = ValueOneofCase.None;
      value_ = null;
    }
";
    }

    private static string BuildReferenceOnlyOneof()
    {
        return @"
    private object staticOrDynamicName_;
    public enum StaticOrDynamicNameOneofCase {
      None = 0,
      Name = 2,
      StaticName = 10,
    }
    private StaticOrDynamicNameOneofCase staticOrDynamicNameCase_ = StaticOrDynamicNameOneofCase.None;

    public string Name {
      get { return HasName ? (string) staticOrDynamicName_ : """"; }
      set {
        staticOrDynamicName_ = pb::ProtoPreconditions.CheckNotNull(value, ""value"");
        staticOrDynamicNameCase_ = StaticOrDynamicNameOneofCase.Name;
      }
    }
    public bool HasName {
      get { return staticOrDynamicNameCase_ == StaticOrDynamicNameOneofCase.Name; }
    }

    public string StaticName {
      get { return HasStaticName ? (string) staticOrDynamicName_ : """"; }
      set {
        staticOrDynamicName_ = pb::ProtoPreconditions.CheckNotNull(value, ""value"");
        staticOrDynamicNameCase_ = StaticOrDynamicNameOneofCase.StaticName;
      }
    }
    public bool HasStaticName {
      get { return staticOrDynamicNameCase_ == StaticOrDynamicNameOneofCase.StaticName; }
    }

    public void ClearStaticOrDynamicName() {
      staticOrDynamicNameCase_ = StaticOrDynamicNameOneofCase.None;
      staticOrDynamicName_ = null;
    }
";
    }

    private static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(search, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += search.Length;
        }
        return count;
    }

    #endregion
}
