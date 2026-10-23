using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;

namespace Groundwork.Cad;

public sealed record SourceInfo(string FileName, string Sha256, string DwgVersion, string Units);
public sealed record LayerInfo(string Id, string Handle, string Name, bool IsOn, string Flags, string Color, string LineType);
public sealed record BlockInfo(string Id, string Name, double[] BasePoint, string Units, string Flags, string? XRefPath, string[] EntityIds);
public sealed record Classification(string Status = "unclassified", string? ClassName = null, string? Source = null);
public sealed record CadEntity(string Id, string Handle, string OwnerBlockId, string LayerId,
    string EntityType, bool IsInvisible, string GeometryStatus, object? Geometry, Classification Classification);
public sealed record PageInfo(string Id, string Name, int Order, string Kind, string SegmentationStatus,
    string[] RootEntityIds, string[] ReferencedLayerIds, string VisibilityStatus);
public sealed record CadIndex(string SchemaVersion, SourceInfo Source, LayerInfo[] Layers,
    BlockInfo[] Blocks, CadEntity[] Entities, PageInfo[] Pages, string[] Warnings);

public static class CadExtractor
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static CadIndex Read(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".dwg", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only .dwg input is supported.");
        // Hash and parse the same locked stream, so IDs identify the bytes actually read.
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        stream.Position = 0;
        var warnings = new List<string>();
        using var reader = new DwgReader(stream);
        reader.OnNotification += (_, e) => warnings.Add(e.Message);
        var document = reader.Read();
        return Extract(document, Path.GetFileName(path), hash, warnings);
    }

    public static CadIndex Extract(CadDocument document, string fileName, string hash, List<string>? warnings = null)
    {
        warnings ??= [];
        string Id(string kind, ulong handle) => $"{hash}:{kind}:{handle:X}";
        var layers = document.Layers.Select(l => new LayerInfo(Id("layer", l.Handle), l.Handle.ToString("X"),
            l.Name, l.IsOn, l.Flags.ToString(), l.Color.ToString(), l.LineType.Name)).ToArray();
        var entities = new List<CadEntity>();
        var blocks = new List<BlockInfo>();
        foreach (var block in document.BlockRecords)
        {
            var blockId = Id("block", block.Handle);
            var ids = new List<string>();
            foreach (var entity in block.Entities)
            {
                var id = Id("entity", entity.Handle);
                ids.Add(id);
                var geometry = Geometry(entity, h => Id("block", h));
                if (geometry is null)
                    warnings.Add($"Entity {entity.Handle:X} ({entity.ObjectName}): geometry not exported in schema 0.1.");
                entities.Add(new CadEntity(id, entity.Handle.ToString("X"), blockId,
                    Id("layer", entity.Layer.Handle), entity.ObjectName, entity.IsInvisible,
                    geometry is null ? "unsupported" : "parameters", geometry, new Classification()));
            }
            blocks.Add(new BlockInfo(blockId, block.Name, V(block.BlockEntity.BasePoint), block.Units.ToString(), block.BlockEntity.Flags.ToString(), block.BlockEntity.XRefPath, ids.ToArray()));
        }
        // Collect layers through block references without flattening/duplicating definitions.
        string[] ReferencedLayers(ACadSharp.Tables.BlockRecord root)
        {
            var seen = new HashSet<ulong>();
            var used = new HashSet<string>();
            void Visit(ACadSharp.Tables.BlockRecord block)
            {
                if (!seen.Add(block.Handle)) return;
                foreach (var e in block.Entities)
                {
                    used.Add(Id("layer", e.Layer.Handle));
                    if (e is Insert insert) Visit(insert.Block);
                }
            }
            Visit(root);
            return used.Order().ToArray();
        }
        var pages = document.Layouts.OrderBy(l => l.TabOrder).Select(l => new PageInfo(
            Id("layout", l.Handle), l.Name, l.TabOrder, l.IsPaperSpace ? "paperSpace" : "modelSpace",
            l.IsPaperSpace ? "layout" : "requiresDrawingBoundaryReview",
            l.AssociatedBlock.Entities.Select(e => Id("entity", e.Handle)).ToArray(),
            ReferencedLayers(l.AssociatedBlock), "notEvaluated")).ToArray();
        warnings.Add("Page layer references are structural, not visible-layer results. Viewport clipping, frozen layers and layer-0 inheritance are not evaluated.");
        warnings.Add("Model space is not automatically split into drawings. Native coordinates and units are preserved; no site alignment or dimensional inference is performed.");
        return new CadIndex("0.1.0", new SourceInfo(fileName, hash, document.Header.Version.ToString(),
            document.Header.InsUnits.ToString()), layers, blocks.ToArray(), entities.ToArray(), pages,
            warnings.Distinct().ToArray());
    }

    static double[] V(XYZ p) => [p.X, p.Y, p.Z];
    // Coordinates retain each DWG entity's native WCS/OCS semantics. Angles are radians.
    static object? Geometry(Entity e, Func<ulong, string> blockId) => e switch
    {
        Line line => new { start = V(line.StartPoint), end = V(line.EndPoint), normal = V(line.Normal), line.Thickness },
        Arc arc => new { center = V(arc.Center), arc.Radius, arc.StartAngle, arc.EndAngle, normal = V(arc.Normal), arc.Thickness },
        Circle circle => new { center = V(circle.Center), circle.Radius, normal = V(circle.Normal), circle.Thickness },
        LwPolyline poly => new { poly.IsClosed, poly.Elevation, poly.Thickness, poly.ConstantWidth,
            normal = V(poly.Normal), vertices = poly.Vertices.Select(v => new { x = v.Location.X, y = v.Location.Y, v.Bulge, v.StartWidth, v.EndWidth }).ToArray() },
        Insert insert => new { blockId = blockId(insert.Block.Handle), point = V(insert.InsertPoint),
            scale = new[] { insert.XScale, insert.YScale, insert.ZScale }, insert.Rotation, normal = V(insert.Normal),
            insert.RowCount, insert.ColumnCount, insert.RowSpacing, insert.ColumnSpacing,
            attributes = insert.Attributes.Select(a => new { a.Tag, a.Value, point = V(a.InsertPoint) }).ToArray() },
        TextEntity text => new { text.Value, point = V(text.InsertPoint), text.Height, text.Rotation, normal = V(text.Normal) },
        MText text => new { text.Value, text.PlainText, point = V(text.InsertPoint), text.Height, text.Rotation, normal = V(text.Normal) },
        _ => null
    };
}
