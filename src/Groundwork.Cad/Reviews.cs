using System.Text.Json;

namespace Groundwork.Cad;

// Separate from source CAD records: user interpretation must not replace source facts.
public sealed record DimensionSpec(double Value, string Unit, double? Tolerance = null);
public sealed record ObjectReview(string EntityId, string ClassName, bool Confirmed,
    Dictionary<string, DimensionSpec> Dimensions);
public sealed record ReviewSet(string SchemaVersion, string SourceSha256, ObjectReview[] Objects);

public static class ReviewValidator
{
    public static void Validate(CadIndex index, ReviewSet review)
    {
        if (review.SchemaVersion != "0.1.0") throw new ArgumentException("Unsupported review schema version.");
        if (review.SourceSha256 != index.Source.Sha256) throw new ArgumentException("Review belongs to a different DWG revision.");
        if (review.Objects is null) throw new ArgumentException("Objects is required.");
        var ids = index.Entities.Select(e => e.Id).ToHashSet();
        var seen = new HashSet<string>();
        foreach (var item in review.Objects)
        {
            if (item is null || !ids.Contains(item.EntityId)) throw new ArgumentException("Unknown review entity ID.");
            if (!seen.Add(item.EntityId)) throw new ArgumentException("Duplicate review entity ID.");
            if (string.IsNullOrWhiteSpace(item.ClassName)) throw new ArgumentException("Class name is required.");
            if (item.Dimensions is null) throw new ArgumentException("Dimensions is required (may be empty).");
            foreach (var (name, spec) in item.Dimensions)
            {
                if (string.IsNullOrWhiteSpace(name) || spec is null || !double.IsFinite(spec.Value) || spec.Value <= 0)
                    throw new ArgumentException("Dimension name and positive finite value are required.");
                if (spec.Unit is not ("mm" or "cm" or "m" or "in" or "ft"))
                    throw new ArgumentException("Dimension unit must be mm, cm, m, in, or ft.");
                if (spec.Tolerance is double t && (!double.IsFinite(t) || t < 0))
                    throw new ArgumentException("Tolerance must be a finite non-negative value in the dimension unit.");
            }
        }
    }

    public static ReviewSet CreateTemplate(CadIndex index) => new("0.1.0", index.Source.Sha256, []);
}
