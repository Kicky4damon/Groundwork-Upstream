using System.Text.Json;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using Groundwork.Cad;

var output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/tests");
Directory.CreateDirectory(output);
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}"); passed++;
}
void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { Check(true, name); return; }
    throw new Exception($"FAIL: {name} did not reject invalid data");
}
var doc = new CadDocument();
var layer = new Layer("구조_기둥");
doc.Layers.Add(layer);
var block = new BlockRecord("기둥_400");
block.Entities.Add(new Circle { Center = new XYZ(0, 0, 0), Radius = 200, Layer = layer });
doc.BlockRecords.Add(block);
doc.Entities.Add(new Insert(block) { InsertPoint = new XYZ(1000, 2000, 0), XScale = 2 });
doc.Entities.Add(new Insert(block) { InsertPoint = new XYZ(3000, 2000, 0) });
doc.Entities.Add(new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(1000, 0, 0), Layer = layer });
doc.PaperSpace.Entities.Add(new TextEntity { Value = "기초 평면도", Height = 10 });
var path = Path.Combine(output, "fixture.dwg");
DwgWriter.Write(path, doc);
var index = CadExtractor.Read(path);
Check(index.Layers.Any(l => l.Name == "구조_기둥"), "DWG roundtrip preserves Korean layer names");
Check(index.Pages.Any(p => p.Kind == "paperSpace") && index.Pages.Any(p => p.Kind == "modelSpace"), "paper and model spaces are separated");
Check(index.Pages.Single(p => p.Kind == "modelSpace").SegmentationStatus == "requiresDrawingBoundaryReview", "model space does not falsely claim drawing segmentation");
Check(index.Entities.Count(e => e.EntityType == "INSERT") == 2, "repeated block instances retain distinct identities");
Check(index.Entities.Count(e => e.EntityType == "CIRCLE") == 1, "block definition geometry is not duplicated");
Check(index.Entities.Select(e => e.Id).Distinct().Count() == index.Entities.Length, "entity IDs are unique");
var second = CadExtractor.Read(path);
Check(index.Entities.Select(e => e.Id).SequenceEqual(second.Entities.Select(e => e.Id)), "IDs are stable for identical source bytes");
var text = JsonSerializer.Serialize(index, CadExtractor.JsonOptions);
Check(text.Contains("1000") && text.Contains("2000"), "geometry parameters reach JSON");
var parsed = JsonSerializer.Deserialize<CadIndex>(text, CadExtractor.JsonOptions)!;
Check(parsed.Source.Sha256.Length == 64, "JSON contract roundtrips with source SHA256");
var review = new ReviewSet("0.1.0", index.Source.Sha256,
    [new(index.Entities[0].Id, "column", true, new() { ["width"] = new(400, "mm", 10) })]);
ReviewValidator.Validate(index, review);
Check(true, "valid user classification and size specification accepted");
Reject(() => ReviewValidator.Validate(index, review with { SourceSha256 = "wrong" }), "review from another DWG revision rejected");
Reject(() => ReviewValidator.Validate(index, review with { Objects = [review.Objects[0] with { EntityId = "unknown" }] }), "unknown entity rejected");
Reject(() => ReviewValidator.Validate(index, review with { Objects = [review.Objects[0], review.Objects[0]] }), "duplicate review rejected");
Reject(() => ReviewValidator.Validate(index, review with { Objects = [review.Objects[0] with { Dimensions = new() { ["width"] = new(400, "unknown") } }] }), "unknown measurement units rejected");
Reject(() => ReviewValidator.Validate(index, review with { Objects = [review.Objects[0] with { Dimensions = new() { ["width"] = new(400, "mm", -1) } }] }), "negative tolerance rejected");
File.WriteAllText(Path.Combine(output, "fixture.index.json"), text);
File.WriteAllText(Path.Combine(output, "fixture.review.json"), JsonSerializer.Serialize(review, CadExtractor.JsonOptions));
Console.WriteLine($"{passed} checks passed. Generated DWG fixture is synthetic, not a field validation.");
