using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace frankbiz_demo.SyncConsole;

public sealed record BrickClassEntry(string Label, string Uri);
public sealed record BrickClassStore(IReadOnlyList<BrickClassEntry> EquipmentClasses, IReadOnlyList<BrickClassEntry> SensorClasses);
public sealed record BrickSyncResponse(int EquipmentCount, int SensorCount);

public sealed record QudtUnitEntry(string Label, string Uri);
public sealed record QudtSyncResponse(int UnitCount);

public sealed class SyncApiClient(HttpClient httpClient)
{
    private const string BrickTtlUrl = "https://github.com/BrickSchema/Brick/releases/latest/download/Brick.ttl";
    private const string QudtTtlUrl = "https://qudt.org/2.1/vocab/unit";
    private const string BrickPrefix = "https://brickschema.org/schema/Brick#";
    private const string QudtUnitPrefix = "http://qudt.org/vocab/unit/";

    public async Task<(BrickSyncResponse Result, string Json)> SyncBrickClassesAsync(CancellationToken cancellationToken = default)
    {
        var ttl = await httpClient.GetStringAsync(BrickTtlUrl, cancellationToken);
        var store = ParseBrickTtl(ttl);
        var json = JsonSerializer.Serialize(new
        {
            equipment = store.EquipmentClasses.Select(e => new { e.Label, e.Uri }),
            sensor = store.SensorClasses.Select(e => new { e.Label, e.Uri })
        }, new JsonSerializerOptions { WriteIndented = true });

        var result = new BrickSyncResponse(store.EquipmentClasses.Count, store.SensorClasses.Count);
        return (result, json);
    }

    public async Task<(QudtSyncResponse Result, string Json)> SyncQudtUnitsAsync(CancellationToken cancellationToken = default)
    {
        var ttl = await httpClient.GetStringAsync(QudtTtlUrl, cancellationToken);
        var units = ParseQudtTtl(ttl);
        var json = JsonSerializer.Serialize(
            units.Select(u => new { u.Label, u.Uri }),
            new JsonSerializerOptions { WriteIndented = true });

        var result = new QudtSyncResponse(units.Count);
        return (result, json);
    }

    internal static BrickClassStore ParseBrickTtl(string ttl)
    {
        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentSubject = null;

        foreach (var rawLine in ttl.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#') || string.IsNullOrEmpty(line)) continue;

            var subjectMatch = Regex.Match(line, @"^brick:(\w+)\s+a\s+");
            if (subjectMatch.Success)
            {
                currentSubject = subjectMatch.Groups[1].Value;
            }

            if (line.EndsWith('.'))
            {
                currentSubject = null;
            }

            if (currentSubject is not null)
            {
                var scMatch = Regex.Match(line, @"rdfs:subClassOf\s+brick:(\w+)");
                if (scMatch.Success)
                {
                    parentOf.TryAdd(currentSubject, scMatch.Groups[1].Value);
                }
            }
        }

        static IReadOnlyList<BrickClassEntry> Descendants(
            string root,
            Dictionary<string, string> parent,
            string brickPrefix)
        {
            var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (child, par) in parent)
            {
                if (!children.TryGetValue(par, out var list))
                {
                    list = [];
                    children[par] = list;
                }

                list.Add(child);
            }

            var result = new List<BrickClassEntry>();
            var queue = new Queue<string>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current != root)
                {
                    var label = current.Replace('_', ' ');
                    result.Add(new BrickClassEntry(label, brickPrefix + current));
                }

                if (children.TryGetValue(current, out var kids))
                {
                    foreach (var kid in kids) queue.Enqueue(kid);
                }
            }

            return [.. result.OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)];
        }

        return new BrickClassStore(
            Descendants("Equipment", parentOf, BrickPrefix),
            Descendants("Point", parentOf, BrickPrefix));
    }

    internal static IReadOnlyList<QudtUnitEntry> ParseQudtTtl(string ttl)
    {
        var unitNames = new HashSet<string>(StringComparer.Ordinal);
        string? currentSubject = null;

        foreach (var rawLine in ttl.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#') || string.IsNullOrEmpty(line)) continue;

            var subjectMatch = Regex.Match(line, @"^(?:unit:)?(\w+)\s+a\s+");
            if (subjectMatch.Success && !line.StartsWith("@") && !line.StartsWith("owl:") && !line.StartsWith("qudt:") && !line.StartsWith("rdfs:"))
            {
                currentSubject = subjectMatch.Groups[1].Value;
            }

            if (line.EndsWith('.'))
            {
                currentSubject = null;
            }

            if (currentSubject is not null && Regex.IsMatch(line, @"\ba\s+qudt:Unit\b"))
            {
                unitNames.Add(currentSubject);
            }
        }

        return [.. unitNames
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new QudtUnitEntry(n, QudtUnitPrefix + n))];
    }
}
