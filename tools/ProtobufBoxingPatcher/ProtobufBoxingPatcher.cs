using System.Text.RegularExpressions;

public sealed class ProtobufBoxingPatcher
{
    private static readonly HashSet<string> ValueTypes = new()
    {
        "int", "uint", "long", "ulong", "float", "double", "bool",
        "byte", "sbyte", "short", "ushort", "char", "decimal"
    };

    private static readonly Dictionary<string, string> TypeSuffixes = new()
    {
        ["int"] = "Int32",
        ["uint"] = "UInt32",
        ["long"] = "Int64",
        ["ulong"] = "UInt64",
        ["float"] = "Single",
        ["double"] = "Double",
        ["bool"] = "Bool",
        ["byte"] = "Byte",
        ["sbyte"] = "SByte",
        ["short"] = "Int16",
        ["ushort"] = "UInt16",
    };

    public bool PatchFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var patched = PatchContent(content);

        if (patched == content)
            return false;

        File.WriteAllText(filePath, patched);
        var fileName = Path.GetFileName(filePath);
        Console.WriteLine($"Patched: {fileName}");

        var fields = AnalyzeFields(content);
        foreach (var field in fields)
        {
            PrintFieldInfo(field);
        }

        return true;
    }

    public string PatchContent(string content)
    {
        var fields = AnalyzeFields(content);

        foreach (var field in fields)
        {
            content = ApplyPatches(content, field);
        }

        return content;
    }

    public List<OneofFieldInfo> AnalyzeFields(string content)
    {
        var objectFieldPattern = new Regex(@"private object (\w+)_;");
        var matches = objectFieldPattern.Matches(content);
        var fields = new List<OneofFieldInfo>();

        foreach (Match match in matches)
        {
            var fieldName = match.Groups[1].Value;
            var info = AnalyzeOneofField(content, fieldName);
            if (info != null)
                fields.Add(info);
        }

        return fields;
    }

    private OneofFieldInfo? AnalyzeOneofField(string content, string fieldName)
    {
        var escapedField = Regex.Escape(fieldName);

        var getterPattern1 = new Regex(
            $@"Has(\w+)\s*\?\s*\(([^)]+)\)\s*{escapedField}_ :");
        var getterPattern2 = new Regex(
            $@"\w+Case_\s*==\s*\w+\.(\w+)\s*\?\s*\(([^)]+)\)\s*{escapedField}_ :");

        var members = new Dictionary<string, string>();

        foreach (Match m in getterPattern1.Matches(content))
            members.TryAdd(m.Groups[1].Value, m.Groups[2].Value.Trim());

        foreach (Match m in getterPattern2.Matches(content))
            members.TryAdd(m.Groups[1].Value, m.Groups[2].Value.Trim());

        if (members.Count == 0)
            return null;

        var valueTypeMembers = members.Where(kv => IsValueType(kv.Value)).ToList();
        var refTypeMembers = members.Where(kv => !IsValueType(kv.Value)).ToList();

        if (valueTypeMembers.Count == 0)
            return null;

        var distinctValueTypes = valueTypeMembers.Select(kv => kv.Value).Distinct().ToList();
        var hasRefTypes = refTypeMembers.Count > 0;

        OneofStrategy strategy;
        if (distinctValueTypes.Count == 1 && !hasRefTypes)
            strategy = OneofStrategy.SameValueType;
        else if (!hasRefTypes)
            strategy = OneofStrategy.MultiValueType;
        else
            strategy = OneofStrategy.MixedTypes;

        return new OneofFieldInfo
        {
            FieldName = fieldName,
            Strategy = strategy,
            Members = members,
            ValueTypeMembers = valueTypeMembers.ToDictionary(kv => kv.Key, kv => kv.Value),
            RefTypeMembers = refTypeMembers.ToDictionary(kv => kv.Key, kv => kv.Value),
            DistinctValueTypes = distinctValueTypes,
        };
    }

    private string ApplyPatches(string content, OneofFieldInfo field)
    {
        return field.Strategy switch
        {
            OneofStrategy.SameValueType => PatchSameValueType(content, field),
            OneofStrategy.MultiValueType => PatchMultiValueType(content, field),
            OneofStrategy.MixedTypes => PatchMixedTypes(content, field),
            _ => content,
        };
    }

    private string PatchSameValueType(string content, OneofFieldInfo field)
    {
        var type = field.DistinctValueTypes[0];
        var fn = field.FieldName;
        var escaped = Regex.Escape(fn);

        content = content.Replace(
            $"private object {fn}_;",
            $"private {type} {fn}_;");

        content = Regex.Replace(content,
            $@"\({Regex.Escape(type)}\)\s*{escaped}_",
            $"{fn}_");

        content = RemoveNullAssignment(content, fn);

        return content;
    }

    private string PatchMultiValueType(string content, OneofFieldInfo field)
    {
        var fn = field.FieldName;
        var escaped = Regex.Escape(fn);

        var indent = DetectIndent(content, $"private object {fn}_;");
        var newDecl = string.Join("\n",
            field.DistinctValueTypes.Select(t =>
                $"{indent}private {t} {fn}_{GetSuffix(t)}_;"));

        content = ReplaceLineContaining(content, $"private object {fn}_;", newDecl);

        foreach (var type in field.DistinctValueTypes)
        {
            var suffix = GetSuffix(type);
            content = Regex.Replace(content,
                $@"\({Regex.Escape(type)}\)\s*{escaped}_",
                $"{fn}_{suffix}_");
        }

        content = PatchSetters(content, field);
        content = RemoveNullAssignment(content, fn);

        return content;
    }

    private string PatchMixedTypes(string content, OneofFieldInfo field)
    {
        var fn = field.FieldName;
        var escaped = Regex.Escape(fn);

        var indent = DetectIndent(content, $"private object {fn}_;");
        var newFields = string.Join("\n",
            field.DistinctValueTypes.Select(t =>
                $"{indent}private {t} {fn}_{GetSuffix(t)}_;"));

        content = content.Replace(
            $"private object {fn}_;",
            $"private object {fn}_;\n{newFields}");

        foreach (var type in field.DistinctValueTypes)
        {
            var suffix = GetSuffix(type);
            content = Regex.Replace(content,
                $@"\({Regex.Escape(type)}\)\s*{escaped}_",
                $"{fn}_{suffix}_");
        }

        content = PatchSetters(content, field);

        return content;
    }

    private string PatchSetters(string content, OneofFieldInfo field)
    {
        var fn = field.FieldName;
        var escaped = Regex.Escape(fn);

        var setterPattern = new Regex(
            $@"({escaped}_)\s*=\s*([^;]+);\s*\r?\n(\s*)(\w+Case_\s*=\s*\w+\.)(\w+);");

        content = setterPattern.Replace(content, match =>
        {
            var memberName = match.Groups[5].Value;
            if (!field.ValueTypeMembers.TryGetValue(memberName, out var type))
                return match.Value;

            var suffix = GetSuffix(type);
            var valueExpr = match.Groups[2].Value;
            var indent = match.Groups[3].Value;
            var caseAssign = match.Groups[4].Value + match.Groups[5].Value;

            return $"{fn}_{suffix}_ = {valueExpr};\n{indent}{caseAssign};";
        });

        return content;
    }

    private static string RemoveNullAssignment(string content, string fieldName)
    {
        var pattern = new Regex($@"\r?\n[ \t]*{Regex.Escape(fieldName)}_ = null;");
        return pattern.Replace(content, "");
    }

    private static string ReplaceLineContaining(string content, string search, string replacement)
    {
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r').TrimEnd().Contains(search))
            {
                lines[i] = replacement;
                break;
            }
        }
        return string.Join('\n', lines);
    }

    private static string DetectIndent(string content, string search)
    {
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r').TrimEnd();
            if (trimmed.Contains(search))
            {
                var idx = line.IndexOf('p');
                return idx > 0 ? line[..idx] : "";
            }
        }
        return "    ";
    }

    private static bool IsValueType(string type) => ValueTypes.Contains(type);

    internal static string GetSuffix(string type) =>
        TypeSuffixes.TryGetValue(type, out var suffix) ? suffix : type;

    private static void PrintFieldInfo(OneofFieldInfo field)
    {
        Console.WriteLine($"  {field.FieldName}_ ({field.Strategy}):");
        foreach (var (member, type) in field.ValueTypeMembers)
        {
            Console.WriteLine($"    {member}: {type} -> {field.FieldName}_{GetSuffix(type)}_");
        }
        foreach (var (member, type) in field.RefTypeMembers)
        {
            Console.WriteLine($"    {member}: {type} (reference, unchanged)");
        }
    }
}

public enum OneofStrategy
{
    SameValueType,
    MultiValueType,
    MixedTypes,
}

public sealed class OneofFieldInfo
{
    public required string FieldName { get; init; }
    public required OneofStrategy Strategy { get; init; }
    public required Dictionary<string, string> Members { get; init; }
    public required Dictionary<string, string> ValueTypeMembers { get; init; }
    public required Dictionary<string, string> RefTypeMembers { get; init; }
    public required List<string> DistinctValueTypes { get; init; }
}
