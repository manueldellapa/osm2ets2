using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Poc002.Adapter;
using TruckLib.ScsMap;

internal static class Program
{
    private const int ExpectedSchemaVersion = 1;
    private const string ExpectedPoc = "PoC-002 — Coordinate and Geometry Validation";
    private const string ExpectedSourceUnit = "scene metre";
    private const string RoadType = "ger1";
    private const string RoadLook = "ger_1";
    private const string RoadVariant = "broken_de";
    private const string RoadEdge = "ger_sh_15";
    private const uint ExpectedMapFormat = 907;
    private const double NativeConversionThresholdMetres = 0.001;
    private const double StraightRoadThresholdMetres = 1.0;
    private const double NativeRadiusThresholdMetres = 10_000.0;
    private const double ObservedCandidateCoordinateStepMetres = 1.0 / 256.0;
    private const string RevisedCriteriaId = "poc-002-q256-rerun-v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static int Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["--self-test"] => RunSelfTest(),
                ["--quantizer-rca", var existingAdapterReport, var outputReport] =>
                    QuantizerRca.Run(existingAdapterReport, outputReport),
                ["--revised-rerun", var generationId, var neutralJson, var outputRoot] =>
                    GenerateRevised(generationId, neutralJson, outputRoot),
                ["--compare-revised-generations", var preRun, var pythonValidation, var neutralJson,
                    var adapterA, var semanticA, var adapterB, var semanticB, var outputReport] =>
                    RevisedGenerationComparison.Run(
                        preRun,
                        pythonValidation,
                        neutralJson,
                        adapterA,
                        semanticA,
                        adapterB,
                        semanticB,
                        outputReport),
                ["--validate-editor-save", var savedMapRoot, var neutralJson, var outputReport] =>
                    ValidateEditorSave(savedMapRoot, neutralJson, outputReport),
                ["--validate-revised-editor-save", var aggregateReport, var preEditorAdapter,
                    var savedMapRoot, var outputReport] =>
                    RevisedEditorValidation.Run(
                        aggregateReport,
                        preEditorAdapter,
                        savedMapRoot,
                        outputReport),
                [var neutralJson, var outputRoot] => Generate(neutralJson, outputRoot),
                _ => UsageError(),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 1;
        }
    }

    private static int UsageError()
    {
        Console.Error.WriteLine(
            "Usage:\n"
            + "  dotnet run -- <neutral-json> <fresh-output-root>\n"
            + "  dotnet run -- --self-test\n"
            + "  dotnet run -- --quantizer-rca <existing-adapter-report> <output-report>\n"
            + "  dotnet run -- --revised-rerun <generation-id> <neutral-json> <fresh-output-root>\n"
            + "  dotnet run -- --compare-revised-generations <pre-run-manifest> <python-validation> "
            + "<neutral-json> <adapter-a> <semantic-a> <adapter-b> <semantic-b> <output-report>\n"
            + "  dotnet run -- --validate-editor-save <saved-map-root> <neutral-json> <output-report>\n\n"
            + "  dotnet run -- --validate-revised-editor-save <aggregate-pass-report> "
            + "<pre-editor-adapter-v2> <saved-map-root> <output-report>\n\n"
            + "Generation writes maps/<map-id>/<map-id>.mbd under the output root. "
            + "Editor validation expects the same one-directory-per-map layout under saved-map-root.");
        return 64;
    }

    private static int Generate(string neutralJsonArgument, string outputRootArgument)
    {
        var neutralJsonPath = Path.GetFullPath(neutralJsonArgument);
        var outputRoot = Path.GetFullPath(outputRootArgument);
        var model = LoadAndValidateModel(neutralJsonPath);
        EnsureFreshOutputRoot(outputRoot);

        var mapReports = new List<GeneratedMapReport>();
        var numericalFailures = new List<string>();

        foreach (var neutralMap in model.Maps)
        {
            mapReports.Add(GenerateMap(neutralMap, outputRoot, revisedQ256Reports: null));
        }

        var allRoads = mapReports.SelectMany(map => map.Roads).ToArray();
        var maxima = new NumericalMaxima(
            DoubleToFloat3dMetres: MaxOrZero(allRoads.Select(road => road.Errors.DoubleToFloatMax3dMetres)),
            GeneratedReadback3dMetres: MaxOrZero(allRoads.Select(road => road.Errors.GeneratedReadbackMax3dMetres)),
            FloatSerialization3dMetres: MaxOrZero(allRoads.Select(road => road.Errors.FloatSerializationMax3dMetres)),
            StraightSegmentHausdorffMetres: MaxOrZero(allRoads.Select(road => road.Errors.StraightSegmentHausdorffMetres)),
            NativePlanarRadiusMetres: MaxOrZero(allRoads.Select(road => road.NativePlanarRadiusMaxMetres)));
        var maxCandidateGridResidual = MaxOrZero(
            allRoads.Select(road => CandidateGridResidual(road.GeneratedReadback)));

        AddThresholdFailure(
            numericalFailures,
            "double-to-float conversion",
            maxima.DoubleToFloat3dMetres,
            NativeConversionThresholdMetres);
        AddThresholdFailure(
            numericalFailures,
            "generated TruckLib readback",
            maxima.GeneratedReadback3dMetres,
            NativeConversionThresholdMetres);
        AddThresholdFailure(
            numericalFailures,
            "native straight-road geometry",
            maxima.StraightSegmentHausdorffMetres,
            StraightRoadThresholdMetres);
        AddThresholdFailure(
            numericalFailures,
            "native planar radius",
            maxima.NativePlanarRadiusMetres,
            NativeRadiusThresholdMetres);

        var files = InventoryFiles(outputRoot);
        var automaticPassed = numericalFailures.Count == 0;
        var report = new AdapterValidationReport(
            SchemaVersion: 1,
            Poc: model.Poc,
            GateStatus: automaticPassed ? "AWAITING_MANUAL_VALIDATION" : "FAIL",
            AutomaticValidation: automaticPassed ? "PASS" : "FAIL",
            Input: new InputEvidence(
                Path: neutralJsonPath,
                Sha256: Sha256(neutralJsonPath)),
            Environment: ReadEnvironment(),
            CoordinateContract: new CoordinateContractReport(
                SourceAxes: ["E", "N", "H"],
                Unit: ExpectedSourceUnit,
                CandidateMapping: new MappingReport("E", "H", "-N"),
                AdapterOperation: "double (E,N,H) -> double (X=E,Y=H,Z=-N) -> System.Numerics.Vector3(float)",
                SemanticStatus: "HYPOTHESIS_APPLIED; MAP_EDITOR_ORIENTATION_VALIDATION_REQUIRED"),
            Thresholds: new ThresholdReport(
                NativeNumericalConversion3dMetres: NativeConversionThresholdMetres,
                StraightRoadHausdorffMetres: StraightRoadThresholdMetres,
                NativePlanarRadiusMetres: NativeRadiusThresholdMetres),
            Maxima: maxima,
            NativePrecisionDiagnostic: new NativePrecisionDiagnosticReport(
                CandidateCoordinateStepMetres: ObservedCandidateCoordinateStepMetres,
                MaxGeneratedReadbackAxisResidualFromCandidateGridMetres: maxCandidateGridResidual,
                Interpretation:
                    "The 1/256 m grid is tested as an experiment-derived diagnostic hypothesis, not asserted as a TruckLib or ETS2 format specification.",
                ThresholdCompatibility: maxima.GeneratedReadback3dMetres <= NativeConversionThresholdMetres
                    ? "NOT_DISPROVED_BY_THESE_FIXTURES"
                    : "INCOMPATIBLE_WITH_0.001_M_FOR_AT_LEAST_ONE_FIXTURE"),
            Criteria: BuildAutomaticCriteria(maxima),
            NumericalFailures: numericalFailures,
            Maps: mapReports,
            GeneratedFiles: files,
            ManualChecksStillRequired:
            [
                "copy each complete maps/<map-id> directory to the Windows ETS2 map workspace without editing native geometry",
                "open every generated map in ETS2 1.60.1.7 Map Editor on Windows 11 x64",
                "inspect cardinal/oblique direction, endpoint positions, scale and isolated-road orientation",
                "run Map > Recompute map and classify all warnings/errors",
                "save, close the editor completely, reopen every saved map, and repeat the visual checks",
                "run --validate-editor-save against the saved one-directory-per-map root and retain its numeric report",
                "treat screenshots as supporting evidence only; the post-editor numeric readback must remain <= 0.001 m",
            ]);

        var reportPath = Path.Combine(outputRoot, "adapter-validation.json");
        WriteJson(reportPath, report);

        Console.WriteLine(automaticPassed
            ? "ADAPTER_AUTOMATIC_VALIDATION_PASSED"
            : "ADAPTER_AUTOMATIC_VALIDATION_FAILED");
        Console.WriteLine($"Gate status: {(automaticPassed ? "AWAITING_MANUAL_VALIDATION" : "FAIL")}");
        Console.WriteLine($"Maps: {mapReports.Count.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Roads: {allRoads.Length.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine(
            $"Max double-to-float error: {FormatMetres(maxima.DoubleToFloat3dMetres)} m");
        Console.WriteLine(
            $"Max generated readback error: {FormatMetres(maxima.GeneratedReadback3dMetres)} m");
        Console.WriteLine(
            $"Max straight-segment deviation: {FormatMetres(maxima.StraightSegmentHausdorffMetres)} m");
        Console.WriteLine($"Report: {reportPath}");
        return automaticPassed ? 0 : 2;
    }

    private static int GenerateRevised(
        string generationIdArgument,
        string neutralJsonArgument,
        string outputRootArgument)
    {
        ValidateSafeId(generationIdArgument, "generation id");
        var generationId = generationIdArgument;
        var neutralJsonPath = Path.GetFullPath(neutralJsonArgument);
        var outputRoot = Path.GetFullPath(outputRootArgument);
        var environment = ReadEnvironment(requireRevisedRuntime: true);
        var model = LoadAndValidateRevisedModel(neutralJsonPath);
        EnsureFreshOutputRoot(outputRoot);

        var mapReports = new List<GeneratedMapReport>();
        var q256Roads = new List<RevisedQ256RoadReport>();
        foreach (var neutralMap in model.Maps)
        {
            mapReports.Add(GenerateMap(neutralMap, outputRoot, q256Roads));
        }

        var allRoads = mapReports.SelectMany(map => map.Roads).ToArray();
        var allEndpoints = q256Roads
            .SelectMany(road => new[] { road.Backward, road.Forward })
            .ToArray();
        var allAxes = allEndpoints.SelectMany(endpoint => endpoint.Axes).ToArray();
        var probes = QuantizerRca.ValidateSerializerProbes();
        var maxima = new NumericalMaxima(
            DoubleToFloat3dMetres: MaxOrZero(allRoads.Select(road => road.Errors.DoubleToFloatMax3dMetres)),
            GeneratedReadback3dMetres: MaxOrZero(allRoads.Select(road => road.Errors.GeneratedReadbackMax3dMetres)),
            FloatSerialization3dMetres: MaxOrZero(allRoads.Select(road => road.Errors.FloatSerializationMax3dMetres)),
            StraightSegmentHausdorffMetres: MaxOrZero(allRoads.Select(road => road.Errors.StraightSegmentHausdorffMetres)),
            NativePlanarRadiusMetres: MaxOrZero(allRoads.Select(road => road.NativePlanarRadiusMaxMetres)));
        var truncationHorizontalBound = Math.Sqrt(2.0) * QuantizerRca.GridStepMetres;
        var truncationThreeDimensionalBound = Math.Sqrt(3.0) * QuantizerRca.GridStepMetres;
        var maximumGeneratedAxisLoss = MaxOrZero(allAxes.Select(axis => axis.AbsoluteQuantizationLossMetres));
        var maximumGeneratedHorizontalLoss = MaxOrZero(
            allEndpoints.Select(endpoint => endpoint.HorizontalXzQuantizationLossMetres));
        var maximumGeneratedThreeDimensionalLoss = MaxOrZero(
            allEndpoints.Select(endpoint => endpoint.ThreeDimensionalQuantizationLossMetres));
        var maximumProbeAxisLoss = MaxOrZero(probes.Select(probe => probe.AbsoluteAxisErrorMetres));
        var exactGeneratedAxisCount = allAxes.Count(axis => axis.ExactCodeAgreement);
        var failures = new List<string>();

        AddThresholdFailure(
            failures,
            "double-to-float conversion",
            maxima.DoubleToFloat3dMetres,
            NativeConversionThresholdMetres);
        AddThresholdFailure(
            failures,
            "native straight-road geometry",
            maxima.StraightSegmentHausdorffMetres,
            StraightRoadThresholdMetres);
        AddThresholdFailure(
            failures,
            "native planar radius",
            maxima.NativePlanarRadiusMetres,
            NativeRadiusThresholdMetres);
        if (exactGeneratedAxisCount != allAxes.Length)
        {
            failures.Add(
                $"Exact Q256 agreement held for {exactGeneratedAxisCount.ToString(CultureInfo.InvariantCulture)} "
                + $"of {allAxes.Length.ToString(CultureInfo.InvariantCulture)} generated endpoint axes.");
        }
        AddStrictBoundFailure(
            failures,
            "generated Q256 per-axis loss",
            maximumGeneratedAxisLoss,
            QuantizerRca.GridStepMetres);
        AddStrictBoundFailure(
            failures,
            "generated Q256 horizontal X/Z loss",
            maximumGeneratedHorizontalLoss,
            truncationHorizontalBound);
        AddStrictBoundFailure(
            failures,
            "generated Q256 3D loss",
            maximumGeneratedThreeDimensionalLoss,
            truncationThreeDimensionalBound);
        AddStrictBoundFailure(
            failures,
            "direct-probe Q256 per-axis loss",
            maximumProbeAxisLoss,
            QuantizerRca.GridStepMetres);

        foreach (var axis in new[] { "X", "Y", "Z" })
        {
            var axisProbes = probes.Where(probe => probe.Axis == axis).ToArray();
            if (axisProbes.Length != 15
                || !axisProbes.Any(probe => probe.InputFloat > 0)
                || !axisProbes.Any(probe => probe.InputFloat < 0)
                || !axisProbes.Any(probe => probe.InputFloat == 0))
            {
                failures.Add($"Direct Q256 probes do not cover positive, negative and zero values on {axis}.");
            }
        }

        var automaticPassed = failures.Count == 0;
        var q256Summary = new RevisedQ256Summary(
            Rule: "expected_q = trunc_toward_zero(float32_axis * 256f)",
            Readback: "expected_native_axis = expected_q / 256f",
            GridStepMetres: QuantizerRca.GridStepMetres,
            PerAxisStrictUpperBoundMetres: QuantizerRca.GridStepMetres,
            HorizontalXzStrictUpperBoundMetres: truncationHorizontalBound,
            ThreeDimensionalStrictUpperBoundMetres: truncationThreeDimensionalBound,
            GeneratedNodeCount: allEndpoints.Length,
            GeneratedAxisCount: allAxes.Length,
            ExactGeneratedAxisAgreementCount: exactGeneratedAxisCount,
            DirectProbeCount: probes.Count,
            DirectProbeAxes: ["X", "Y", "Z"],
            MaximumGeneratedAxisLossMetres: maximumGeneratedAxisLoss,
            MaximumGeneratedHorizontalXzLossMetres: maximumGeneratedHorizontalLoss,
            MaximumGeneratedThreeDimensionalLossMetres: maximumGeneratedThreeDimensionalLoss,
            MaximumDirectProbeAxisLossMetres: maximumProbeAxisLoss);

        var nativeFiles = InventoryFiles(Path.Combine(outputRoot, "maps"));
        var semanticEvidence = BuildRevisedSemanticEvidence(
            model,
            neutralJsonPath,
            automaticPassed,
            maxima,
            q256Summary,
            probes,
            mapReports,
            q256Roads,
            nativeFiles,
            environment);
        var semanticPath = Path.Combine(outputRoot, "semantic-validation.json");
        WriteJson(semanticPath, semanticEvidence);
        var report = new RevisedAdapterValidationReport(
            SchemaVersion: 2,
            CriteriaId: RevisedCriteriaId,
            Poc: model.Poc,
            HistoricalV1Status: "FAIL",
            GenerationId: generationId,
            OutputRoot: outputRoot,
            GenerationAutomaticValidation: automaticPassed ? "PASS" : "FAIL",
            RerunState: automaticPassed
                ? "AUTOMATIC_GENERATION_PASSED; REPRODUCIBILITY_COMPARISON_REQUIRED"
                : "FAIL",
            Input: new InputEvidence(neutralJsonPath, Sha256(neutralJsonPath)),
            Environment: environment,
            CoordinateContract: new CoordinateContractReport(
                SourceAxes: ["E", "N", "H"],
                Unit: ExpectedSourceUnit,
                CandidateMapping: new MappingReport("E", "H", "-N"),
                AdapterOperation: "double (E,N,H) -> double (X=E,Y=H,Z=-N) -> System.Numerics.Vector3(float) -> TruckLib Node.Position Q256",
                SemanticStatus: "ARITHMETIC_VALIDATED; MAP_EDITOR_GEOGRAPHIC_SEMANTICS_PENDING"),
            Thresholds: new RevisedThresholdReport(
                Float64ToFloat32ThreeDimensionalMetres: NativeConversionThresholdMetres,
                ExactQ256IntegerCodeAgreementRequired: true,
                StraightRoadHausdorffMetres: StraightRoadThresholdMetres,
                NativePlanarRadiusMetres: NativeRadiusThresholdMetres),
            Maxima: maxima,
            Q256: q256Summary,
            Criteria: BuildRevisedCriteria(maxima, q256Summary, probes),
            Failures: failures,
            DirectSerializerProbes: probes,
            Q256Roads: q256Roads,
            Maps: mapReports,
            NativeFiles: nativeFiles,
            SemanticEvidence: new SemanticEvidenceReference(
                Path: Path.GetFileName(semanticPath),
                Sha256: Sha256(semanticPath),
                Excludes:
                [
                    "random TruckLib map/item/node UIDs",
                    "native binary SHA-256 values",
                    "absolute paths",
                ]),
            ManualChecksStillRequired:
            [
                "preserve this pre-editor output and its node identities/Q256 codes",
                "complete the Windows 11 x64 ETS2 1.60.1.7 Map Editor open/inspect/Recompute/save/close/reopen cycle",
                "validate q_after == q_before == q_expected component by component with no additional Q256 allowance",
                "confirm or reject the geographic semantics of X=E, Y=H, Z=-N visually",
            ]);
        var reportPath = Path.Combine(outputRoot, "adapter-validation-v2.json");
        WriteJson(reportPath, report);

        Console.WriteLine(automaticPassed
            ? "REVISED_GENERATION_AUTOMATIC_VALIDATION_PASSED"
            : "REVISED_GENERATION_AUTOMATIC_VALIDATION_FAILED");
        Console.WriteLine($"Rerun state: {report.RerunState}");
        Console.WriteLine($"Maps: {mapReports.Count.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Roads: {allRoads.Length.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Generated Q256 axes: {allAxes.Length.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Direct Q256 probes: {probes.Count.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Semantic evidence SHA-256: {Sha256(semanticPath)}");
        Console.WriteLine($"Report: {reportPath}");
        return automaticPassed ? 0 : 2;
    }

    private static GeneratedMapReport GenerateMap(
        NeutralMap neutralMap,
        string outputRoot,
        List<RevisedQ256RoadReport>? revisedQ256Reports)
    {
        var mapDirectory = Path.Combine(outputRoot, "maps", neutralMap.Id);
        if (Directory.Exists(mapDirectory))
        {
            throw new IOException($"Native map directory already exists; use a fresh output root: {mapDirectory}");
        }

        Directory.CreateDirectory(mapDirectory);
        var map = new Map();
        var pending = new List<PendingRoad>();

        foreach (var neutralRoad in neutralMap.Roads)
        {
            var expectedBackward = ApplyCandidateMapping(neutralRoad.Backward);
            var expectedForward = ApplyCandidateMapping(neutralRoad.Forward);
            var vectorBackward = ToVector3(expectedBackward);
            var vectorForward = ToVector3(expectedForward);
            Require(vectorBackward != vectorForward,
                $"Road '{neutralMap.Id}/{neutralRoad.Id}' collapses after native float conversion.");

            var road = Road.Add(
                map,
                vectorBackward,
                vectorForward,
                RoadType,
                leftTerrainSize: 0,
                rightTerrainSize: 0);
            road.Right.Look = RoadLook;
            road.Right.Variant = RoadVariant;
            road.Right.LeftEdge = RoadEdge;
            road.Right.RightEdge = RoadEdge;
            ValidateInMemoryRoad(map, road, vectorBackward, vectorForward);

            var writtenBackwardQ256 = revisedQ256Reports is null
                ? null
                : QuantizerRca.SerializePositionCodes(RequireConcreteNode(road.Node));
            var writtenForwardQ256 = revisedQ256Reports is null
                ? null
                : QuantizerRca.SerializePositionCodes(RequireConcreteNode(road.ForwardNode));

            pending.Add(new PendingRoad(
                neutralRoad,
                expectedBackward,
                expectedForward,
                vectorBackward,
                vectorForward,
                road.Uid,
                road.Node.Uid,
                road.ForwardNode.Uid,
                writtenBackwardQ256,
                writtenForwardQ256));
        }

        ValidateMapIsolation(map, neutralMap.Roads.Count);
        map.Save(mapDirectory, neutralMap.Id, cleanSectorDirectory: true);

        var mbdPath = Path.Combine(mapDirectory, $"{neutralMap.Id}.mbd");
        Require(File.Exists(mbdPath), $"TruckLib did not write the expected .mbd file: {mbdPath}");
        var actualMapFormat = ReadMapFormat(mbdPath);
        Require(actualMapFormat == ExpectedMapFormat,
            $"Map '{neutralMap.Id}' format is {actualMapFormat}, expected {ExpectedMapFormat}.");

        var reopenedMap = Map.Open(mbdPath);
        ValidateMapIsolation(reopenedMap, neutralMap.Roads.Count);

        var roadReports = new List<GeneratedRoadReport>();
        foreach (var item in pending)
        {
            if (!reopenedMap.MapItems.TryGetValue(item.RoadUid, out var reopenedItem))
            {
                throw new InvalidOperationException(
                    $"Road UID {Hex(item.RoadUid)} disappeared from map '{neutralMap.Id}'.");
            }

            if (reopenedItem is not Road reopenedRoad)
            {
                throw new InvalidOperationException(
                    $"UID {Hex(item.RoadUid)} is no longer a Road in map '{neutralMap.Id}'.");
            }
            ValidateReopenedRoad(reopenedMap, reopenedRoad, item);

            if (revisedQ256Reports is not null)
            {
                revisedQ256Reports.Add(BuildRevisedQ256RoadReport(
                    neutralMap.Id,
                    item,
                    reopenedRoad));
            }

            var reopenedBackward = NativePoint.FromVector3(reopenedRoad.Node.Position);
            var reopenedForward = NativePoint.FromVector3(reopenedRoad.ForwardNode.Position);
            var doubleToFloatBackward = Distance3d(item.ExpectedBackward, NativePoint.FromVector3(item.VectorBackward));
            var doubleToFloatForward = Distance3d(item.ExpectedForward, NativePoint.FromVector3(item.VectorForward));
            var generatedReadbackBackward = Distance3d(item.ExpectedBackward, reopenedBackward);
            var generatedReadbackForward = Distance3d(item.ExpectedForward, reopenedForward);
            var floatSerializationBackward = Distance3d(NativePoint.FromVector3(item.VectorBackward), reopenedBackward);
            var floatSerializationForward = Distance3d(NativePoint.FromVector3(item.VectorForward), reopenedForward);
            var straightHausdorff = SegmentHausdorff(
                item.ExpectedBackward,
                item.ExpectedForward,
                reopenedBackward,
                reopenedForward);
            var orientationError = OrientationErrorDegrees(
                item.ExpectedBackward,
                item.ExpectedForward,
                reopenedBackward,
                reopenedForward);
            var maxRadius = new[]
            {
                PlanarRadius(item.ExpectedBackward),
                PlanarRadius(item.ExpectedForward),
                PlanarRadius(reopenedBackward),
                PlanarRadius(reopenedForward),
            }.Max();

            roadReports.Add(new GeneratedRoadReport(
                Id: item.NeutralRoad.Id,
                SourceFixtureId: item.NeutralRoad.SourceFixtureId,
                Scale: item.NeutralRoad.Scale,
                Uid: Hex(reopenedRoad.Uid),
                Assets: new RoadAssetReport(
                    Type: reopenedRoad.RoadType.ToString(),
                    Look: reopenedRoad.Right.Look.ToString(),
                    Variant: reopenedRoad.Right.Variant.ToString(),
                    LeftEdge: reopenedRoad.Right.LeftEdge.ToString(),
                    RightEdge: reopenedRoad.Right.RightEdge.ToString()),
                ExpectedNativeDouble: new SegmentReport(item.ExpectedBackward, item.ExpectedForward),
                Vector3Input: new SegmentReport(
                    NativePoint.FromVector3(item.VectorBackward),
                    NativePoint.FromVector3(item.VectorForward)),
                GeneratedReadback: new SegmentReport(reopenedBackward, reopenedForward),
                BackwardNode: NodeReport.FromNode(reopenedRoad.Node),
                ForwardNode: NodeReport.FromNode(reopenedRoad.ForwardNode),
                LengthReadbackMetres: reopenedRoad.Length,
                OrientationErrorDegrees: orientationError,
                NativePlanarRadiusMaxMetres: maxRadius,
                Errors: new GeneratedErrorReport(
                    DoubleToFloatBackward3dMetres: doubleToFloatBackward,
                    DoubleToFloatForward3dMetres: doubleToFloatForward,
                    DoubleToFloatMax3dMetres: Math.Max(doubleToFloatBackward, doubleToFloatForward),
                    GeneratedReadbackBackward3dMetres: generatedReadbackBackward,
                    GeneratedReadbackForward3dMetres: generatedReadbackForward,
                    GeneratedReadbackMax3dMetres: Math.Max(generatedReadbackBackward, generatedReadbackForward),
                    FloatSerializationBackward3dMetres: floatSerializationBackward,
                    FloatSerializationForward3dMetres: floatSerializationForward,
                    FloatSerializationMax3dMetres: Math.Max(floatSerializationBackward, floatSerializationForward),
                    StraightSegmentHausdorffMetres: straightHausdorff)));
        }

        return new GeneratedMapReport(
            Id: neutralMap.Id,
            MbdPath: RelativePath(outputRoot, mbdPath),
            SectorDirectory: RelativePath(outputRoot, Path.Combine(mapDirectory, neutralMap.Id)),
            MapFormat: actualMapFormat,
            EditorMapUid: Hex(reopenedMap.EditorMapId),
            SectorCountObserved: reopenedMap.Sectors.Count,
            RoadCount: roadReports.Count,
            NodeCount: reopenedMap.Nodes.Count,
            AllUidsNonZeroAndUnique: true,
            RoadsUsePrivateTerminalNodes: true,
            Roads: roadReports);
    }

    private static int ValidateEditorSave(
        string savedMapRootArgument,
        string neutralJsonArgument,
        string outputReportArgument)
    {
        var savedMapRoot = Path.GetFullPath(savedMapRootArgument);
        var neutralJsonPath = Path.GetFullPath(neutralJsonArgument);
        var outputReportPath = Path.GetFullPath(outputReportArgument);
        Require(Directory.Exists(savedMapRoot), $"Saved-map root does not exist: {savedMapRoot}");
        var model = LoadAndValidateModel(neutralJsonPath);
        EnsureParentDirectory(outputReportPath);

        var mapReports = new List<EditorMapReport>();
        var failures = new List<string>();
        foreach (var neutralMap in model.Maps)
        {
            var mbdPath = Path.Combine(savedMapRoot, "maps", neutralMap.Id, $"{neutralMap.Id}.mbd");
            Require(File.Exists(mbdPath),
                $"Expected editor-saved map is missing. Preserve layout maps/<map-id>/<map-id>.mbd: {mbdPath}");
            var mapFormat = ReadMapFormat(mbdPath);
            Require(mapFormat == ExpectedMapFormat,
                $"Editor-saved map '{neutralMap.Id}' format is {mapFormat}, expected {ExpectedMapFormat}.");

            var map = Map.Open(mbdPath);
            ValidateMapIsolation(map, neutralMap.Roads.Count);
            var actualRoads = map.MapItems.Values.OfType<Road>().OrderBy(road => road.Uid).ToArray();
            var assignment = AssignRoadsByGeometry(neutralMap.Roads, actualRoads);
            var roadReports = new List<EditorRoadReport>();

            for (var expectedIndex = 0; expectedIndex < neutralMap.Roads.Count; expectedIndex++)
            {
                var neutralRoad = neutralMap.Roads[expectedIndex];
                var road = actualRoads[assignment[expectedIndex]];
                ValidateRoadStructure(map, road);
                ValidateRoadAssets(road);

                var expectedBackward = ApplyCandidateMapping(neutralRoad.Backward);
                var expectedForward = ApplyCandidateMapping(neutralRoad.Forward);
                var actualBackward = NativePoint.FromVector3(road.Node.Position);
                var actualForward = NativePoint.FromVector3(road.ForwardNode.Position);
                var backwardError = Distance3d(expectedBackward, actualBackward);
                var forwardError = Distance3d(expectedForward, actualForward);
                var maxError = Math.Max(backwardError, forwardError);
                var reverseMaxError = Math.Max(
                    Distance3d(expectedBackward, actualForward),
                    Distance3d(expectedForward, actualBackward));
                var directionPreserved = maxError <= reverseMaxError;
                var straightHausdorff = SegmentHausdorff(
                    expectedBackward,
                    expectedForward,
                    actualBackward,
                    actualForward);
                var orientationError = OrientationErrorDegrees(
                    expectedBackward,
                    expectedForward,
                    actualBackward,
                    actualForward);
                var maxRadius = new[]
                {
                    PlanarRadius(expectedBackward),
                    PlanarRadius(expectedForward),
                    PlanarRadius(actualBackward),
                    PlanarRadius(actualForward),
                }.Max();

                AddRoadFailure(
                    failures,
                    neutralMap.Id,
                    neutralRoad.Id,
                    "post-editor numeric readback",
                    maxError,
                    NativeConversionThresholdMetres);
                AddRoadFailure(
                    failures,
                    neutralMap.Id,
                    neutralRoad.Id,
                    "post-editor straight-segment geometry",
                    straightHausdorff,
                    StraightRoadThresholdMetres);
                AddRoadFailure(
                    failures,
                    neutralMap.Id,
                    neutralRoad.Id,
                    "post-editor native planar radius",
                    maxRadius,
                    NativeRadiusThresholdMetres);
                if (!directionPreserved)
                {
                    failures.Add($"Road '{neutralMap.Id}/{neutralRoad.Id}' direction was reversed after editor save.");
                }

                roadReports.Add(new EditorRoadReport(
                    Id: neutralRoad.Id,
                    SourceFixtureId: neutralRoad.SourceFixtureId,
                    Uid: Hex(road.Uid),
                    MatchedBy: "globally minimum undirected endpoint error; ordered endpoints validate direction",
                    ExpectedNativeDouble: new SegmentReport(expectedBackward, expectedForward),
                    EditorReadback: new SegmentReport(actualBackward, actualForward),
                    BackwardNode: NodeReport.FromNode(road.Node),
                    ForwardNode: NodeReport.FromNode(road.ForwardNode),
                    DirectionPreserved: directionPreserved,
                    OrientationErrorDegrees: orientationError,
                    Backward3dErrorMetres: backwardError,
                    Forward3dErrorMetres: forwardError,
                    Max3dErrorMetres: maxError,
                    StraightSegmentHausdorffMetres: straightHausdorff,
                    NativePlanarRadiusMaxMetres: maxRadius));
            }

            mapReports.Add(new EditorMapReport(
                Id: neutralMap.Id,
                MbdPath: mbdPath,
                MapFormat: mapFormat,
                EditorMapUid: Hex(map.EditorMapId),
                SectorCountObserved: map.Sectors.Count,
                RoadCount: actualRoads.Length,
                NodeCount: map.Nodes.Count,
                Roads: roadReports));
        }

        var allRoads = mapReports.SelectMany(map => map.Roads).ToArray();
        var maximumReadbackError = MaxOrZero(allRoads.Select(road => road.Max3dErrorMetres));
        var maximumStraightDeviation = MaxOrZero(
            allRoads.Select(road => road.StraightSegmentHausdorffMetres));
        var maximumRadius = MaxOrZero(allRoads.Select(road => road.NativePlanarRadiusMaxMetres));
        var numericPassed = failures.Count == 0;
        var report = new EditorValidationReport(
            SchemaVersion: 1,
            Poc: model.Poc,
            GateStatus: numericPassed ? "AWAITING_MANUAL_VALIDATION" : "FAIL",
            NumericPostEditorValidation: numericPassed ? "PASS" : "FAIL",
            ImportantLimitation:
                "TruckLib numeric readback is diagnostic. This report does not prove that the required visual inspection, Recompute map, save, complete editor close and reopen cycle occurred.",
            Input: new InputEvidence(neutralJsonPath, Sha256(neutralJsonPath)),
            Environment: ReadEnvironment(),
            SavedMapRoot: savedMapRoot,
            MatchingMethod:
                "Globally minimum one-to-one assignment by undirected endpoint error within each map; original node order is then checked independently.",
            Thresholds: new ThresholdReport(
                NativeConversionThresholdMetres,
                StraightRoadThresholdMetres,
                NativeRadiusThresholdMetres),
            MaxPostEditorReadback3dMetres: maximumReadbackError,
            MaxStraightSegmentHausdorffMetres: maximumStraightDeviation,
            MaxNativePlanarRadiusMetres: maximumRadius,
            Failures: failures,
            Maps: mapReports,
            ReadFiles: InventoryFiles(savedMapRoot));
        WriteJson(outputReportPath, report);

        Console.WriteLine(numericPassed
            ? "EDITOR_SAVE_NUMERIC_READBACK_PASSED"
            : "EDITOR_SAVE_NUMERIC_READBACK_FAILED");
        Console.WriteLine($"Gate status: {(numericPassed ? "AWAITING_MANUAL_VALIDATION" : "FAIL")}");
        Console.WriteLine($"Max post-editor readback error: {FormatMetres(maximumReadbackError)} m");
        Console.WriteLine($"Report: {outputReportPath}");
        return numericPassed ? 0 : 2;
    }

    private static int RunSelfTest()
    {
        var mapped = ApplyCandidateMapping(new NeutralPoint { E = 3.25, N = -7.5, H = 1.125 });
        Require(mapped == new NativePoint(3.25, 1.125, 7.5), "Candidate mapping self-test failed.");
        var vector = ToVector3(mapped);
        Require(vector == new Vector3(3.25f, 1.125f, 7.5f), "Vector3 conversion self-test failed.");

        var validModel = CreateSelfTestModel();
        ValidateModel(validModel);

        var validRevisedModel = CreateSelfTestModel();
        validRevisedModel.SchemaVersion = 2;
        validRevisedModel.CoordinateSystem.SourceAxes = null;
        validRevisedModel.CoordinateSystem.Axes = ["E", "N", "H"];
        validRevisedModel.CoordinateSystem.CandidateMapping = null;
        ValidateModel(validRevisedModel, revisedSchema: true);

        var invalidAxes = CreateSelfTestModel();
        invalidAxes.CoordinateSystem.SourceAxes![0] = "N";
        ExpectThrows<InvalidDataException>(() => ValidateModel(invalidAxes), "schema axes rejection");

        var invalidMapping = CreateSelfTestModel();
        invalidMapping.CoordinateSystem.CandidateMapping!.Z = "N";
        ExpectThrows<InvalidDataException>(() => ValidateModel(invalidMapping), "candidate mapping rejection");

        var missingV1Mapping = CreateSelfTestModel();
        missingV1Mapping.CoordinateSystem.CandidateMapping = null;
        ExpectThrows<InvalidDataException>(
            () => ValidateModel(missingV1Mapping),
            "schema v1 missing candidate mapping rejection");

        var pollutedRevisedBoundary = CreateSelfTestModel();
        pollutedRevisedBoundary.SchemaVersion = 2;
        pollutedRevisedBoundary.CoordinateSystem.Axes = ["E", "N", "H"];
        ExpectThrows<InvalidDataException>(
            () => ValidateModel(pollutedRevisedBoundary, revisedSchema: true),
            "schema v2 ETS2 mapping rejection");

        var nonFinite = CreateSelfTestModel();
        nonFinite.Maps[0].Roads[0].Backward = new NeutralPoint { E = double.NaN, N = 0, H = 0 };
        ExpectThrows<InvalidDataException>(() => ValidateModel(nonFinite), "non-finite input rejection");

        const string unknownMemberJson =
            "{\"schemaVersion\":1,\"poc\":\"PoC-002 self-test\",\"coordinateSystem\":{"
            + "\"sourceAxes\":[\"E\",\"N\",\"H\"],\"unit\":\"scene metre\","
            + "\"candidateMapping\":{\"x\":\"E\",\"y\":\"H\",\"z\":\"-N\"}},"
            + "\"maps\":[],\"unexpected\":true}";
        ExpectThrows<JsonException>(
            () => JsonSerializer.Deserialize<NeutralRoot>(unknownMemberJson, JsonOptions),
            "unknown schema member rejection");

        var quantizerProbeCount = QuantizerRca.ValidateSerializerProbes().Count;
        var expectedQ256 = QuantizerRca.ExpectedPosition(vector);
        var expectedQ256Readback = QuantizerRca.Decode(expectedQ256);
        Require(
            QuantizerRca.ReconstructReadbackCodes(expectedQ256Readback) == expectedQ256,
            "Q256 expected-code/readback reconstruction self-test failed.");
        ValidateSafeId("generation-a", "generation id");
        ExpectThrows<InvalidDataException>(
            () => ValidateSafeId("../generation-a", "generation id"),
            "unsafe generation id rejection");
        ValidateRevisedRuntimeVersion("10.0.11");
        ExpectThrows<InvalidOperationException>(
            () => ValidateRevisedRuntimeVersion("10.0.10"),
            "revised runtime mismatch rejection");
        RevisedGenerationComparison.RunSelfTest();
        RevisedEditorValidation.RunSelfTest();

        Console.WriteLine("SELF_TEST_PASSED");
        Console.WriteLine(
            "Checks: candidate mapping, Vector3 conversion, schema/axes/mapping, non-finite rejection, "
            + $"unknown-member rejection, {quantizerProbeCount} direct Node Q256 serializer probes, "
            + "neutral v1/v2 boundary separation, Q256 code reconstruction, generation/runtime guards, "
            + "semantic comparison, aggregate/editor binding");
        return 0;
    }

    private static NeutralRoot CreateSelfTestModel() => new()
    {
        SchemaVersion = 1,
        Poc = ExpectedPoc,
        CoordinateSystem = new CoordinateSystemContract
        {
            SourceAxes = ["E", "N", "H"],
            Unit = ExpectedSourceUnit,
            CandidateMapping = new CandidateMapping { X = "E", Y = "H", Z = "-N" },
        },
        Maps =
        [
            new NeutralMap
            {
                Id = "self-test",
                Roads =
                [
                    new NeutralRoad
                    {
                        Id = "road-1",
                        SourceFixtureId = "fixture-1",
                        Scale = 1.0,
                        Backward = new NeutralPoint { E = 0, N = 0, H = 0 },
                        Forward = new NeutralPoint { E = 100, N = 0, H = 0 },
                    },
                ],
            },
        ],
    };

    private static NeutralRoot LoadAndValidateModel(string path)
    {
        Require(File.Exists(path), $"Neutral JSON does not exist: {path}");
        var model = JsonSerializer.Deserialize<NeutralRoot>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Neutral JSON deserialized to null.");
        ValidateModel(model, revisedSchema: false);
        return model;
    }

    private static NeutralRoot LoadAndValidateRevisedModel(string path)
    {
        Require(File.Exists(path), $"Revised neutral JSON does not exist: {path}");
        var model = JsonSerializer.Deserialize<NeutralRoot>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Revised neutral JSON deserialized to null.");
        ValidateModel(model, revisedSchema: true);
        return model;
    }

    private static void ValidateModel(NeutralRoot model) =>
        ValidateModel(model, revisedSchema: false);

    private static void ValidateModel(NeutralRoot model, bool revisedSchema)
    {
        var expectedSchemaVersion = revisedSchema ? 2 : ExpectedSchemaVersion;
        if (model.SchemaVersion != expectedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported schemaVersion {model.SchemaVersion}; expected {expectedSchemaVersion}.");
        }

        if (model.Poc != ExpectedPoc)
        {
            throw new InvalidDataException($"poc must be exactly '{ExpectedPoc}'.");
        }

        if (revisedSchema)
        {
            if (model.CoordinateSystem.SourceAxes is not null
                || model.CoordinateSystem.CandidateMapping is not null
                || model.CoordinateSystem.Axes is null
                || !model.CoordinateSystem.Axes.SequenceEqual(["E", "N", "H"], StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Revised coordinateSystem must contain only axes ['E','N','H'] and unit; "
                    + "sourceAxes/candidateMapping are forbidden at the neutral boundary.");
            }
        }
        else
        {
            if (model.CoordinateSystem.Axes is not null
                || model.CoordinateSystem.SourceAxes is null
                || !model.CoordinateSystem.SourceAxes.SequenceEqual(["E", "N", "H"], StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "coordinateSystem.sourceAxes must be exactly ['E','N','H'] for schemaVersion 1.");
            }

            var mapping = model.CoordinateSystem.CandidateMapping;
            if (mapping is null || mapping.X != "E" || mapping.Y != "H" || mapping.Z != "-N")
            {
                throw new InvalidDataException(
                    "coordinateSystem.candidateMapping must be exactly {x:'E',y:'H',z:'-N'} "
                    + "for schemaVersion 1.");
            }
        }

        if (model.CoordinateSystem.Unit != ExpectedSourceUnit)
        {
            throw new InvalidDataException(
                $"coordinateSystem.unit must be exactly '{ExpectedSourceUnit}'.");
        }

        if (model.Maps.Count == 0)
        {
            throw new InvalidDataException("maps must contain at least one map fixture.");
        }

        var mapIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var map in model.Maps)
        {
            ValidateSafeId(map.Id, "map id");
            if (!mapIds.Add(map.Id))
            {
                throw new InvalidDataException($"Duplicate map id '{map.Id}'.");
            }

            if (map.Roads.Count == 0)
            {
                throw new InvalidDataException($"Map '{map.Id}' must contain at least one road.");
            }

            var roadIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var road in map.Roads)
            {
                ValidateSafeId(road.Id, $"road id in map '{map.Id}'");
                ValidateSafeId(road.SourceFixtureId, $"sourceFixtureId for road '{map.Id}/{road.Id}'");
                if (!roadIds.Add(road.Id))
                {
                    throw new InvalidDataException($"Duplicate road id '{map.Id}/{road.Id}'.");
                }

                if (!double.IsFinite(road.Scale) || road.Scale <= 0)
                {
                    throw new InvalidDataException($"Road '{map.Id}/{road.Id}' has invalid scale {road.Scale}.");
                }

                ValidatePoint(road.Backward, map.Id, road.Id, "backward");
                ValidatePoint(road.Forward, map.Id, road.Id, "forward");
                _ = ToVector3(ApplyCandidateMapping(road.Backward));
                _ = ToVector3(ApplyCandidateMapping(road.Forward));
            }
        }
    }

    private static void ValidatePoint(NeutralPoint point, string mapId, string roadId, string endpoint)
    {
        if (!double.IsFinite(point.E) || !double.IsFinite(point.N) || !double.IsFinite(point.H))
        {
            throw new InvalidDataException(
                $"Road '{mapId}/{roadId}' {endpoint} endpoint contains a non-finite E/N/H value.");
        }
    }

    private static void ValidateSafeId(string value, string role)
    {
        if (value.Length is < 1 or > 64
            || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new InvalidDataException(
                $"Invalid {role} '{value}'; use 1-64 ASCII letters, digits, '.', '_' or '-'.");
        }
    }

    private static NativePoint ApplyCandidateMapping(NeutralPoint point) =>
        new(point.E, point.H, -point.N);

    private static Vector3 ToVector3(NativePoint point)
    {
        var vector = new Vector3((float)point.X, (float)point.Y, (float)point.Z);
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
        {
            throw new InvalidDataException("Candidate native coordinates cannot be represented by Vector3.");
        }

        return vector;
    }

    private static void ValidateInMemoryRoad(
        Map map,
        Road road,
        Vector3 expectedBackward,
        Vector3 expectedForward)
    {
        Require(road.Node.Position == expectedBackward, "Unexpected in-memory backward-node position.");
        Require(road.ForwardNode.Position == expectedForward, "Unexpected in-memory forward-node position.");
        ValidateRoadStructure(map, road);
        ValidateRoadAssets(road);
    }

    private static Node RequireConcreteNode(INode node) =>
        node as Node
        ?? throw new InvalidOperationException(
            $"TruckLib road node {Hex(node.Uid)} is not a serializable Node instance.");

    private static void ValidateReopenedRoad(Map map, Road road, PendingRoad expected)
    {
        Require(road.Uid == expected.RoadUid, "Road UID changed during generated readback.");
        Require(road.Node.Uid == expected.BackwardNodeUid,
            $"Backward-node UID changed for road {Hex(road.Uid)}.");
        Require(road.ForwardNode.Uid == expected.ForwardNodeUid,
            $"Forward-node UID changed for road {Hex(road.Uid)}.");
        ValidateRoadStructure(map, road);
        ValidateRoadAssets(road);
    }

    private static void ValidateRoadStructure(Map map, Road road)
    {
        Require(ReferenceEquals(road.Node.ForwardItem, road),
            $"Backward node {Hex(road.Node.Uid)} does not reference road {Hex(road.Uid)}.");
        Require(ReferenceEquals(road.ForwardNode.BackwardItem, road),
            $"Forward node {Hex(road.ForwardNode.Uid)} does not reference road {Hex(road.Uid)}.");
        Require(road.Node.BackwardItem is null,
            $"Backward terminal {Hex(road.Node.Uid)} has an unexpected backward item.");
        Require(road.ForwardNode.ForwardItem is null,
            $"Forward terminal {Hex(road.ForwardNode.Uid)} has an unexpected forward item.");
        Require(map.Nodes.TryGetValue(road.Node.Uid, out var storedBackward)
                && ReferenceEquals(storedBackward, road.Node),
            $"Backward node {Hex(road.Node.Uid)} is absent from the map node collection.");
        Require(map.Nodes.TryGetValue(road.ForwardNode.Uid, out var storedForward)
                && ReferenceEquals(storedForward, road.ForwardNode),
            $"Forward node {Hex(road.ForwardNode.Uid)} is absent from the map node collection.");
    }

    private static void ValidateRoadAssets(Road road)
    {
        Require(road.RoadType.ToString() == RoadType, $"Unexpected road type for {Hex(road.Uid)}.");
        Require(road.Right.Look.ToString() == RoadLook, $"Unexpected road look for {Hex(road.Uid)}.");
        Require(road.Right.Variant.ToString() == RoadVariant,
            $"Unexpected road variant for {Hex(road.Uid)}.");
        Require(road.Right.LeftEdge.ToString() == RoadEdge,
            $"Unexpected left edge for {Hex(road.Uid)}.");
        Require(road.Right.RightEdge.ToString() == RoadEdge,
            $"Unexpected right edge for {Hex(road.Uid)}.");
    }

    private static void ValidateMapIsolation(Map map, int expectedRoadCount)
    {
        var roads = map.MapItems.Values.OfType<Road>().ToArray();
        Require(map.MapItems.Count == expectedRoadCount,
            $"Map contains {map.MapItems.Count} items, expected {expectedRoadCount} isolated roads.");
        Require(roads.Length == expectedRoadCount,
            $"Map contains {roads.Length} roads, expected {expectedRoadCount}.");
        Require(map.Nodes.Count == checked(expectedRoadCount * 2),
            $"Map contains {map.Nodes.Count} nodes, expected {expectedRoadCount * 2} private terminals.");

        foreach (var road in roads)
        {
            ValidateRoadStructure(map, road);
        }

        var uids = new[] { map.EditorMapId }
            .Concat(map.MapItems.Keys)
            .Concat(map.Nodes.Keys)
            .ToArray();
        Require(uids.All(uid => uid != 0), "Map contains a zero UID.");
        Require(uids.Distinct().Count() == uids.Length, "Map contains duplicate UIDs.");
    }

    private static int[] AssignRoadsByGeometry(IReadOnlyList<NeutralRoad> expected, IReadOnlyList<Road> actual)
    {
        Require(expected.Count == actual.Count, "Expected and actual road counts differ.");
        if (expected.Count > 20)
        {
            throw new InvalidOperationException(
                "PoC-002 editor matcher intentionally supports at most 20 isolated roads per map.");
        }

        var costs = new double[expected.Count, actual.Count];
        for (var expectedIndex = 0; expectedIndex < expected.Count; expectedIndex++)
        {
            var expectedBackward = ApplyCandidateMapping(expected[expectedIndex].Backward);
            var expectedForward = ApplyCandidateMapping(expected[expectedIndex].Forward);
            for (var actualIndex = 0; actualIndex < actual.Count; actualIndex++)
            {
                var actualBackward = NativePoint.FromVector3(actual[actualIndex].Node.Position);
                var actualForward = NativePoint.FromVector3(actual[actualIndex].ForwardNode.Position);
                var ordered = Math.Max(
                    Distance3d(expectedBackward, actualBackward),
                    Distance3d(expectedForward, actualForward));
                var reversed = Math.Max(
                    Distance3d(expectedBackward, actualForward),
                    Distance3d(expectedForward, actualBackward));
                costs[expectedIndex, actualIndex] = Math.Min(ordered, reversed);
            }
        }

        var memo = new Dictionary<(int Index, ulong Used), AssignmentResult>();
        return SolveAssignment(costs, 0, 0, memo).Assignment;
    }

    private static AssignmentResult SolveAssignment(
        double[,] costs,
        int expectedIndex,
        ulong usedActual,
        Dictionary<(int Index, ulong Used), AssignmentResult> memo)
    {
        var count = costs.GetLength(0);
        if (expectedIndex == count)
        {
            return new AssignmentResult(0, []);
        }

        if (memo.TryGetValue((expectedIndex, usedActual), out var cached))
        {
            return cached;
        }

        AssignmentResult? best = null;
        for (var actualIndex = 0; actualIndex < count; actualIndex++)
        {
            var mask = 1UL << actualIndex;
            if ((usedActual & mask) != 0)
            {
                continue;
            }

            var remainder = SolveAssignment(costs, expectedIndex + 1, usedActual | mask, memo);
            var total = costs[expectedIndex, actualIndex] + remainder.Cost;
            var assignment = new int[remainder.Assignment.Length + 1];
            assignment[0] = actualIndex;
            remainder.Assignment.CopyTo(assignment, 1);
            var candidate = new AssignmentResult(total, assignment);
            if (best is null || candidate.Cost < best.Cost)
            {
                best = candidate;
            }
        }

        var result = best ?? throw new InvalidOperationException("Could not match editor-saved roads.");
        memo[(expectedIndex, usedActual)] = result;
        return result;
    }

    private static RevisedQ256RoadReport BuildRevisedQ256RoadReport(
        string mapId,
        PendingRoad pending,
        Road reopenedRoad)
    {
        var writtenBackward = pending.WrittenBackwardQ256
            ?? throw new InvalidOperationException("Missing pre-save backward Q256 evidence.");
        var writtenForward = pending.WrittenForwardQ256
            ?? throw new InvalidOperationException("Missing pre-save forward Q256 evidence.");
        return new RevisedQ256RoadReport(
            MapId: mapId,
            RoadId: pending.NeutralRoad.Id,
            SourceFixtureId: pending.NeutralRoad.SourceFixtureId,
            Scale: pending.NeutralRoad.Scale,
            Backward: BuildRevisedQ256EndpointReport(
                "backward",
                reopenedRoad.Node,
                pending.NeutralRoad.Backward,
                pending.ExpectedBackward,
                pending.VectorBackward,
                writtenBackward),
            Forward: BuildRevisedQ256EndpointReport(
                "forward",
                reopenedRoad.ForwardNode,
                pending.NeutralRoad.Forward,
                pending.ExpectedForward,
                pending.VectorForward,
                writtenForward));
    }

    private static RevisedQ256EndpointReport BuildRevisedQ256EndpointReport(
        string endpoint,
        INode reopenedNode,
        NeutralPoint neutralEnhFloat64,
        NativePoint mappedFloat64,
        Vector3 float32Input,
        QuantizerRca.EncodedPosition written)
    {
        var expected = QuantizerRca.ExpectedPosition(float32Input);
        var readback = QuantizerRca.ReconstructReadbackCodes(reopenedNode.Position);
        var expectedNative = QuantizerRca.Decode(expected);
        var axes = new List<RevisedQ256AxisReport>
        {
            BuildRevisedQ256AxisReport(
                "X", "E", mappedFloat64.X, float32Input.X, expected.X, written.X, readback.X,
                expectedNative.X, reopenedNode.Position.X),
            BuildRevisedQ256AxisReport(
                "Y", "H", mappedFloat64.Y, float32Input.Y, expected.Y, written.Y, readback.Y,
                expectedNative.Y, reopenedNode.Position.Y),
            BuildRevisedQ256AxisReport(
                "Z", "-N", mappedFloat64.Z, float32Input.Z, expected.Z, written.Z, readback.Z,
                expectedNative.Z, reopenedNode.Position.Z),
        };
        var byAxis = axes.ToDictionary(axis => axis.Axis, StringComparer.Ordinal);
        var horizontalLoss = Math.Sqrt(
            Math.Pow(byAxis["X"].AbsoluteQuantizationLossMetres, 2)
            + Math.Pow(byAxis["Z"].AbsoluteQuantizationLossMetres, 2));
        var threeDimensionalLoss = Math.Sqrt(
            axes.Sum(axis => Math.Pow(axis.AbsoluteQuantizationLossMetres, 2)));
        return new RevisedQ256EndpointReport(
            Endpoint: endpoint,
            NodeUid: Hex(reopenedNode.Uid),
            NeutralEnhFloat64: new NeutralEnhReport(
                neutralEnhFloat64.E,
                neutralEnhFloat64.N,
                neutralEnhFloat64.H),
            Axes: axes,
            HorizontalXzQuantizationLossMetres: horizontalLoss,
            ThreeDimensionalQuantizationLossMetres: threeDimensionalLoss,
            ExactCodeAgreement: axes.All(axis => axis.ExactCodeAgreement));
    }

    private static RevisedQ256AxisReport BuildRevisedQ256AxisReport(
        string axis,
        string neutralSourceExpression,
        double mappedFloat64,
        float float32Input,
        int expectedQ,
        int writtenQ,
        int readbackQ,
        float expectedNative,
        float readbackNative)
    {
        var signedLoss = (double)readbackNative - float32Input;
        return new RevisedQ256AxisReport(
            Axis: axis,
            NeutralSourceExpression: neutralSourceExpression,
            MappedFloat64: mappedFloat64,
            Float32Input: float32Input,
            Float32BitsHex: QuantizerRca.FloatBits(float32Input),
            ExpectedQ: expectedQ,
            WrittenQ: writtenQ,
            ReadbackQ: readbackQ,
            ExpectedNativeAxis: expectedNative,
            ReadbackNativeAxis: readbackNative,
            SignedQuantizationLossMetres: signedLoss,
            AbsoluteQuantizationLossMetres: Math.Abs(signedLoss),
            ExactCodeAgreement:
                expectedQ == writtenQ
                && expectedQ == readbackQ
                && expectedNative.Equals(readbackNative));
    }

    private static List<RevisedCriterionReport> BuildRevisedCriteria(
        NumericalMaxima maxima,
        RevisedQ256Summary q256,
        IReadOnlyCollection<QuantizerRca.SerializerProbe> probes) =>
    [
        RevisedCriterion(
            "float64-to-float32 conversion",
            maxima.DoubleToFloat3dMetres <= NativeConversionThresholdMetres,
            FormatMetres(maxima.DoubleToFloat3dMetres) + " m",
            "<= 0.001 m scene-space 3D"),
        RevisedCriterion(
            "exact generated Q256 integer codes",
            q256.ExactGeneratedAxisAgreementCount == q256.GeneratedAxisCount,
            $"{q256.ExactGeneratedAxisAgreementCount}/{q256.GeneratedAxisCount}",
            "expected_q == written_q == readback_q for every X/Y/Z component"),
        RevisedCriterion(
            "direct Q256 probes",
            probes.Count == 45,
            probes.Count.ToString(CultureInfo.InvariantCulture),
            "45 exact probes: 15 values on each of X, Y and Z"),
        RevisedCriterion(
            "Q256 per-axis strict bound",
            q256.MaximumGeneratedAxisLossMetres < q256.PerAxisStrictUpperBoundMetres
                && q256.MaximumDirectProbeAxisLossMetres < q256.PerAxisStrictUpperBoundMetres,
            FormatMetres(Math.Max(
                q256.MaximumGeneratedAxisLossMetres,
                q256.MaximumDirectProbeAxisLossMetres)) + " m",
            "< 1/256 m"),
        RevisedCriterion(
            "Q256 horizontal X/Z strict bound",
            q256.MaximumGeneratedHorizontalXzLossMetres < q256.HorizontalXzStrictUpperBoundMetres,
            FormatMetres(q256.MaximumGeneratedHorizontalXzLossMetres) + " m",
            "< sqrt(2)/256 m"),
        RevisedCriterion(
            "Q256 3D strict bound",
            q256.MaximumGeneratedThreeDimensionalLossMetres < q256.ThreeDimensionalStrictUpperBoundMetres,
            FormatMetres(q256.MaximumGeneratedThreeDimensionalLossMetres) + " m",
            "< sqrt(3)/256 m"),
        RevisedCriterion(
            "native straight-road geometry",
            maxima.StraightSegmentHausdorffMetres <= StraightRoadThresholdMetres,
            FormatMetres(maxima.StraightSegmentHausdorffMetres) + " m",
            "<= 1.0 m scene-space Hausdorff"),
        RevisedCriterion(
            "native planar radius",
            maxima.NativePlanarRadiusMetres <= NativeRadiusThresholdMetres,
            FormatMetres(maxima.NativePlanarRadiusMetres) + " m",
            "<= 10000 m"),
        RevisedCriterion(
            "candidate adapter arithmetic",
            true,
            "X=E, Y=H, Z=-N applied before float32/Q256",
            "arithmetic only; Map Editor geographic semantics remain pending"),
    ];

    private static RevisedCriterionReport RevisedCriterion(
        string name,
        bool passed,
        string observed,
        string requirement) =>
        new(name, observed, requirement, passed ? "PASS" : "FAIL");

    private static RevisedSemanticEvidence BuildRevisedSemanticEvidence(
        NeutralRoot model,
        string neutralJsonPath,
        bool automaticPassed,
        NumericalMaxima maxima,
        RevisedQ256Summary q256,
        List<QuantizerRca.SerializerProbe> probes,
        List<GeneratedMapReport> maps,
        List<RevisedQ256RoadReport> q256Roads,
        List<FileEvidence> nativeFiles,
        NativeEnvironment environment)
    {
        var qByRoad = q256Roads.ToDictionary(
            road => (road.MapId, road.RoadId),
            road => road);
        var semanticMaps = maps.Select(map => new RevisedSemanticMap(
            Id: map.Id,
            MbdPath: map.MbdPath,
            SectorDirectory: map.SectorDirectory,
            MapFormat: map.MapFormat,
            SectorCountObserved: map.SectorCountObserved,
            RoadCount: map.RoadCount,
            NodeCount: map.NodeCount,
            AllUidsNonZeroAndUnique: map.AllUidsNonZeroAndUnique,
            RoadsUsePrivateTerminalNodes: map.RoadsUsePrivateTerminalNodes,
            Roads: map.Roads.Select(road =>
            {
                var qRoad = qByRoad[(map.Id, road.Id)];
                return new RevisedSemanticRoad(
                    Id: road.Id,
                    SourceFixtureId: road.SourceFixtureId,
                    Scale: road.Scale,
                    Assets: road.Assets,
                    ExpectedNativeDouble: road.ExpectedNativeDouble,
                    Vector3Input: road.Vector3Input,
                    GeneratedReadback: road.GeneratedReadback,
                    LengthReadbackMetres: road.LengthReadbackMetres,
                    OrientationErrorDegrees: road.OrientationErrorDegrees,
                    NativePlanarRadiusMaxMetres: road.NativePlanarRadiusMaxMetres,
                    Errors: road.Errors,
                    Q256: new RevisedSemanticQ256Road(
                        Backward: RevisedSemanticQ256Endpoint.From(qRoad.Backward),
                        Forward: RevisedSemanticQ256Endpoint.From(qRoad.Forward)));
            }).ToList())).ToList();

        return new RevisedSemanticEvidence(
            SchemaVersion: 1,
            CriteriaId: RevisedCriteriaId,
            Poc: model.Poc,
            InputSha256: Sha256(neutralJsonPath),
            GenerationAutomaticValidation: automaticPassed ? "PASS" : "FAIL",
            Environment: environment,
            CandidateMapping: new MappingReport("E", "H", "-N"),
            AxisSemanticStatus: "MAP_EDITOR_GEOGRAPHIC_SEMANTICS_PENDING",
            Maxima: maxima,
            Q256: q256,
            DirectSerializerProbes: probes,
            Maps: semanticMaps,
            NativeFiles: nativeFiles
                .Select(file => new RevisedSemanticFile(file.Path, file.Bytes))
                .ToList());
    }

    private static NativeEnvironment ReadEnvironment(bool requireRevisedRuntime = false)
    {
        var assembly = typeof(Map).Assembly;
        var assemblyVersion = assembly.GetName().Version?.ToString()
            ?? throw new InvalidOperationException("TruckLib assembly version is unavailable.");
        Require(assemblyVersion == "0.5.1.0", $"Unexpected TruckLib assembly version {assemblyVersion}.");
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var runtimeVersion = Environment.Version.ToString();
        if (requireRevisedRuntime)
        {
            ValidateRevisedRuntimeVersion(runtimeVersion);
        }

        return new NativeEnvironment(
            SdkPinned: "10.0.400",
            TargetFramework: "net10.0",
            Framework: RuntimeInformation.FrameworkDescription,
            RuntimeVersion: runtimeVersion,
            Os: RuntimeInformation.OSDescription,
            OsArchitecture: RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            TruckLibPackage: "0.5.1",
            TruckLibAssemblyVersion: assemblyVersion,
            TruckLibInformationalVersion: informationalVersion,
            TruckLibDeclaredMapFormat: ExpectedMapFormat);
    }

    private static void ValidateRevisedRuntimeVersion(string runtimeVersion) =>
        Require(
            runtimeVersion == "10.0.11",
            $"Revised rerun requires Microsoft.NETCore.App runtime 10.0.11; actual {runtimeVersion}.");

    private static List<CriterionReport> BuildAutomaticCriteria(NumericalMaxima maxima) =>
    [
        Criterion(
            "double-to-float conversion",
            maxima.DoubleToFloat3dMetres,
            NativeConversionThresholdMetres),
        Criterion(
            "generated TruckLib readback",
            maxima.GeneratedReadback3dMetres,
            NativeConversionThresholdMetres),
        Criterion(
            "native straight-road geometry",
            maxima.StraightSegmentHausdorffMetres,
            StraightRoadThresholdMetres),
        Criterion(
            "native planar radius",
            maxima.NativePlanarRadiusMetres,
            NativeRadiusThresholdMetres),
    ];

    private static CriterionReport Criterion(string name, double measured, double maximum) =>
        new(name, measured, "<=", maximum, measured <= maximum ? "PASS" : "FAIL");

    private static void AddThresholdFailure(
        List<string> failures,
        string name,
        double measured,
        double maximum)
    {
        if (measured > maximum)
        {
            failures.Add(
                $"Maximum {name} error/value {measured.ToString("R", CultureInfo.InvariantCulture)} m "
                + $"exceeds {maximum.ToString("R", CultureInfo.InvariantCulture)} m.");
        }
    }

    private static void AddStrictBoundFailure(
        List<string> failures,
        string name,
        double measured,
        double strictUpperBound)
    {
        if (measured >= strictUpperBound)
        {
            failures.Add(
                $"Maximum {name} {measured.ToString("R", CultureInfo.InvariantCulture)} m "
                + $"is not below the strict bound {strictUpperBound.ToString("R", CultureInfo.InvariantCulture)} m.");
        }
    }

    private static void AddRoadFailure(
        List<string> failures,
        string mapId,
        string roadId,
        string criterion,
        double measured,
        double maximum)
    {
        if (measured > maximum)
        {
            failures.Add(
                $"Road '{mapId}/{roadId}' {criterion} {measured.ToString("R", CultureInfo.InvariantCulture)} m "
                + $"exceeds {maximum.ToString("R", CultureInfo.InvariantCulture)} m.");
        }
    }

    private static double SegmentHausdorff(
        NativePoint firstStart,
        NativePoint firstEnd,
        NativePoint secondStart,
        NativePoint secondEnd) =>
        new[]
        {
            DistancePointToSegment(firstStart, secondStart, secondEnd),
            DistancePointToSegment(firstEnd, secondStart, secondEnd),
            DistancePointToSegment(secondStart, firstStart, firstEnd),
            DistancePointToSegment(secondEnd, firstStart, firstEnd),
        }.Max();

    private static double DistancePointToSegment(NativePoint point, NativePoint start, NativePoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var dz = end.Z - start.Z;
        var lengthSquared = (dx * dx) + (dy * dy) + (dz * dz);
        if (lengthSquared == 0)
        {
            return Distance3d(point, start);
        }

        var parameter = (((point.X - start.X) * dx)
                         + ((point.Y - start.Y) * dy)
                         + ((point.Z - start.Z) * dz)) / lengthSquared;
        parameter = Math.Clamp(parameter, 0, 1);
        var closest = new NativePoint(
            start.X + (parameter * dx),
            start.Y + (parameter * dy),
            start.Z + (parameter * dz));
        return Distance3d(point, closest);
    }

    private static double OrientationErrorDegrees(
        NativePoint expectedStart,
        NativePoint expectedEnd,
        NativePoint actualStart,
        NativePoint actualEnd)
    {
        var expected = Subtract(expectedEnd, expectedStart);
        var actual = Subtract(actualEnd, actualStart);
        var expectedLength = Magnitude(expected);
        var actualLength = Magnitude(actual);
        Require(expectedLength > 0 && actualLength > 0, "Cannot measure orientation of a zero-length road.");
        var cosine = Dot(expected, actual) / (expectedLength * actualLength);
        return Math.Acos(Math.Clamp(cosine, -1, 1)) * (180.0 / Math.PI);
    }

    private static NativePoint Subtract(NativePoint left, NativePoint right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static double Dot(NativePoint left, NativePoint right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static double Magnitude(NativePoint value) => Math.Sqrt(Dot(value, value));

    private static double Distance3d(NativePoint left, NativePoint right) =>
        Magnitude(Subtract(left, right));

    private static double PlanarRadius(NativePoint point) =>
        Math.Sqrt((point.X * point.X) + (point.Z * point.Z));

    private static double CandidateGridResidual(SegmentReport segment) =>
        Math.Max(
            CandidateGridResidual(segment.Backward),
            CandidateGridResidual(segment.Forward));

    private static double CandidateGridResidual(NativePoint point) =>
        new[]
        {
            AxisGridResidual(point.X),
            AxisGridResidual(point.Y),
            AxisGridResidual(point.Z),
        }.Max();

    private static double AxisGridResidual(double value)
    {
        var nearest = Math.Round(value / ObservedCandidateCoordinateStepMetres)
            * ObservedCandidateCoordinateStepMetres;
        return Math.Abs(value - nearest);
    }

    private static double MaxOrZero(IEnumerable<double> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? 0 : array.Max();
    }

    private static void EnsureFreshOutputRoot(string path)
    {
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new IOException($"Output root must be absent or empty: {path}");
        }

        Directory.CreateDirectory(path);
    }

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Output report has no parent directory: {path}");
        Directory.CreateDirectory(parent);
    }

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine);
    }

    private static List<FileEvidence> InventoryFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => new FileEvidence(
                RelativePath(root, path),
                new FileInfo(path).Length,
                Sha256(path)))
            .ToList();

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static uint ReadMapFormat(string mbdPath)
    {
        using var stream = File.OpenRead(mbdPath);
        using var reader = new BinaryReader(stream);
        return reader.ReadUInt32();
    }

    private static void ExpectThrows<TException>(Action action, string check)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Self-test did not observe expected {check}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string Hex(ulong value) => $"0x{value:X16}";

    private static string FormatMetres(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private sealed record PendingRoad(
        NeutralRoad NeutralRoad,
        NativePoint ExpectedBackward,
        NativePoint ExpectedForward,
        Vector3 VectorBackward,
        Vector3 VectorForward,
        ulong RoadUid,
        ulong BackwardNodeUid,
        ulong ForwardNodeUid,
        QuantizerRca.EncodedPosition? WrittenBackwardQ256,
        QuantizerRca.EncodedPosition? WrittenForwardQ256);

    private sealed record AssignmentResult(double Cost, int[] Assignment);

    private sealed record AdapterValidationReport(
        int SchemaVersion,
        string Poc,
        string GateStatus,
        string AutomaticValidation,
        InputEvidence Input,
        NativeEnvironment Environment,
        CoordinateContractReport CoordinateContract,
        ThresholdReport Thresholds,
        NumericalMaxima Maxima,
        NativePrecisionDiagnosticReport NativePrecisionDiagnostic,
        List<CriterionReport> Criteria,
        List<string> NumericalFailures,
        List<GeneratedMapReport> Maps,
        List<FileEvidence> GeneratedFiles,
        List<string> ManualChecksStillRequired);

    private sealed record InputEvidence(string Path, string Sha256);

    private sealed record NativeEnvironment(
        string SdkPinned,
        string TargetFramework,
        string Framework,
        string RuntimeVersion,
        string Os,
        string OsArchitecture,
        string ProcessArchitecture,
        string TruckLibPackage,
        string TruckLibAssemblyVersion,
        string? TruckLibInformationalVersion,
        uint TruckLibDeclaredMapFormat);

    private sealed record CoordinateContractReport(
        List<string> SourceAxes,
        string Unit,
        MappingReport CandidateMapping,
        string AdapterOperation,
        string SemanticStatus);

    private sealed record MappingReport(string X, string Y, string Z);

    private sealed record ThresholdReport(
        double NativeNumericalConversion3dMetres,
        double StraightRoadHausdorffMetres,
        double NativePlanarRadiusMetres);

    private sealed record NumericalMaxima(
        double DoubleToFloat3dMetres,
        double GeneratedReadback3dMetres,
        double FloatSerialization3dMetres,
        double StraightSegmentHausdorffMetres,
        double NativePlanarRadiusMetres);

    private sealed record NativePrecisionDiagnosticReport(
        double CandidateCoordinateStepMetres,
        double MaxGeneratedReadbackAxisResidualFromCandidateGridMetres,
        string Interpretation,
        string ThresholdCompatibility);

    private sealed record CriterionReport(
        string Name,
        double MeasuredMetres,
        string Operator,
        double LimitMetres,
        string Status);

    private sealed record GeneratedMapReport(
        string Id,
        string MbdPath,
        string SectorDirectory,
        uint MapFormat,
        string EditorMapUid,
        int SectorCountObserved,
        int RoadCount,
        int NodeCount,
        bool AllUidsNonZeroAndUnique,
        bool RoadsUsePrivateTerminalNodes,
        List<GeneratedRoadReport> Roads);

    private sealed record GeneratedRoadReport(
        string Id,
        string SourceFixtureId,
        double Scale,
        string Uid,
        RoadAssetReport Assets,
        SegmentReport ExpectedNativeDouble,
        SegmentReport Vector3Input,
        SegmentReport GeneratedReadback,
        NodeReport BackwardNode,
        NodeReport ForwardNode,
        double LengthReadbackMetres,
        double OrientationErrorDegrees,
        double NativePlanarRadiusMaxMetres,
        GeneratedErrorReport Errors);

    private sealed record RoadAssetReport(
        string Type,
        string Look,
        string Variant,
        string LeftEdge,
        string RightEdge);

    private readonly record struct NativePoint(double X, double Y, double Z)
    {
        public static NativePoint FromVector3(Vector3 value) => new(value.X, value.Y, value.Z);
    }

    private sealed record SegmentReport(NativePoint Backward, NativePoint Forward);

    private sealed record NodeReport(
        string Uid,
        NativePoint Position,
        QuaternionReport Rotation,
        string? BackwardItemUid,
        string? ForwardItemUid)
    {
        public static NodeReport FromNode(INode node) => new(
            Hex(node.Uid),
            NativePoint.FromVector3(node.Position),
            new QuaternionReport(node.Rotation.X, node.Rotation.Y, node.Rotation.Z, node.Rotation.W),
            node.BackwardItem is null ? null : Hex(node.BackwardItem.Uid),
            node.ForwardItem is null ? null : Hex(node.ForwardItem.Uid));
    }

    private sealed record QuaternionReport(float X, float Y, float Z, float W);

    private sealed record GeneratedErrorReport(
        double DoubleToFloatBackward3dMetres,
        double DoubleToFloatForward3dMetres,
        double DoubleToFloatMax3dMetres,
        double GeneratedReadbackBackward3dMetres,
        double GeneratedReadbackForward3dMetres,
        double GeneratedReadbackMax3dMetres,
        double FloatSerializationBackward3dMetres,
        double FloatSerializationForward3dMetres,
        double FloatSerializationMax3dMetres,
        double StraightSegmentHausdorffMetres);

    private sealed record FileEvidence(string Path, long Bytes, string Sha256);

    private sealed record RevisedAdapterValidationReport(
        int SchemaVersion,
        string CriteriaId,
        string Poc,
        string HistoricalV1Status,
        string GenerationId,
        string OutputRoot,
        string GenerationAutomaticValidation,
        string RerunState,
        InputEvidence Input,
        NativeEnvironment Environment,
        CoordinateContractReport CoordinateContract,
        RevisedThresholdReport Thresholds,
        NumericalMaxima Maxima,
        RevisedQ256Summary Q256,
        List<RevisedCriterionReport> Criteria,
        List<string> Failures,
        List<QuantizerRca.SerializerProbe> DirectSerializerProbes,
        List<RevisedQ256RoadReport> Q256Roads,
        List<GeneratedMapReport> Maps,
        List<FileEvidence> NativeFiles,
        SemanticEvidenceReference SemanticEvidence,
        List<string> ManualChecksStillRequired);

    private sealed record RevisedThresholdReport(
        double Float64ToFloat32ThreeDimensionalMetres,
        bool ExactQ256IntegerCodeAgreementRequired,
        double StraightRoadHausdorffMetres,
        double NativePlanarRadiusMetres);

    private sealed record RevisedCriterionReport(
        string Name,
        string Observed,
        string Requirement,
        string Status);

    private sealed record RevisedQ256Summary(
        string Rule,
        string Readback,
        double GridStepMetres,
        double PerAxisStrictUpperBoundMetres,
        double HorizontalXzStrictUpperBoundMetres,
        double ThreeDimensionalStrictUpperBoundMetres,
        int GeneratedNodeCount,
        int GeneratedAxisCount,
        int ExactGeneratedAxisAgreementCount,
        int DirectProbeCount,
        List<string> DirectProbeAxes,
        double MaximumGeneratedAxisLossMetres,
        double MaximumGeneratedHorizontalXzLossMetres,
        double MaximumGeneratedThreeDimensionalLossMetres,
        double MaximumDirectProbeAxisLossMetres);

    private sealed record RevisedQ256RoadReport(
        string MapId,
        string RoadId,
        string SourceFixtureId,
        double Scale,
        RevisedQ256EndpointReport Backward,
        RevisedQ256EndpointReport Forward);

    private sealed record RevisedQ256EndpointReport(
        string Endpoint,
        string NodeUid,
        NeutralEnhReport NeutralEnhFloat64,
        List<RevisedQ256AxisReport> Axes,
        double HorizontalXzQuantizationLossMetres,
        double ThreeDimensionalQuantizationLossMetres,
        bool ExactCodeAgreement);

    private sealed record NeutralEnhReport(double E, double N, double H);

    private sealed record RevisedQ256AxisReport(
        string Axis,
        string NeutralSourceExpression,
        double MappedFloat64,
        float Float32Input,
        string Float32BitsHex,
        int ExpectedQ,
        int WrittenQ,
        int ReadbackQ,
        float ExpectedNativeAxis,
        float ReadbackNativeAxis,
        double SignedQuantizationLossMetres,
        double AbsoluteQuantizationLossMetres,
        bool ExactCodeAgreement);

    private sealed record SemanticEvidenceReference(
        string Path,
        string Sha256,
        List<string> Excludes);

    private sealed record RevisedSemanticEvidence(
        int SchemaVersion,
        string CriteriaId,
        string Poc,
        string InputSha256,
        string GenerationAutomaticValidation,
        NativeEnvironment Environment,
        MappingReport CandidateMapping,
        string AxisSemanticStatus,
        NumericalMaxima Maxima,
        RevisedQ256Summary Q256,
        List<QuantizerRca.SerializerProbe> DirectSerializerProbes,
        List<RevisedSemanticMap> Maps,
        List<RevisedSemanticFile> NativeFiles);

    private sealed record RevisedSemanticMap(
        string Id,
        string MbdPath,
        string SectorDirectory,
        uint MapFormat,
        int SectorCountObserved,
        int RoadCount,
        int NodeCount,
        bool AllUidsNonZeroAndUnique,
        bool RoadsUsePrivateTerminalNodes,
        List<RevisedSemanticRoad> Roads);

    private sealed record RevisedSemanticRoad(
        string Id,
        string SourceFixtureId,
        double Scale,
        RoadAssetReport Assets,
        SegmentReport ExpectedNativeDouble,
        SegmentReport Vector3Input,
        SegmentReport GeneratedReadback,
        double LengthReadbackMetres,
        double OrientationErrorDegrees,
        double NativePlanarRadiusMaxMetres,
        GeneratedErrorReport Errors,
        RevisedSemanticQ256Road Q256);

    private sealed record RevisedSemanticQ256Road(
        RevisedSemanticQ256Endpoint Backward,
        RevisedSemanticQ256Endpoint Forward);

    private sealed record RevisedSemanticQ256Endpoint(
        string Endpoint,
        NeutralEnhReport NeutralEnhFloat64,
        List<RevisedQ256AxisReport> Axes,
        double HorizontalXzQuantizationLossMetres,
        double ThreeDimensionalQuantizationLossMetres,
        bool ExactCodeAgreement)
    {
        public static RevisedSemanticQ256Endpoint From(RevisedQ256EndpointReport value) => new(
            value.Endpoint,
            value.NeutralEnhFloat64,
            value.Axes,
            value.HorizontalXzQuantizationLossMetres,
            value.ThreeDimensionalQuantizationLossMetres,
            value.ExactCodeAgreement);
    }

    private sealed record RevisedSemanticFile(string Path, long Bytes);

    private sealed record EditorValidationReport(
        int SchemaVersion,
        string Poc,
        string GateStatus,
        string NumericPostEditorValidation,
        string ImportantLimitation,
        InputEvidence Input,
        NativeEnvironment Environment,
        string SavedMapRoot,
        string MatchingMethod,
        ThresholdReport Thresholds,
        double MaxPostEditorReadback3dMetres,
        double MaxStraightSegmentHausdorffMetres,
        double MaxNativePlanarRadiusMetres,
        List<string> Failures,
        List<EditorMapReport> Maps,
        List<FileEvidence> ReadFiles);

    private sealed record EditorMapReport(
        string Id,
        string MbdPath,
        uint MapFormat,
        string EditorMapUid,
        int SectorCountObserved,
        int RoadCount,
        int NodeCount,
        List<EditorRoadReport> Roads);

    private sealed record EditorRoadReport(
        string Id,
        string SourceFixtureId,
        string Uid,
        string MatchedBy,
        SegmentReport ExpectedNativeDouble,
        SegmentReport EditorReadback,
        NodeReport BackwardNode,
        NodeReport ForwardNode,
        bool DirectionPreserved,
        double OrientationErrorDegrees,
        double Backward3dErrorMetres,
        double Forward3dErrorMetres,
        double Max3dErrorMetres,
        double StraightSegmentHausdorffMetres,
        double NativePlanarRadiusMaxMetres);
}
