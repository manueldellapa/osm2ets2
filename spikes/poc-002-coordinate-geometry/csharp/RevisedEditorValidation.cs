using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using TruckLib.ScsMap;

namespace Poc002.Adapter;

internal static class RevisedEditorValidation
{
    private const string ExpectedCriteriaId = "poc-002-q256-rerun-v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static int Run(
        string aggregateReportArgument,
        string preEditorAdapterArgument,
        string savedMapRootArgument,
        string outputReportArgument)
    {
        var aggregateReportPath = Path.GetFullPath(aggregateReportArgument);
        var preEditorAdapterPath = Path.GetFullPath(preEditorAdapterArgument);
        var savedMapRoot = Path.GetFullPath(savedMapRootArgument);
        var outputReportPath = Path.GetFullPath(outputReportArgument);
        Require(File.Exists(aggregateReportPath),
            $"Aggregate automatic PASS report does not exist: {aggregateReportPath}");
        Require(File.Exists(preEditorAdapterPath),
            $"Pre-editor adapter v2 report does not exist: {preEditorAdapterPath}");
        Require(Directory.Exists(savedMapRoot), $"Saved-map root does not exist: {savedMapRoot}");
        var preEditorAdapterBytes = File.ReadAllBytes(preEditorAdapterPath);
        using var aggregate = JsonDocument.Parse(File.ReadAllBytes(aggregateReportPath));
        var aggregateRoot = aggregate.RootElement;
        Require(aggregateRoot.GetProperty("schemaVersion").GetInt32() == 1,
            "Aggregate automatic report is not schemaVersion 1.");
        Require(RequiredString(aggregateRoot, "criteriaId") == ExpectedCriteriaId,
            $"Aggregate automatic report criteriaId must be {ExpectedCriteriaId}.");
        Require(RequiredString(aggregateRoot, "comparisonValidation") == "PASS",
            "Aggregate automatic comparison validation is not PASS.");
        Require(RequiredString(aggregateRoot, "rerunState") == "AWAITING_MANUAL_VALIDATION",
            "Aggregate automatic report is not awaiting manual validation.");
        var preEditorAdapterSha256 = Sha256(preEditorAdapterBytes);
        var matchedGenerationId = FindValidatedGeneration(
            aggregateRoot,
            preEditorAdapterSha256);
        Require(matchedGenerationId is not null,
            "Pre-editor adapter SHA-256 is not one of the aggregate report's two validated generations.");
        using var adapter = JsonDocument.Parse(preEditorAdapterBytes);
        var root = adapter.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 2,
            "Pre-editor adapter report is not schemaVersion 2.");
        Require(RequiredString(root, "criteriaId") == ExpectedCriteriaId,
            $"Pre-editor adapter criteriaId must be {ExpectedCriteriaId}.");
        Require(RequiredString(root, "generationAutomaticValidation") == "PASS",
            "Pre-editor generation automatic validation is not PASS.");

        var failures = new List<string>();
        var mapExpectations = root.GetProperty("maps")
            .EnumerateArray()
            .ToDictionary(map => RequiredString(map, "id"), map => map.Clone(), StringComparer.Ordinal);
        var qRoadsByMap = root.GetProperty("q256Roads")
            .EnumerateArray()
            .GroupBy(road => RequiredString(road, "mapId"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(value => value.Clone()).ToArray(), StringComparer.Ordinal);
        var mapReports = new List<RevisedEditorMapReport>();
        var comparisons = new List<RevisedEditorAxisComparison>();

        foreach (var (mapId, expectedMap) in mapExpectations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            ValidateSafeId(mapId);
            var mbdPath = Path.Combine(savedMapRoot, "maps", mapId, $"{mapId}.mbd");
            Require(File.Exists(mbdPath), $"Editor-saved map is missing: {mbdPath}");
            var expectedFormat = expectedMap.GetProperty("mapFormat").GetUInt32();
            var actualFormat = ReadMapFormat(mbdPath);
            var map = Map.Open(mbdPath);
            var expectedRoadCount = expectedMap.GetProperty("roadCount").GetInt32();
            var expectedNodeCount = expectedMap.GetProperty("nodeCount").GetInt32();
            var expectedSectorCount = expectedMap.GetProperty("sectorCountObserved").GetInt32();
            var actualRoadCount = map.MapItems.Values.OfType<Road>().Count();

            AddEqualityFailure(failures, mapId, "map format", expectedFormat, actualFormat);
            AddEqualityFailure(failures, mapId, "road count", expectedRoadCount, actualRoadCount);
            AddEqualityFailure(failures, mapId, "node count", expectedNodeCount, map.Nodes.Count);
            AddEqualityFailure(failures, mapId, "sector count", expectedSectorCount, map.Sectors.Count);
            if (!qRoadsByMap.TryGetValue(mapId, out var qRoads))
            {
                failures.Add($"Map '{mapId}' has no pre-editor Q256 road evidence.");
                qRoads = [];
            }

            foreach (var road in qRoads.OrderBy(value => RequiredString(value, "roadId"), StringComparer.Ordinal))
            {
                var roadId = RequiredString(road, "roadId");
                foreach (var endpointName in new[] { "backward", "forward" })
                {
                    var endpoint = road.GetProperty(endpointName);
                    var nodeUidText = RequiredString(endpoint, "nodeUid");
                    var nodeUid = ParseUid(nodeUidText);
                    var nodeFound = map.Nodes.TryGetValue(nodeUid, out var node);
                    if (!nodeFound || node is null)
                    {
                        failures.Add(
                            $"Map '{mapId}' road '{roadId}' {endpointName} node {nodeUidText} is absent after editor save.");
                    }

                    foreach (var axis in endpoint.GetProperty("axes").EnumerateArray())
                    {
                        var axisName = RequiredString(axis, "axis");
                        var expectedQ = axis.GetProperty("expectedQ").GetInt32();
                        var writtenQ = axis.GetProperty("writtenQ").GetInt32();
                        var beforeQ = axis.GetProperty("readbackQ").GetInt32();
                        if (expectedQ != writtenQ || expectedQ != beforeQ)
                        {
                            failures.Add(
                                $"Pre-editor Q256 evidence mismatch for '{mapId}/{roadId}' "
                                + $"{endpointName}/{axisName}: expected={expectedQ}, written={writtenQ}, before={beforeQ}.");
                        }

                        int? afterQ = null;
                        int? delta = null;
                        float? afterNative = null;
                        var exact = false;
                        if (node is not null)
                        {
                            afterNative = AxisValue(node.Position, axisName);
                            if (TryReconstructQ256(afterNative.Value, out var reconstructed))
                            {
                                afterQ = reconstructed;
                                delta = reconstructed - beforeQ;
                                exact = reconstructed == beforeQ && reconstructed == expectedQ;
                                if (!exact)
                                {
                                    failures.Add(
                                        $"Post-editor Q256 delta for '{mapId}/{roadId}' {endpointName}/{axisName}: "
                                        + $"expected={expectedQ}, before={beforeQ}, after={reconstructed}, delta={delta}.");
                                }
                            }
                            else
                            {
                                failures.Add(
                                    $"Post-editor coordinate for '{mapId}/{roadId}' {endpointName}/{axisName} "
                                    + "is not a finite exact Q256 value.");
                            }
                        }

                        comparisons.Add(new RevisedEditorAxisComparison(
                            MapId: mapId,
                            RoadId: roadId,
                            Endpoint: endpointName,
                            NodeUid: nodeUidText,
                            Axis: axisName,
                            ExpectedQ: expectedQ,
                            WrittenQ: writtenQ,
                            BeforeQ: beforeQ,
                            AfterQ: afterQ,
                            DeltaQ: delta,
                            AfterNativeAxis: afterNative,
                            ExactQ256Identity: exact));
                    }
                }
            }

            mapReports.Add(new RevisedEditorMapReport(
                Id: mapId,
                MbdPath: mbdPath,
                ExpectedFormat: expectedFormat,
                ActualFormat: actualFormat,
                ExpectedRoadCount: expectedRoadCount,
                ActualRoadCount: actualRoadCount,
                ExpectedNodeCount: expectedNodeCount,
                ActualNodeCount: map.Nodes.Count,
                ExpectedSectorCount: expectedSectorCount,
                ActualSectorCount: map.Sectors.Count));
        }

        var expectedAxisCount = root.GetProperty("q256").GetProperty("generatedAxisCount").GetInt32();
        if (comparisons.Count != expectedAxisCount)
        {
            failures.Add(
                $"Post-editor axis comparison count is {comparisons.Count}, expected {expectedAxisCount}.");
        }
        var passed = failures.Count == 0;
        var report = new RevisedEditorValidationReport(
            SchemaVersion: 1,
            CriteriaId: ExpectedCriteriaId,
            Poc: "PoC-002 — Coordinate and Geometry Validation",
            HistoricalV1Status: "FAIL",
            NumericQ256PersistenceValidation: passed ? "PASS" : "FAIL",
            GateStatus: passed ? "AWAITING_MANUAL_VALIDATION" : "FAIL",
            ManualCycleAssertion: "NOT_PROVEN_BY_THIS_DIAGNOSTIC",
            ImportantLimitation:
                "TruckLib readback cannot prove that the required Map Editor open/inspect/Recompute/save/complete-close/reopen cycle occurred and cannot validate visual geographic axis semantics.",
            AggregateAutomaticReport: Evidence(aggregateReportPath),
            ValidatedGenerationId: matchedGenerationId!,
            PreEditorAdapter: Evidence(preEditorAdapterPath),
            SavedMapRoot: savedMapRoot,
            ExpectedAxisComparisonCount: expectedAxisCount,
            CompletedAxisComparisonCount: comparisons.Count,
            ExactAxisIdentityCount: comparisons.Count(item => item.ExactQ256Identity),
            Failures: failures,
            Maps: mapReports,
            Axes: comparisons,
            ReadFiles: InventoryFiles(savedMapRoot),
            VisualAxisSemanticStatus: "PENDING_MANUAL_MAP_EDITOR_VALIDATION");
        var parent = Path.GetDirectoryName(outputReportPath)
            ?? throw new InvalidOperationException($"Output report has no parent: {outputReportPath}");
        Directory.CreateDirectory(parent);
        File.WriteAllText(outputReportPath, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);

        Console.WriteLine(passed
            ? "REVISED_EDITOR_Q256_READBACK_PASSED"
            : "REVISED_EDITOR_Q256_READBACK_FAILED");
        Console.WriteLine($"Gate status: {report.GateStatus}");
        Console.WriteLine($"Exact Q256 axes: {report.ExactAxisIdentityCount}/{expectedAxisCount}");
        Console.WriteLine($"Report: {outputReportPath}");
        return passed ? 0 : 2;
    }

    internal static void RunSelfTest()
    {
        const string aggregateJson = """
            {
              "generationA": {
                "generationId": "generation-a",
                "generationAutomaticValidation": "PASS",
                "adapterReport": { "sha256": "aaaaaaaa" }
              },
              "generationB": {
                "generationId": "generation-b",
                "generationAutomaticValidation": "PASS",
                "adapterReport": { "sha256": "bbbbbbbb" }
              }
            }
            """;
        using var aggregate = JsonDocument.Parse(aggregateJson);
        Require(
            FindValidatedGeneration(aggregate.RootElement, "bbbbbbbb") == "generation-b",
            "Aggregate adapter hash did not resolve to the validated generation.");
        Require(
            FindValidatedGeneration(aggregate.RootElement, "cccccccc") is null,
            "Unknown adapter hash resolved to a validated generation.");
    }

    private static string? FindValidatedGeneration(JsonElement aggregateRoot, string adapterSha256)
    {
        foreach (var propertyName in new[] { "generationA", "generationB" })
        {
            var generation = aggregateRoot.GetProperty(propertyName);
            Require(
                RequiredString(generation, "generationAutomaticValidation") == "PASS",
                $"Aggregate {propertyName} validation is not PASS.");
            var adapterReport = generation.GetProperty("adapterReport");
            if (RequiredString(adapterReport, "sha256") == adapterSha256)
            {
                return RequiredString(generation, "generationId");
            }
        }

        return null;
    }

    private static bool TryReconstructQ256(float value, out int code)
    {
        var scaled = value * QuantizerRca.FixedPointFactor;
        if (!float.IsFinite(scaled)
            || scaled != MathF.Truncate(scaled)
            || scaled < int.MinValue
            || scaled > int.MaxValue)
        {
            code = 0;
            return false;
        }

        code = (int)scaled;
        return value.Equals(code / QuantizerRca.FixedPointFactor);
    }

    private static float AxisValue(Vector3 value, string axis) => axis switch
    {
        "X" => value.X,
        "Y" => value.Y,
        "Z" => value.Z,
        _ => throw new InvalidDataException($"Unexpected axis '{axis}'."),
    };

    private static ulong ParseUid(string value)
    {
        if (!value.StartsWith("0x", StringComparison.Ordinal)
            || !ulong.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var uid))
        {
            throw new InvalidDataException($"Invalid hexadecimal node UID '{value}'.");
        }

        return uid;
    }

    private static void ValidateSafeId(string value)
    {
        if (value.Length is < 1 or > 64
            || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new InvalidDataException($"Unsafe map id '{value}' in pre-editor evidence.");
        }
    }

    private static void AddEqualityFailure<T>(
        List<string> failures,
        string mapId,
        string field,
        T expected,
        T actual)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
        {
            failures.Add($"Map '{mapId}' {field} is {actual}, expected {expected}.");
        }
    }

    private static uint ReadMapFormat(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        return reader.ReadUInt32();
    }

    private static string RequiredString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"Missing required string property '{propertyName}'.");

    private static EvidenceReport Evidence(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new EvidenceReport(path, Sha256(bytes), bytes.LongLength);
    }

    private static List<FileEvidence> InventoryFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                var bytes = File.ReadAllBytes(path);
                return new FileEvidence(
                    Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                    bytes.LongLength,
                    Sha256(bytes));
            })
            .ToList();

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record RevisedEditorValidationReport(
        int SchemaVersion,
        string CriteriaId,
        string Poc,
        string HistoricalV1Status,
        string NumericQ256PersistenceValidation,
        string GateStatus,
        string ManualCycleAssertion,
        string ImportantLimitation,
        EvidenceReport AggregateAutomaticReport,
        string ValidatedGenerationId,
        EvidenceReport PreEditorAdapter,
        string SavedMapRoot,
        int ExpectedAxisComparisonCount,
        int CompletedAxisComparisonCount,
        int ExactAxisIdentityCount,
        List<string> Failures,
        List<RevisedEditorMapReport> Maps,
        List<RevisedEditorAxisComparison> Axes,
        List<FileEvidence> ReadFiles,
        string VisualAxisSemanticStatus);

    private sealed record RevisedEditorMapReport(
        string Id,
        string MbdPath,
        uint ExpectedFormat,
        uint ActualFormat,
        int ExpectedRoadCount,
        int ActualRoadCount,
        int ExpectedNodeCount,
        int ActualNodeCount,
        int ExpectedSectorCount,
        int ActualSectorCount);

    private sealed record RevisedEditorAxisComparison(
        string MapId,
        string RoadId,
        string Endpoint,
        string NodeUid,
        string Axis,
        int ExpectedQ,
        int WrittenQ,
        int BeforeQ,
        int? AfterQ,
        int? DeltaQ,
        float? AfterNativeAxis,
        bool ExactQ256Identity);

    private sealed record EvidenceReport(string Path, string Sha256, long Bytes);

    private sealed record FileEvidence(string Path, long Bytes, string Sha256);
}
