using System.Text.Json;
using Groundwork.Cad;

const string usage = """
Usage:
  Groundwork.Cad inspect <input.dwg> <output.json>
  Groundwork.Cad review-template <index.json> <review.json>
  Groundwork.Cad validate-review <index.json> <review.json>
Output files must not already exist. Input DWG is never modified.
""";
if (args.Length == 1 && args[0] is "--help" or "-h") { Console.WriteLine(usage); return 0; }
if (args.Length != 3) { Console.Error.WriteLine(usage); return 2; }
try
{
    switch (args[0])
    {
        case "inspect":
            var index = CadExtractor.Read(args[1]);
            WriteNew(args[2], index);
            Console.WriteLine($"Extracted {index.Pages.Length} spaces, {index.Layers.Length} layers, {index.Entities.Length} entity definitions.");
            Console.WriteLine($"Warnings: {index.Warnings.Length}. Output: {Path.GetFullPath(args[2])}");
            break;
        case "review-template":
            WriteNew(args[2], ReviewValidator.CreateTemplate(Read<CadIndex>(args[1])));
            Console.WriteLine($"Review template: {Path.GetFullPath(args[2])}");
            break;
        case "validate-review":
            ReviewValidator.Validate(Read<CadIndex>(args[1]), Read<ReviewSet>(args[2]));
            Console.WriteLine("Review is valid. No geometry or site compliance assessment has been performed.");
            break;
        default:
            Console.Error.WriteLine(usage); return 2;
    }
    return 0;
}
catch (Exception e) { Console.Error.WriteLine($"Operation failed: {e.Message}"); return 1; }

static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), CadExtractor.JsonOptions)
    ?? throw new ArgumentException("JSON document is empty.");
static void WriteNew<T>(string path, T value)
{
    var json = JsonSerializer.Serialize(value, CadExtractor.JsonOptions);
    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
    using var writer = new StreamWriter(stream);
    writer.Write(json);
}
