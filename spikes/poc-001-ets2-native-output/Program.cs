using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using TruckLib.ScsMap;

const string mapName = "poc001_minimal";
const string roadType = "ger1";
const string roadLook = "ger_1";
const string roadVariant = "broken_de";
const string roadEdge = "ger_sh_15";
const uint expectedMapFormat = 907;

if (args.Length > 0 && args[0] == "--validate-editor-save")
{
    if (args.Length != 3)
    {
        throw new ArgumentException(
            "Usage: dotnet run -- --validate-editor-save <saved-mbd-path> <original-manifest-path>.");
    }

    ValidateEditorSavedMap(
        Path.GetFullPath(args[1]),
        Path.GetFullPath(args[2]),
        expectedMapFormat,
        roadType,
        roadLook,
        roadVariant,
        roadEdge);
    return;
}

var runId = args.Length switch
{
    0 => "run-current",
    1 when IsValidRunId(args[0]) => args[0],
    _ => throw new ArgumentException(
        "Usage: dotnet run [-- <run-id>], where run-id contains only letters, digits, '.', '_' or '-'."),
};

var projectDirectory = FindProjectDirectory();
var runDirectory = Path.Combine(projectDirectory, "output", runId);
var mapDirectory = Path.Combine(runDirectory, "map");
Directory.CreateDirectory(mapDirectory);

var backwardPosition = new Vector3(100, 0, 100);
var forwardPosition = new Vector3(200, 0, 100);

var map = new Map();
var road = Road.Add(
    map,
    backwardPosition,
    forwardPosition,
    roadType,
    leftTerrainSize: 0,
    rightTerrainSize: 0);

// These identifiers were observed in the base catalog of ETS2 1.60.1.7.
// For this single-carriageway template TruckLib's same-version sample sets
// carriageway properties on the right side.
road.Right.Look = roadLook;
road.Right.Variant = roadVariant;
road.Right.LeftEdge = roadEdge;
road.Right.RightEdge = roadEdge;

ValidateInMemoryMap(map, road, backwardPosition, forwardPosition);
map.Save(mapDirectory, mapName, cleanSectorDirectory: true);

var mbdPath = Path.Combine(mapDirectory, $"{mapName}.mbd");
var sectorDirectory = Path.Combine(mapDirectory, mapName);
ValidateGeneratedFiles(mbdPath, sectorDirectory, expectedMapFormat);

var reopenedMap = Map.Open(mbdPath);
var reopenedRoad = ValidateReopenedMap(
    reopenedMap,
    road.Uid,
    road.Node.Uid,
    road.ForwardNode.Uid,
    backwardPosition,
    forwardPosition,
    roadType,
    roadLook,
    roadVariant,
    roadEdge);

var truckLibAssembly = typeof(Map).Assembly;
var truckLibAssemblyVersion = truckLibAssembly.GetName().Version?.ToString()
    ?? throw new InvalidOperationException("TruckLib assembly version is unavailable.");
Require(truckLibAssemblyVersion == "0.5.1.0", $"Unexpected TruckLib assembly version {truckLibAssemblyVersion}.");

var truckLibInformationalVersion = truckLibAssembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
    .InformationalVersion;

var generatedFiles = Directory
    .EnumerateFiles(mapDirectory, "*", SearchOption.AllDirectories)
    .Order(StringComparer.Ordinal)
    .Select(path => new
    {
        path = Path.GetRelativePath(runDirectory, path).Replace(Path.DirectorySeparatorChar, '/'),
        bytes = new FileInfo(path).Length,
        sha256 = Sha256(path),
    })
    .ToArray();

var manifest = new
{
    schemaVersion = 1,
    poc = "PoC-001 — ETS2 Native Output Feasibility",
    gateStatus = "AWAITING_MANUAL_VALIDATION",
    automaticValidation = "PASSED",
    generatedAtUtc = DateTimeOffset.UtcNow,
    runId,
    environment = new
    {
        framework = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription,
        processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
        truckLibPackage = "0.5.1",
        truckLibAssemblyVersion,
        truckLibInformationalVersion,
        truckLibDeclaredMapFormat = expectedMapFormat,
    },
    map = new
    {
        name = mapName,
        mbd = Path.GetRelativePath(runDirectory, mbdPath).Replace(Path.DirectorySeparatorChar, '/'),
        sectorDirectory = Path.GetRelativePath(runDirectory, sectorDirectory)
            .Replace(Path.DirectorySeparatorChar, '/'),
        editorMapUid = Hex(reopenedMap.EditorMapId),
        normalScale = reopenedMap.NormalScale,
        cityScale = reopenedMap.CityScale,
        startPosition = Vector(reopenedMap.StartPosition),
        startRotation = Quaternion(reopenedMap.StartRotation),
        sectorCount = reopenedMap.Sectors.Count,
        itemCount = reopenedMap.MapItems.Count,
        nodeCount = reopenedMap.Nodes.Count,
    },
    road = new
    {
        uid = Hex(reopenedRoad.Uid),
        type = reopenedRoad.RoadType.ToString(),
        look = reopenedRoad.Right.Look.ToString(),
        variant = reopenedRoad.Right.Variant.ToString(),
        leftEdge = reopenedRoad.Right.LeftEdge.ToString(),
        rightEdge = reopenedRoad.Right.RightEdge.ToString(),
        length = reopenedRoad.Length,
        backwardNode = Node(reopenedRoad.Node),
        forwardNode = Node(reopenedRoad.ForwardNode),
    },
    checks = new[]
    {
        "one Road item and two nodes exist before serialization",
        "map, item and node UIDs are non-zero and unique",
        "TruckLib wrote an .mbd plus one sector with .base/.data/.aux/.snd/.desc; no .layer is emitted for the default layer",
        "the .mbd header reports map format 907",
        "TruckLib reopened the generated map without error",
        "road type, appearance tokens, positions, UIDs and bidirectional references survived read-back",
    },
    generatedFiles,
    manualChecksStillRequired = new[]
    {
        "open in ETS2 1.60.x Map Editor on Windows 11 x64",
        "confirm the road is visible, native and selectable",
        "run Map > Recompute map and classify all warnings/errors",
        "save, close the editor completely, and reopen the saved map",
        "confirm the road and references remain valid after editor save",
        "repeat the editor cycle for both generated runs",
    },
};

var manifestPath = Path.Combine(runDirectory, "automatic-validation.json");
var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(manifestPath, json + Environment.NewLine);

Console.WriteLine("AUTOMATIC_VALIDATION_PASSED");
Console.WriteLine("Gate status: AWAITING_MANUAL_VALIDATION");
Console.WriteLine($"Run: {runId}");
Console.WriteLine($"TruckLib: {truckLibAssemblyVersion}");
Console.WriteLine($"Map format: {expectedMapFormat}");
Console.WriteLine($"Map: {mbdPath}");
Console.WriteLine($"Sector directory: {sectorDirectory}");
Console.WriteLine($"Road UID: {Hex(reopenedRoad.Uid)}");
Console.WriteLine($"Backward node UID: {Hex(reopenedRoad.Node.Uid)}");
Console.WriteLine($"Forward node UID: {Hex(reopenedRoad.ForwardNode.Uid)}");
Console.WriteLine($"Manifest: {manifestPath}");

static bool IsValidRunId(string value) =>
    value.Length is > 0 and <= 64
    && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

static string FindProjectDirectory()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Poc001.csproj")))
        {
            return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException("Could not locate the PoC-001 project directory.");
}

static void ValidateInMemoryMap(
    Map map,
    Road road,
    Vector3 backwardPosition,
    Vector3 forwardPosition)
{
    Require(map.MapItems.Count == 1, "The in-memory map must contain exactly one item.");
    Require(map.Nodes.Count == 2, "The in-memory map must contain exactly two nodes.");
    Require(road.Node.Position == backwardPosition, "Unexpected backward-node position.");
    Require(road.ForwardNode.Position == forwardPosition, "Unexpected forward-node position.");
    Require(ReferenceEquals(road.Node.ForwardItem, road), "Backward node does not reference the road.");
    Require(ReferenceEquals(road.ForwardNode.BackwardItem, road), "Forward node does not reference the road.");
    Require(road.Node.BackwardItem is null, "Backward terminal has an unexpected backward item.");
    Require(road.ForwardNode.ForwardItem is null, "Forward terminal has an unexpected forward item.");

    var uids = new[] { map.EditorMapId, road.Uid, road.Node.Uid, road.ForwardNode.Uid };
    Require(uids.All(uid => uid != 0), "TruckLib generated a zero UID.");
    Require(uids.Distinct().Count() == uids.Length, "TruckLib generated duplicate UIDs.");
}

static void ValidateGeneratedFiles(string mbdPath, string sectorDirectory, uint expectedMapFormat)
{
    Require(File.Exists(mbdPath), "TruckLib did not write the .mbd file.");
    Require(Directory.Exists(sectorDirectory), "TruckLib did not write the sector directory.");

    using (var stream = File.OpenRead(mbdPath))
    using (var reader = new BinaryReader(stream))
    {
        Require(reader.ReadUInt32() == expectedMapFormat, "Unexpected .mbd map-format version.");
    }

    var expectedExtensions = new HashSet<string>(StringComparer.Ordinal)
    {
        ".aux", ".base", ".data", ".desc", ".snd",
    };
    var sectorFiles = Directory.GetFiles(sectorDirectory);
    var actualExtensions = sectorFiles
        .Select(Path.GetExtension)
        .ToHashSet(StringComparer.Ordinal);

    Require(sectorFiles.Length == expectedExtensions.Count, "Unexpected number of generated sector files.");
    Require(actualExtensions.SetEquals(expectedExtensions), "Unexpected set of generated sector extensions.");
}

static Road ValidateReopenedMap(
    Map reopenedMap,
    ulong expectedRoadUid,
    ulong expectedBackwardNodeUid,
    ulong expectedForwardNodeUid,
    Vector3 backwardPosition,
    Vector3 forwardPosition,
    string roadType,
    string roadLook,
    string roadVariant,
    string roadEdge)
{
    Require(reopenedMap.Sectors.Count == 1, "Reopened map must contain exactly one sector.");
    Require(reopenedMap.MapItems.Count == 1, "Reopened map must contain exactly one item.");
    Require(reopenedMap.Nodes.Count == 2, "Reopened map must contain exactly two nodes.");

    var road = reopenedMap.MapItems.Values.OfType<Road>().Single();
    Require(road.Uid == expectedRoadUid, "Road UID changed during read-back.");
    Require(road.Node.Uid == expectedBackwardNodeUid, "Backward-node UID changed during read-back.");
    Require(road.ForwardNode.Uid == expectedForwardNodeUid, "Forward-node UID changed during read-back.");
    Require(road.Node.Position == backwardPosition, "Backward-node position changed during read-back.");
    Require(road.ForwardNode.Position == forwardPosition, "Forward-node position changed during read-back.");
    Require(road.RoadType.ToString() == roadType, "Road type changed during read-back.");
    Require(road.Right.Look.ToString() == roadLook, "Road look changed during read-back.");
    Require(road.Right.Variant.ToString() == roadVariant, "Road variant changed during read-back.");
    Require(road.Right.LeftEdge.ToString() == roadEdge, "Left edge changed during read-back.");
    Require(road.Right.RightEdge.ToString() == roadEdge, "Right edge changed during read-back.");
    Require(ReferenceEquals(road.Node.ForwardItem, road), "Reopened backward node lost its road reference.");
    Require(ReferenceEquals(road.ForwardNode.BackwardItem, road), "Reopened forward node lost its road reference.");
    Require(road.Node.BackwardItem is null, "Reopened backward terminal has an unexpected item.");
    Require(road.ForwardNode.ForwardItem is null, "Reopened forward terminal has an unexpected item.");
    return road;
}

static void ValidateEditorSavedMap(
    string mbdPath,
    string originalManifestPath,
    uint expectedMapFormat,
    string roadType,
    string roadLook,
    string roadVariant,
    string roadEdge)
{
    Require(File.Exists(mbdPath), $"Editor-saved .mbd does not exist: {mbdPath}");
    Require(File.Exists(originalManifestPath), $"Original manifest does not exist: {originalManifestPath}");
    Require(ReadMapFormat(mbdPath) == expectedMapFormat, "Editor-saved .mbd has an unexpected format version.");

    using var document = JsonDocument.Parse(File.ReadAllText(originalManifestPath));
    var root = document.RootElement;
    var expectedEditorMapUid = ParseHex(
        root.GetProperty("map").GetProperty("editorMapUid").GetString()
        ?? throw new InvalidDataException("Manifest map UID is missing."));
    var roadElement = root.GetProperty("road");
    var expectedRoadUid = ParseHex(
        roadElement.GetProperty("uid").GetString()
        ?? throw new InvalidDataException("Manifest road UID is missing."));
    var expectedBackwardNodeUid = ParseHex(
        roadElement.GetProperty("backwardNode").GetProperty("uid").GetString()
        ?? throw new InvalidDataException("Manifest backward-node UID is missing."));
    var expectedForwardNodeUid = ParseHex(
        roadElement.GetProperty("forwardNode").GetProperty("uid").GetString()
        ?? throw new InvalidDataException("Manifest forward-node UID is missing."));

    var map = Map.Open(mbdPath);
    var roads = map.MapItems.Values.OfType<Road>().ToArray();
    Require(roads.Length == 1, "Editor-saved map must contain exactly one road.");
    var road = roads.Single();

    Require(road.Node.Position == new Vector3(100, 0, 100),
        "Backward-node position changed after editor save.");
    Require(road.ForwardNode.Position == new Vector3(200, 0, 100),
        "Forward-node position changed after editor save.");
    Require(road.RoadType.ToString() == roadType, "Road type changed after editor save.");
    Require(road.Right.Look.ToString() == roadLook, "Road look changed after editor save.");
    Require(road.Right.Variant.ToString() == roadVariant, "Road variant changed after editor save.");
    Require(road.Right.LeftEdge.ToString() == roadEdge, "Left edge changed after editor save.");
    Require(road.Right.RightEdge.ToString() == roadEdge, "Right edge changed after editor save.");
    Require(ReferenceEquals(road.Node.ForwardItem, road),
        "Backward node lost its road reference after editor save.");
    Require(ReferenceEquals(road.ForwardNode.BackwardItem, road),
        "Forward node lost its road reference after editor save.");
    Require(road.Node.BackwardItem is null,
        "Backward terminal has an unexpected backward item after editor save.");
    Require(road.ForwardNode.ForwardItem is null,
        "Forward terminal has an unexpected forward item after editor save.");
    Require(map.Nodes.TryGetValue(road.Node.Uid, out var storedBackwardNode)
            && ReferenceEquals(storedBackwardNode, road.Node),
        "Backward node is not present in the editor-saved node collection.");
    Require(map.Nodes.TryGetValue(road.ForwardNode.Uid, out var storedForwardNode)
            && ReferenceEquals(storedForwardNode, road.ForwardNode),
        "Forward node is not present in the editor-saved node collection.");

    var allUids = new[] { map.EditorMapId }
        .Concat(map.MapItems.Keys)
        .Concat(map.Nodes.Keys)
        .ToArray();
    Require(allUids.All(uid => uid != 0), "Editor-saved map contains a zero UID.");
    Require(allUids.Distinct().Count() == allUids.Length,
        "Editor-saved map contains duplicate UIDs.");

    var uidsStable = map.EditorMapId == expectedEditorMapUid
        && road.Uid == expectedRoadUid
        && road.Node.Uid == expectedBackwardNodeUid
        && road.ForwardNode.Uid == expectedForwardNodeUid;

    Console.WriteLine("EDITOR_SAVE_TRUCKLIB_READBACK_PASSED");
    Console.WriteLine($"Map: {mbdPath}");
    Console.WriteLine($"Map items: {map.MapItems.Count}");
    Console.WriteLine($"Nodes: {map.Nodes.Count}");
    Console.WriteLine($"UID stability: {(uidsStable ? "STABLE" : "CHANGED_REQUIRES_REVIEW")}");
    Console.WriteLine($"Map UID: {Hex(expectedEditorMapUid)} -> {Hex(map.EditorMapId)}");
    Console.WriteLine($"Road UID: {Hex(expectedRoadUid)} -> {Hex(road.Uid)}");
    Console.WriteLine(
        $"Backward node UID: {Hex(expectedBackwardNodeUid)} -> {Hex(road.Node.Uid)}");
    Console.WriteLine(
        $"Forward node UID: {Hex(expectedForwardNodeUid)} -> {Hex(road.ForwardNode.Uid)}");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static string Hex(ulong value) => $"0x{value:X16}";

static uint ReadMapFormat(string mbdPath)
{
    using var stream = File.OpenRead(mbdPath);
    using var reader = new BinaryReader(stream);
    return reader.ReadUInt32();
}

static ulong ParseHex(string value)
{
    if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException($"Expected hexadecimal UID, got '{value}'.");
    }

    return Convert.ToUInt64(value[2..], 16);
}

static object Vector(Vector3 value) => new { x = value.X, y = value.Y, z = value.Z };

static object Quaternion(Quaternion value) =>
    new { x = value.X, y = value.Y, z = value.Z, w = value.W };

static object Node(INode node) => new
{
    uid = Hex(node.Uid),
    position = Vector(node.Position),
    rotation = Quaternion(node.Rotation),
    isRed = node.IsRed,
    backwardItemUid = node.BackwardItem is null ? null : Hex(node.BackwardItem.Uid),
    forwardItemUid = node.ForwardItem is null ? null : Hex(node.ForwardItem.Uid),
};
