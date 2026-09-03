using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Poc002.Adapter;

internal static class RevisedGenerationComparison
{
    private const string ExpectedCriteriaId = "poc-002-q256-rerun-v2";
    private const string ExpectedRuntimeVersion = "10.0.11";

    private static readonly FrozenRoadExpectation[] FrozenRoads =
    [
        new("east-scale-1", "east-scale-1-road", "east", 1.0),
        new("north-scale-1", "north-scale-1-road", "north", 1.0),
        new("oblique-scale-1", "oblique-scale-1-road", "oblique", 1.0),
        new("oblique-scale-0.1", "oblique-scale-0.1-road", "oblique", 0.1),
        new("tiny-offsets", "tiny-offset-0.001", "tiny-offset-0.001", 1.0),
        new("tiny-offsets", "tiny-offset-0.01", "tiny-offset-0.01", 1.0),
        new("tiny-offsets", "tiny-offset-0.1", "tiny-offset-0.1", 1.0),
        new("near-native-radius", "near-native-radius-road", "native-radius", 2.06),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static int Run(
        string preRunArgument,
        string pythonValidationArgument,
        string neutralJsonArgument,
        string adapterAArgument,
        string semanticAArgument,
        string adapterBArgument,
        string semanticBArgument,
        string outputArgument)
    {
        Require(
            Environment.Version.ToString() == ExpectedRuntimeVersion,
            $"Revised comparison requires Microsoft.NETCore.App runtime {ExpectedRuntimeVersion}; "
            + $"actual {Environment.Version}.");
        var preRunPath = Path.GetFullPath(preRunArgument);
        var pythonValidationPath = Path.GetFullPath(pythonValidationArgument);
        var neutralJsonPath = Path.GetFullPath(neutralJsonArgument);
        var adapterAPath = Path.GetFullPath(adapterAArgument);
        var semanticAPath = Path.GetFullPath(semanticAArgument);
        var adapterBPath = Path.GetFullPath(adapterBArgument);
        var semanticBPath = Path.GetFullPath(semanticBArgument);
        var outputPath = Path.GetFullPath(outputArgument);
        Require(!PathsEqual(adapterAPath, adapterBPath), "Independent adapter report paths must differ.");
        Require(!PathsEqual(semanticAPath, semanticBPath), "Independent semantic evidence paths must differ.");

        Require(File.Exists(preRunPath), $"Pre-run manifest does not exist: {preRunPath}");
        Require(File.Exists(pythonValidationPath), $"Python validation does not exist: {pythonValidationPath}");
        Require(File.Exists(neutralJsonPath), $"Neutral JSON does not exist: {neutralJsonPath}");
        Require(File.Exists(adapterAPath), $"First adapter report does not exist: {adapterAPath}");
        Require(File.Exists(semanticAPath), $"First semantic evidence file does not exist: {semanticAPath}");
        Require(File.Exists(adapterBPath), $"Second adapter report does not exist: {adapterBPath}");
        Require(File.Exists(semanticBPath), $"Second semantic evidence file does not exist: {semanticBPath}");
        var preRunBytes = File.ReadAllBytes(preRunPath);
        var pythonValidationBytes = File.ReadAllBytes(pythonValidationPath);
        var neutralJsonBytes = File.ReadAllBytes(neutralJsonPath);
        var adapterABytes = File.ReadAllBytes(adapterAPath);
        var bytesA = File.ReadAllBytes(semanticAPath);
        var adapterBBytes = File.ReadAllBytes(adapterBPath);
        var bytesB = File.ReadAllBytes(semanticBPath);
        using var preRunDocument = JsonDocument.Parse(preRunBytes);
        using var pythonValidationDocument = JsonDocument.Parse(pythonValidationBytes);
        using var neutralJsonDocument = JsonDocument.Parse(neutralJsonBytes);
        using var adapterDocumentA = JsonDocument.Parse(adapterABytes);
        using var documentA = JsonDocument.Parse(bytesA);
        using var adapterDocumentB = JsonDocument.Parse(adapterBBytes);
        using var documentB = JsonDocument.Parse(bytesB);
        var failures = new List<string>();
        var preRun = ValidatePreRun(preRunDocument.RootElement, failures);
        var neutralSha256 = Sha256(neutralJsonBytes);
        var pythonCheckCount = ValidatePython(
            pythonValidationDocument.RootElement,
            preRun.RunId,
            Sha256(preRunBytes),
            neutralSha256,
            failures);
        var neutralCounts = ValidateNeutral(neutralJsonDocument.RootElement, failures);
        var adapterA = ValidateAdapter(
            adapterDocumentA.RootElement,
            "generation A",
            adapterAPath,
            semanticAPath,
            bytesA,
            neutralJsonPath,
            neutralSha256,
            failures);
        var adapterB = ValidateAdapter(
            adapterDocumentB.RootElement,
            "generation B",
            adapterBPath,
            semanticBPath,
            bytesB,
            neutralJsonPath,
            neutralSha256,
            failures);
        ValidateSemantic(
            documentA.RootElement,
            "generation A",
            neutralSha256,
            failures);
        ValidateSemantic(
            documentB.RootElement,
            "generation B",
            neutralSha256,
            failures);
        Check(
            !string.Equals(adapterA.GenerationId, adapterB.GenerationId, StringComparison.Ordinal),
            failures,
            "Independent generations must have distinct generation IDs.");
        Check(
            !PathsEqual(adapterA.OutputRoot, adapterB.OutputRoot),
            failures,
            "Independent generations must have distinct output roots.");

        var byteExact = AreSemanticallyIdentical(bytesA, bytesB);
        if (!byteExact)
        {
            failures.Add("The deterministic semantic manifests differ byte for byte.");
        }

        var passed = failures.Count == 0;
        var report = new ComparisonReport(
            SchemaVersion: 1,
            CriteriaId: ExpectedCriteriaId,
            Poc: "PoC-002 — Coordinate and Geometry Validation",
            HistoricalV1Status: "FAIL",
            ComparisonValidation: passed ? "PASS" : "FAIL",
            RerunState: passed ? "AWAITING_MANUAL_VALIDATION" : "FAIL",
            Method:
                "Two separately generated semantic-validation.json files are compared byte for byte after each generator excludes documented nondeterministic fields.",
            Compared:
            [
                "pre-run inputValidation, criteria ID, frozen hashes and exact expected criteria",
                "Python automaticStatus, every original check and frozen numeric budgets",
                "schemaVersion 2 ETS2-independent neutral E/N/H JSON and its SHA-256 binding",
                "generation ID, distinct output root, adapter/semantic hashes and exact environment baseline",
                "map/road fixture identity, scale, assets, format and counts",
                "float64 mapping, float32 values/bits and all expected/written/readback Q256 codes",
                "Q256 losses/bounds, native geometry metrics and relative file paths/sizes",
                "45 direct positive/negative/zero and boundary probes across X/Y/Z",
            ],
            Excluded:
            [
                "random TruckLib map/item/node UIDs",
                "native binary SHA-256 values",
                "absolute paths",
            ],
            Inputs: new InputEvidenceReport(
                PreRunManifest: Evidence(preRunPath, preRunBytes),
                PythonValidation: Evidence(pythonValidationPath, pythonValidationBytes),
                NeutralJson: Evidence(neutralJsonPath, neutralJsonBytes)),
            ValidationCounts: new ValidationCountReport(
                PreRunFrozenFileCount: preRun.FrozenFileCount,
                PythonCheckCount: pythonCheckCount,
                NeutralMapCount: neutralCounts.MapCount,
                NeutralRoadCount: neutralCounts.RoadCount,
                ExpectedNodeCountPerGeneration: 16,
                ExpectedAxisCountPerGeneration: 48,
                DirectProbeCountPerGeneration: 45),
            GenerationA: new GenerationEvidenceReport(
                adapterA.GenerationId,
                adapterA.OutputRoot,
                Evidence(adapterAPath, adapterABytes),
                Evidence(semanticAPath, bytesA),
                "PASS"),
            GenerationB: new GenerationEvidenceReport(
                adapterB.GenerationId,
                adapterB.OutputRoot,
                Evidence(adapterBPath, adapterBBytes),
                Evidence(semanticBPath, bytesB),
                "PASS"),
            ByteExactSemanticAgreement: byteExact,
            Failures: failures,
            Environment: new ComparisonEnvironment(
                Runtime: RuntimeInformation.FrameworkDescription,
                RuntimeVersion: Environment.Version.ToString(),
                Os: RuntimeInformation.OSDescription,
                ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString()),
            ManualChecksStillRequired:
            [
                "use one preserved pre-editor native generation and freeze its node identities/Q256 codes",
                "complete the ETS2 1.60.1.7 Map Editor Recompute/save/close/reopen cycle on Windows 11 x64",
                "require q_after == q_before == q_expected per node and X/Y/Z component",
                "confirm or reject the visual geographic semantics of X=E, Y=H, Z=-N",
            ]);
        var parent = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException($"Output report has no parent: {outputPath}");
        Directory.CreateDirectory(parent);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);

        Console.WriteLine(passed
            ? "REVISED_NATIVE_REPRODUCIBILITY_PASSED"
            : "REVISED_NATIVE_REPRODUCIBILITY_FAILED");
        Console.WriteLine($"Rerun state: {report.RerunState}");
        Console.WriteLine($"Byte-exact semantic agreement: {byteExact}");
        Console.WriteLine($"Report: {outputPath}");
        return passed ? 0 : 2;
    }

    internal static void RunSelfTest()
    {
        ReadOnlySpan<byte> first = "{\"stable\":true}"u8;
        ReadOnlySpan<byte> same = "{\"stable\":true}"u8;
        ReadOnlySpan<byte> different = "{\"stable\":false}"u8;
        Require(AreSemanticallyIdentical(first, same), "Semantic comparison rejected identical bytes.");
        Require(!AreSemanticallyIdentical(first, different), "Semantic comparison accepted different bytes.");
        Require(IsSafeId("generation-a"), "Safe generation ID was rejected.");
        Require(!IsSafeId("../generation-a"), "Unsafe generation ID was accepted.");
        Require(
            !PathsEqual("generation-a/adapter-validation-v2.json", "generation-b/adapter-validation-v2.json"),
            "Distinct generation report paths were treated as equal.");
    }

    private static bool AreSemanticallyIdentical(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) =>
        first.SequenceEqual(second);

    private static PreRunValidation ValidatePreRun(JsonElement root, List<string> failures)
    {
        var runId = RequiredString(root, "runId");
        Check(IsSafeId(runId), failures, "Pre-run manifest runId is not a safe 1-64 character ID.");
        Check(
            RequiredString(root, "criteriaId") == ExpectedCriteriaId,
            failures,
            "Pre-run manifest criteriaId does not match the frozen revised criteria.");
        Check(
            RequiredString(root, "inputValidation") == "PASS",
            failures,
            "Pre-run input validation is not PASS.");
        var recordedFailures = root.GetProperty("failures");
        Check(
            recordedFailures.ValueKind == JsonValueKind.Array && recordedFailures.GetArrayLength() == 0,
            failures,
            "Pre-run manifest records one or more failures.");
        var frozenFiles = root.GetProperty("frozenFiles").EnumerateArray().ToArray();
        Check(frozenFiles.Length >= 6, failures, "Pre-run manifest does not bind all frozen files.");
        foreach (var file in frozenFiles)
        {
            Check(file.GetProperty("matched").GetBoolean(), failures, "A frozen pre-run file hash did not match.");
            Check(
                RequiredString(file, "expectedSha256") == RequiredString(file, "actualSha256"),
                failures,
                $"Frozen file hash mismatch for {RequiredString(file, "path")}.");
        }

        var expected = root.GetProperty("expectedCriteria");
        CheckExact(expected, "geographicRoundTripMaximumM", 0.001, failures);
        CheckExact(expected, "projectedDiscretizationMaximumPreScaleM", 0.01, failures);
        CheckExact(expected, "float64ToFloat32Maximum3dM", 0.001, failures);
        CheckExact(expected, "nativeStraightRoadHausdorffMaximumM", 1.0, failures);
        CheckExact(expected, "nativePlanarRadiusMaximumM", 10_000.0, failures);
        var q256 = expected.GetProperty("q256");
        CheckExact(q256, "gridStepM", 1.0 / 256.0, failures);
        CheckExact(q256, "perAxisStrictUpperBoundM", 1.0 / 256.0, failures);
        CheckExact(q256, "horizontalXzStrictUpperBoundM", Math.Sqrt(2.0) / 256.0, failures);
        CheckExact(q256, "threeDimensionalStrictUpperBoundM", Math.Sqrt(3.0) / 256.0, failures);
        Check(
            RequiredString(q256, "expectedCode") == "trunc_toward_zero(float32_axis * 256f)",
            failures,
            "Pre-run Q256 formula differs from DT-07.");
        return new PreRunValidation(runId, frozenFiles.Length);
    }

    private static int ValidatePython(
        JsonElement root,
        string expectedRunId,
        string expectedPreRunSha256,
        string expectedNeutralSha256,
        List<string> failures)
    {
        Check(
            RequiredString(root, "criteriaId") == ExpectedCriteriaId,
            failures,
            "Python validation criteriaId differs from the pre-run criteria.");
        Check(
            RequiredString(root, "runId") == expectedRunId,
            failures,
            "Python validation runId differs from the pre-run manifest.");
        Check(
            RequiredString(root, "preRunManifestSha256") == expectedPreRunSha256,
            failures,
            "Python validation is not bound to the supplied pre-run manifest SHA-256.");
        Check(
            RequiredString(root, "neutralModelSha256") == expectedNeutralSha256,
            failures,
            "Python validation is not bound to the supplied neutral JSON SHA-256.");
        Check(
            RequiredString(root, "automaticStatus") == "PASS",
            failures,
            "Python automatic validation is not PASS.");
        var checks = root.GetProperty("checks").EnumerateArray().ToArray();
        var requiredIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "exact-runtime-and-offline-proj",
            "explicit-origin",
            "candidate-extent-origin-before-exclusions",
            "forward-inverse-round-trip",
            "independent-vincenty-radial-reference",
            "independent-direction-reference",
            "bbox-geodesic-area-and-diagonal",
            "clip-before-project-with-provenance",
            "projected-discretization",
            "single-uniform-scaling",
            "tiny-offsets-are-translated-roads",
            "native-radius-inside-limit",
            "neutral-model-six-independent-maps",
        };
        var observedIds = checks.Select(check => RequiredString(check, "id")).ToHashSet(StringComparer.Ordinal);
        Check(
            requiredIds.IsSubsetOf(observedIds),
            failures,
            "Python validation is missing one or more original frozen checks.");
        Check(
            checks.All(check => check.GetProperty("passed").GetBoolean()),
            failures,
            "At least one Python validation check failed.");
        var maxima = root.GetProperty("maximumErrors");
        Check(
            maxima.GetProperty("geographicRoundTripM").GetDouble() <= 0.001,
            failures,
            "Python WGS84/AEQD round-trip exceeds 0.001 m.");
        Check(
            maxima.GetProperty("projectedDiscretizationIncludingSamplingConvergenceM").GetDouble() <= 0.01,
            failures,
            "Python projected discretization exceeds 0.01 m before scaling.");
        return checks.Length;
    }

    private static NeutralCounts ValidateNeutral(JsonElement root, List<string> failures)
    {
        Check(root.GetProperty("schemaVersion").GetInt32() == 2, failures, "Neutral JSON is not schemaVersion 2.");
        var coordinateSystem = root.GetProperty("coordinateSystem");
        var coordinateProperties = coordinateSystem
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Check(
            coordinateProperties.SetEquals(["axes", "unit"]),
            failures,
            "Neutral coordinateSystem contains adapter/native fields or omits axes/unit.");
        var axes = coordinateSystem.GetProperty("axes")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        Check(axes.SequenceEqual(["E", "N", "H"]), failures, "Neutral axes are not exactly E/N/H.");
        Check(
            RequiredString(coordinateSystem, "unit") == "scene metre",
            failures,
            "Neutral coordinate unit is not scene metre.");
        var maps = root.GetProperty("maps").EnumerateArray().ToArray();
        var roadCount = maps.Sum(map => map.GetProperty("roads").GetArrayLength());
        Check(maps.Length == 6, failures, $"Neutral map count is {maps.Length}, expected 6.");
        Check(roadCount == 8, failures, $"Neutral road count is {roadCount}, expected 8.");
        ValidateFrozenFixtures(maps, "neutral JSON", failures);
        return new NeutralCounts(maps.Length, roadCount);
    }

    private static AdapterValidation ValidateAdapter(
        JsonElement root,
        string label,
        string adapterPath,
        string semanticPath,
        byte[] semanticBytes,
        string neutralJsonPath,
        string neutralSha256,
        List<string> failures)
    {
        Check(root.GetProperty("schemaVersion").GetInt32() == 2, failures, $"{label} adapter schemaVersion is not 2.");
        Check(
            RequiredString(root, "criteriaId") == ExpectedCriteriaId,
            failures,
            $"{label} adapter criteriaId is not {ExpectedCriteriaId}.");
        Check(
            RequiredString(root, "generationAutomaticValidation") == "PASS",
            failures,
            $"{label} adapter automatic validation is not PASS.");
        Check(
            RequiredString(root, "rerunState")
                == "AUTOMATIC_GENERATION_PASSED; REPRODUCIBILITY_COMPARISON_REQUIRED",
            failures,
            $"{label} adapter has an invalid intermediate rerun state.");

        var generationId = RequiredString(root, "generationId");
        Check(IsSafeId(generationId), failures, $"{label} generationId is not a safe 1-64 character ID.");
        var outputRoot = Path.GetFullPath(RequiredString(root, "outputRoot"));
        var adapterDirectory = Path.GetDirectoryName(adapterPath)
            ?? throw new InvalidDataException($"{label} adapter report has no parent directory.");
        Check(
            PathsEqual(outputRoot, adapterDirectory),
            failures,
            $"{label} declared outputRoot is not its adapter report directory.");
        Check(
            Path.GetFileName(adapterPath) == "adapter-validation-v2.json",
            failures,
            $"{label} adapter report filename is not adapter-validation-v2.json.");

        var input = root.GetProperty("input");
        Check(
            RequiredString(input, "sha256") == neutralSha256,
            failures,
            $"{label} adapter is not bound to the supplied neutral JSON SHA-256.");
        Check(
            PathsEqual(RequiredString(input, "path"), neutralJsonPath),
            failures,
            $"{label} adapter input path is not the supplied neutral JSON path.");
        ValidateEnvironment(root.GetProperty("environment"), $"{label} adapter", failures);

        var semanticReference = root.GetProperty("semanticEvidence");
        Check(
            RequiredString(semanticReference, "sha256") == Sha256(semanticBytes),
            failures,
            $"{label} adapter semanticEvidence SHA-256 does not match the supplied semantic file.");
        var declaredSemanticPath = Path.GetFullPath(
            Path.Combine(outputRoot, RequiredString(semanticReference, "path")));
        Check(
            PathsEqual(declaredSemanticPath, semanticPath),
            failures,
            $"{label} adapter semanticEvidence path does not resolve to the supplied semantic file.");
        Check(
            Path.GetFileName(semanticPath) == "semantic-validation.json"
                && PathsEqual(Path.GetDirectoryName(semanticPath)!, outputRoot),
            failures,
            $"{label} semantic evidence is not in its generation output root.");

        var q256 = root.GetProperty("q256");
        Check(q256.GetProperty("generatedNodeCount").GetInt32() == 16, failures, $"{label} adapter node count is not 16.");
        Check(q256.GetProperty("generatedAxisCount").GetInt32() == 48, failures, $"{label} adapter axis count is not 48.");
        Check(
            q256.GetProperty("exactGeneratedAxisAgreementCount").GetInt32() == 48,
            failures,
            $"{label} adapter does not have 48 exact Q256 agreements.");
        Check(q256.GetProperty("directProbeCount").GetInt32() == 45, failures, $"{label} adapter probe count is not 45.");
        ValidateFrozenFixtures(root.GetProperty("maps").EnumerateArray().ToArray(), $"{label} adapter", failures);
        Check(
            root.GetProperty("q256Roads").GetArrayLength() == FrozenRoads.Length,
            failures,
            $"{label} adapter q256Roads count is not {FrozenRoads.Length}.");
        return new AdapterValidation(generationId, outputRoot);
    }

    private static string ValidateSemantic(
        JsonElement root,
        string label,
        string neutralSha256,
        List<string> failures)
    {
        Check(root.GetProperty("schemaVersion").GetInt32() == 1, failures, $"{label} semantic schemaVersion is not 1.");
        var criteria = RequiredString(root, "criteriaId");
        var validation = RequiredString(root, "generationAutomaticValidation");
        Check(criteria == ExpectedCriteriaId, failures, $"{label} criteriaId is not {ExpectedCriteriaId}.");
        Check(validation == "PASS", failures, $"{label} automatic validation is not PASS.");
        Check(
            RequiredString(root, "inputSha256") == neutralSha256,
            failures,
            $"{label} is not bound to the supplied neutral JSON SHA-256.");
        Check(
            RequiredString(root, "axisSemanticStatus") == "MAP_EDITOR_GEOGRAPHIC_SEMANTICS_PENDING",
            failures,
            $"{label} incorrectly closes the Map Editor axis-semantics gate.");
        ValidateEnvironment(root.GetProperty("environment"), $"{label} semantic evidence", failures);

        var maxima = root.GetProperty("maxima");
        Check(
            maxima.GetProperty("doubleToFloat3dMetres").GetDouble() <= 0.001,
            failures,
            $"{label} float64-to-float32 error exceeds 0.001 m.");
        Check(
            maxima.GetProperty("straightSegmentHausdorffMetres").GetDouble() <= 1.0,
            failures,
            $"{label} straight-road Hausdorff deviation exceeds 1.0 m.");
        Check(
            maxima.GetProperty("nativePlanarRadiusMetres").GetDouble() <= 10_000.0,
            failures,
            $"{label} native planar radius exceeds 10,000 m.");

        var q256 = root.GetProperty("q256");
        CheckExact(q256, "gridStepMetres", 1.0 / 256.0, failures, label);
        CheckExact(q256, "perAxisStrictUpperBoundMetres", 1.0 / 256.0, failures, label);
        CheckExact(q256, "horizontalXzStrictUpperBoundMetres", Math.Sqrt(2.0) / 256.0, failures, label);
        CheckExact(q256, "threeDimensionalStrictUpperBoundMetres", Math.Sqrt(3.0) / 256.0, failures, label);
        Check(q256.GetProperty("generatedNodeCount").GetInt32() == 16, failures, $"{label} node count is not 16.");
        Check(q256.GetProperty("generatedAxisCount").GetInt32() == 48, failures, $"{label} axis count is not 48.");
        Check(
            q256.GetProperty("exactGeneratedAxisAgreementCount").GetInt32() == 48,
            failures,
            $"{label} does not have 48 exact generated Q256 axis agreements.");
        Check(q256.GetProperty("directProbeCount").GetInt32() == 45, failures, $"{label} probe count is not 45.");
        Check(
            q256.GetProperty("maximumGeneratedAxisLossMetres").GetDouble() < 1.0 / 256.0,
            failures,
            $"{label} violates the Q256 per-axis strict bound.");
        Check(
            q256.GetProperty("maximumGeneratedHorizontalXzLossMetres").GetDouble() < Math.Sqrt(2.0) / 256.0,
            failures,
            $"{label} violates the Q256 horizontal strict bound.");
        Check(
            q256.GetProperty("maximumGeneratedThreeDimensionalLossMetres").GetDouble() < Math.Sqrt(3.0) / 256.0,
            failures,
            $"{label} violates the Q256 3D strict bound.");
        Check(
            q256.GetProperty("maximumDirectProbeAxisLossMetres").GetDouble() < 1.0 / 256.0,
            failures,
            $"{label} direct probes violate the Q256 per-axis strict bound.");

        ValidateProbes(root.GetProperty("directSerializerProbes"), label, failures);
        ValidateSemanticMaps(root.GetProperty("maps"), label, failures);
        return validation;
    }

    private static void ValidateProbes(JsonElement probesElement, string label, List<string> failures)
    {
        var probes = probesElement.EnumerateArray().ToArray();
        Check(probes.Length == 45, failures, $"{label} does not contain exactly 45 direct probes.");
        foreach (var axis in new[] { "X", "Y", "Z" })
        {
            var axisProbes = probes.Where(probe => RequiredString(probe, "axis") == axis).ToArray();
            Check(axisProbes.Length == 15, failures, $"{label} does not contain 15 {axis} probes.");
            Check(
                axisProbes.Any(probe => probe.GetProperty("inputFloat").GetSingle() < 0)
                && axisProbes.Any(probe => probe.GetProperty("inputFloat").GetSingle() == 0)
                && axisProbes.Any(probe => probe.GetProperty("inputFloat").GetSingle() > 0),
                failures,
                $"{label} {axis} probes do not cover negative, zero and positive values.");
            foreach (var probe in axisProbes)
            {
                var input = probe.GetProperty("inputFloat").GetSingle();
                var expected = checked((int)(input * 256f));
                var declaredExpected = probe.GetProperty("expectedEncodedInteger").GetInt32();
                var encoded = probe.GetProperty("encoded");
                var actual = encoded.GetProperty(axis.ToLowerInvariant()).GetInt32();
                Check(
                    expected == declaredExpected && expected == actual,
                    failures,
                    $"{label} direct probe {RequiredString(probe, "case")}/{axis} has a Q256 code mismatch.");
            }
        }
    }

    private static void ValidateSemanticMaps(JsonElement mapsElement, string label, List<string> failures)
    {
        var maps = mapsElement.EnumerateArray().ToArray();
        var roads = maps.SelectMany(map => map.GetProperty("roads").EnumerateArray()).ToArray();
        Check(maps.Length == 6, failures, $"{label} semantic map count is not 6.");
        Check(roads.Length == 8, failures, $"{label} semantic road count is not 8.");
        Check(
            maps.Sum(map => map.GetProperty("nodeCount").GetInt32()) == 16,
            failures,
            $"{label} semantic node total is not 16.");
        Check(
            maps.All(map => map.GetProperty("mapFormat").GetUInt32() == 907),
            failures,
            $"{label} contains a map format other than 907.");
        Check(
            roads.Any(road => road.GetProperty("scale").GetDouble() == 1.0)
                && roads.Any(road => road.GetProperty("scale").GetDouble() == 0.1),
            failures,
            $"{label} does not include both scale 1.0 and 0.1.");
        ValidateFrozenFixtures(maps, $"{label} semantic evidence", failures);

        var axisCount = 0;
        foreach (var road in roads)
        {
            var qRoad = road.GetProperty("q256");
            foreach (var endpointName in new[] { "backward", "forward" })
            {
                var endpoint = qRoad.GetProperty(endpointName);
                var neutral = endpoint.GetProperty("neutralEnhFloat64");
                var axes = endpoint.GetProperty("axes").EnumerateArray().ToArray();
                axisCount += axes.Length;
                Check(axes.Length == 3, failures, $"{label} {endpointName} endpoint does not contain X/Y/Z.");
                foreach (var axis in axes)
                {
                    var axisName = RequiredString(axis, "axis");
                    var source = RequiredString(axis, "neutralSourceExpression");
                    var expectedSource = axisName switch
                    {
                        "X" => "E",
                        "Y" => "H",
                        "Z" => "-N",
                        _ => "INVALID",
                    };
                    var expectedMapped = axisName switch
                    {
                        "X" => neutral.GetProperty("e").GetDouble(),
                        "Y" => neutral.GetProperty("h").GetDouble(),
                        "Z" => -neutral.GetProperty("n").GetDouble(),
                        _ => double.NaN,
                    };
                    Check(source == expectedSource, failures, $"{label} {axisName} source mapping is wrong.");
                    Check(
                        axis.GetProperty("mappedFloat64").GetDouble() == expectedMapped,
                        failures,
                        $"{label} {axisName} mapped float64 value is wrong.");
                    var input = axis.GetProperty("float32Input").GetSingle();
                    var expectedQ = checked((int)(input * 256f));
                    Check(
                        axis.GetProperty("expectedQ").GetInt32() == expectedQ
                            && axis.GetProperty("writtenQ").GetInt32() == expectedQ
                            && axis.GetProperty("readbackQ").GetInt32() == expectedQ
                            && axis.GetProperty("exactCodeAgreement").GetBoolean(),
                        failures,
                        $"{label} {axisName} endpoint code agreement failed.");
                    Check(
                        axis.GetProperty("readbackNativeAxis").GetSingle() == expectedQ / 256f,
                        failures,
                        $"{label} {axisName} readback does not equal expected_q/256f.");
                }
            }
        }
        Check(axisCount == 48, failures, $"{label} semantic endpoint axis count is {axisCount}, expected 48.");
    }

    private static void ValidateFrozenFixtures(
        JsonElement[] maps,
        string label,
        List<string> failures)
    {
        var expectedMapIds = FrozenRoads
            .Select(expectation => expectation.MapId)
            .ToHashSet(StringComparer.Ordinal);
        var observedMapIds = maps
            .Select(map => RequiredString(map, "id"))
            .ToArray();
        Check(
            observedMapIds.Length == expectedMapIds.Count
                && observedMapIds.Distinct(StringComparer.Ordinal).Count() == observedMapIds.Length
                && observedMapIds.ToHashSet(StringComparer.Ordinal).SetEquals(expectedMapIds),
            failures,
            $"{label} map identities do not exactly match the six frozen fixtures.");

        var observedRoads = maps
            .SelectMany(map => map.GetProperty("roads").EnumerateArray()
                .Select(road => new FrozenRoadExpectation(
                    RequiredString(map, "id"),
                    RequiredString(road, "id"),
                    RequiredString(road, "sourceFixtureId"),
                    road.GetProperty("scale").GetDouble())))
            .ToArray();
        Check(
            observedRoads.Length == FrozenRoads.Length
                && observedRoads.Distinct().Count() == observedRoads.Length
                && observedRoads.ToHashSet().SetEquals(FrozenRoads),
            failures,
            $"{label} road IDs, source fixture IDs or scales do not exactly match the frozen fixtures.");
    }

    private static void ValidateEnvironment(
        JsonElement environment,
        string label,
        List<string> failures)
    {
        Check(
            RequiredString(environment, "sdkPinned") == "10.0.400",
            failures,
            $"{label} SDK pin is not 10.0.400.");
        Check(
            RequiredString(environment, "targetFramework") == "net10.0",
            failures,
            $"{label} target framework is not net10.0.");
        Check(
            RequiredString(environment, "runtimeVersion") == ExpectedRuntimeVersion,
            failures,
            $"{label} runtime version is not {ExpectedRuntimeVersion}.");
        Check(
            RequiredString(environment, "truckLibPackage") == "0.5.1",
            failures,
            $"{label} TruckLib package is not 0.5.1.");
        Check(
            RequiredString(environment, "truckLibAssemblyVersion") == "0.5.1.0",
            failures,
            $"{label} TruckLib assembly version is not 0.5.1.0.");
        Check(
            environment.GetProperty("truckLibDeclaredMapFormat").GetUInt32() == 907,
            failures,
            $"{label} declared map format is not 907.");
    }

    private static bool IsSafeId(string value) =>
        value.Length is >= 1 and <= 64
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.Ordinal);

    private static void CheckExact(
        JsonElement parent,
        string property,
        double expected,
        List<string> failures,
        string? label = null) =>
        Check(
            parent.GetProperty(property).GetDouble() == expected,
            failures,
            $"{label ?? "Pre-run manifest"} {property} differs from the frozen value {expected:R}.");

    private static void Check(bool condition, List<string> failures, string failure)
    {
        if (!condition)
        {
            failures.Add(failure);
        }
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static EvidenceReport Evidence(string path, byte[] bytes) => new(
        Path: path,
        Sha256: Sha256(bytes),
        Bytes: bytes.LongLength);

    private static string RequiredString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException(
                $"Semantic evidence is missing required string property '{propertyName}'.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record ComparisonReport(
        int SchemaVersion,
        string CriteriaId,
        string Poc,
        string HistoricalV1Status,
        string ComparisonValidation,
        string RerunState,
        string Method,
        List<string> Compared,
        List<string> Excluded,
        InputEvidenceReport Inputs,
        ValidationCountReport ValidationCounts,
        GenerationEvidenceReport GenerationA,
        GenerationEvidenceReport GenerationB,
        bool ByteExactSemanticAgreement,
        List<string> Failures,
        ComparisonEnvironment Environment,
        List<string> ManualChecksStillRequired);

    private sealed record InputEvidenceReport(
        EvidenceReport PreRunManifest,
        EvidenceReport PythonValidation,
        EvidenceReport NeutralJson);

    private sealed record ValidationCountReport(
        int PreRunFrozenFileCount,
        int PythonCheckCount,
        int NeutralMapCount,
        int NeutralRoadCount,
        int ExpectedNodeCountPerGeneration,
        int ExpectedAxisCountPerGeneration,
        int DirectProbeCountPerGeneration);

    private readonly record struct NeutralCounts(int MapCount, int RoadCount);

    private readonly record struct PreRunValidation(string RunId, int FrozenFileCount);

    private readonly record struct AdapterValidation(string GenerationId, string OutputRoot);

    private readonly record struct FrozenRoadExpectation(
        string MapId,
        string RoadId,
        string SourceFixtureId,
        double Scale);

    private sealed record GenerationEvidenceReport(
        string GenerationId,
        string OutputRoot,
        EvidenceReport AdapterReport,
        EvidenceReport SemanticEvidence,
        string GenerationAutomaticValidation);

    private sealed record EvidenceReport(
        string Path,
        string Sha256,
        long Bytes);

    private sealed record ComparisonEnvironment(
        string Runtime,
        string RuntimeVersion,
        string Os,
        string ProcessArchitecture);
}
