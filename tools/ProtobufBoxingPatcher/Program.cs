using System.Text.RegularExpressions;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: ProtobufBoxingPatcher <directory>");
    Console.Error.WriteLine("Patches protobuf-generated C# files to eliminate boxing in oneof fields.");
    return 1;
}

var directory = args[0];
if (!Directory.Exists(directory))
{
    Console.Error.WriteLine($"Directory not found: {directory}");
    return 1;
}

var patcher = new ProtobufBoxingPatcher();
var totalPatched = 0;

foreach (var file in Directory.GetFiles(directory, "*.cs"))
{
    if (patcher.PatchFile(file))
        totalPatched++;
}

Console.WriteLine($"Done. Patched {totalPatched} file(s).");
return 0;
