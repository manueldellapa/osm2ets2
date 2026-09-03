using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using TruckLib.ScsMap;

namespace Poc002.Adapter;

internal static class QuantizerRca
{
    internal const float FixedPointFactor = 256f;
    internal const double GridStepMetres = 1.0 / 256.0;
    private const ulong ProbeUid = 0x0102030405060708UL;
    private const string TruckLibCommit = "bd745344fc52d3b2d70ce9ac7c88d61b99934805";
    private const string ExpectedExistingGateStatus = "FAIL";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static int Run(string existingAdapterReportArgument, string outputReportArgument)
    {
        var existingAdapterReport = Path.GetFullPath(existingAdapterReportArgument);
        var outputReport = Path.GetFullPath(outputReportArgument);
        Require(File.Exists(existingAdapterReport),
            $"Existing adapter report does not exist: {existingAdapterReport}");

        var probes = ValidateSerializerProbes();
        var existingFixtureEvidence = ValidateExistingFixtures(existingAdapterReport);
        var ruleMatches = ClassifyRule(probes);
        Require(ruleMatches.TruncationTowardZeroMatches == probes.Count,
            "Direct serializer probes did not all match truncation toward zero.");
        Require(ruleMatches.FloorMatches < probes.Count,
            "Direct serializer probes did not distinguish truncation from floor.");
        Require(ruleMatches.RoundToNearestEvenMatches < probes.Count,
            "Direct serializer probes did not distinguish truncation from rounding to nearest.");

        var assembly = typeof(Node).Assembly;
        var assemblyVersion = assembly.GetName().Version?.ToString()
            ?? throw new InvalidOperationException("TruckLib assembly version is unavailable.");
        Require(assemblyVersion == "0.5.1.0",
            $"Unexpected TruckLib assembly version {assemblyVersion}.");

        var truncationAxisBound = GridStepMetres;
        var truncationHorizontalBound = Math.Sqrt(2.0) * GridStepMetres;
        var truncationThreeDimensionalBound = Math.Sqrt(3.0) * GridStepMetres;
        var nearestAxisBound = GridStepMetres / 2.0;
        var nearestThreeDimensionalBound = Math.Sqrt(3.0) * nearestAxisBound;
        Require(existingFixtureEvidence.AllYComponentsZero,
            "Existing PoC fixture comparison is not confined to the X/Z plane as expected.");
        Require(
            existingFixtureEvidence.MaximumFloatToReadback3dMetres < truncationHorizontalBound,
            "Existing native-only error is inconsistent with the Q256 truncation X/Z bound.");
        Require(
            existingFixtureEvidence.MaximumFloatToReadback3dMetres > nearestThreeDimensionalBound,
            "Existing native-only error did not discriminate truncation from hypothetical nearest.");
        var report = new QuantizerRcaReport(
            SchemaVersion: 1,
            Poc: "PoC-002 — Native Q256 root-cause analysis",
            StatusUnderFrozenCriteria: ExpectedExistingGateStatus,
            Scope:
                "Diagnostic only: direct TruckLib Node serialization plus read-only analysis of the existing PoC-002 adapter report; no map regeneration and no Map Editor run.",
            Environment: new EnvironmentReport(
                Runtime: RuntimeInformation.FrameworkDescription,
                RuntimeVersion: Environment.Version.ToString(),
                Os: RuntimeInformation.OSDescription,
                ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
                TruckLibPackage: "0.5.1",
                TruckLibAssemblyVersion: assemblyVersion,
                TruckLibInformationalVersion: assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion),
            UpstreamImplementation: new UpstreamImplementationReport(
                RepositoryCommit: TruckLibCommit,
                PositionInputType: "System.Numerics.Vector3 / System.Single per axis",
                FixedPointFactorExpression: "private const float fixedPointFactor = 256f",
                SerializeExpression: "(int)(Position.<axis> * fixedPointFactor)",
                DeserializeExpression: "ReadInt32() / fixedPointFactor",
                EncodedType: "signed Int32, little-endian via BinaryWriter.Write(Int32)",
                SourceUrl:
                    $"https://github.com/sk-zk/TruckLib/blob/{TruckLibCommit}/TruckLib/ScsMap/Node.cs#L353-L397"),
            ConfirmedRule: new QuantizerRuleReport(
                InputDomain: "finite float32 scene coordinates within the validated native radius",
                EncodedInteger: "trunc_toward_zero(float32_scene_axis * 256f)",
                DecodedMetres: "encoded_integer / 256f",
                GridStepMetres: GridStepMetres,
                AxesIndependent: true,
                BoundarySemantics:
                    "Positive half-open cells truncate down; negative half-open cells truncate up toward zero; (-1/256, +1/256) maps to zero."),
            RuleClassification: ruleMatches,
            TheoreticalBounds: new BoundsReport(
                Scope: "fixed-point quantization error measured from the float32 serializer input",
                TruncationPerAxisStrictUpperBoundMetres: truncationAxisBound,
                TruncationHorizontalXzStrictUpperBoundMetres: truncationHorizontalBound,
                TruncationThreeDimensionalStrictUpperBoundMetres: truncationThreeDimensionalBound,
                NearestPerAxisUpperBoundMetres: nearestAxisBound,
                NearestHorizontalXzUpperBoundMetres: Math.Sqrt(2.0) * nearestAxisBound,
                NearestThreeDimensionalUpperBoundMetres: nearestThreeDimensionalBound),
            ExistingFixtureEvidence: existingFixtureEvidence,
            CurrentCriterionAssessment: new CurrentCriterionAssessmentReport(
                FrozenNativeCriterionMetres: 0.001,
                UniversallyAchievableForArbitraryQ256NodeCoordinates: false,
                Reason:
                    "Q256 truncation has a per-axis error supremum of 1/256 m; even hypothetical nearest has a per-axis bound of 1/512 m, both greater than 0.001 m.",
                ObservedNativeOnlyErrorWithinTruncationXzBound: true,
                ObservedNativeOnlyErrorExceedsHypotheticalNearest3dBound: true,
                StatusUnderFrozenCriterion: ExpectedExistingGateStatus),
            DirectSerializerProbeCount: probes.Count,
            DirectSerializerProbes: probes);

        var parent = Path.GetDirectoryName(outputReport)
            ?? throw new InvalidOperationException($"Output report has no parent: {outputReport}");
        Directory.CreateDirectory(parent);
        File.WriteAllText(outputReport, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);

        Console.WriteLine("Q256_RCA_PASSED");
        Console.WriteLine($"Rule: {report.ConfirmedRule.EncodedInteger}");
        Console.WriteLine($"Direct Node probes: {probes.Count.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine(
            $"Existing fixture axes checked: {existingFixtureEvidence.AxisComponentCount.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine(
            $"Existing maximum overall error remains: {existingFixtureEvidence.MaximumDoubleToReadback3dMetres.ToString("R", CultureInfo.InvariantCulture)} m");
        Console.WriteLine($"PoC status under frozen criteria: {ExpectedExistingGateStatus}");
        Console.WriteLine($"Report: {outputReport}");
        return 0;
    }

    internal static List<SerializerProbe> ValidateSerializerProbes()
    {
        var scalarCases = new List<ScalarProbeCase>
        {
            new("negative-zero-neighbour", MathF.BitDecrement(0f), 0),
            new("zero", 0f, 0),
            new("positive-zero-neighbour", MathF.BitIncrement(0f), 0),
            new("positive-0.001", 0.001f, 0),
            new("negative-0.001", -0.001f, 0),
            new("positive-0.01", 0.01f, 2),
            new("negative-0.01", -0.01f, -2),
            new("positive-0.1", 0.1f, 25),
            new("negative-0.1", -0.1f, -25),
            new("positive-boundary-below", MathF.BitDecrement(1f), 255),
            new("positive-boundary", 1f, 256),
            new("positive-boundary-above", MathF.BitIncrement(1f), 256),
            new("negative-boundary-below", MathF.BitDecrement(-1f), -256),
            new("negative-boundary", -1f, -256),
            new("negative-boundary-above", MathF.BitIncrement(-1f), -255),
        };
        var probes = new List<SerializerProbe>();
        foreach (var scalarCase in scalarCases)
        {
            foreach (var axis in new[] { "X", "Y", "Z" })
            {
                probes.Add(Probe(axis, scalarCase));
            }
        }

        return probes;
    }

    internal static int ExpectedCode(float value) => checked((int)(value * FixedPointFactor));

    internal static EncodedPosition ExpectedPosition(Vector3 position) => new(
        ExpectedCode(position.X),
        ExpectedCode(position.Y),
        ExpectedCode(position.Z));

    internal static EncodedPosition SerializePositionCodes(Node node)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            node.Serialize(writer);
            writer.Flush();
        }

        var bytes = stream.ToArray();
        Require(bytes.Length >= 20, "Serialized Node is too short to contain UID and three positions.");
        return new EncodedPosition(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16, 4)));
    }

    internal static EncodedPosition ReconstructReadbackCodes(Vector3 position)
    {
        var scaledX = position.X * FixedPointFactor;
        var scaledY = position.Y * FixedPointFactor;
        var scaledZ = position.Z * FixedPointFactor;
        Require(scaledX == MathF.Truncate(scaledX), "Readback X is not exactly on the Q256 grid.");
        Require(scaledY == MathF.Truncate(scaledY), "Readback Y is not exactly on the Q256 grid.");
        Require(scaledZ == MathF.Truncate(scaledZ), "Readback Z is not exactly on the Q256 grid.");
        return new EncodedPosition(
            checked((int)scaledX),
            checked((int)scaledY),
            checked((int)scaledZ));
    }

    internal static Vector3 Decode(EncodedPosition encoded) => new(
        encoded.X / FixedPointFactor,
        encoded.Y / FixedPointFactor,
        encoded.Z / FixedPointFactor);

    private static SerializerProbe Probe(string axis, ScalarProbeCase scalarCase)
    {
        var position = axis switch
        {
            "X" => new Vector3(scalarCase.Input, 0f, 0f),
            "Y" => new Vector3(0f, scalarCase.Input, 0f),
            "Z" => new Vector3(0f, 0f, scalarCase.Input),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unknown axis."),
        };
        var node = new Node { Uid = ProbeUid, Position = position };

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            node.Serialize(writer);
            writer.Flush();
        }

        var bytes = stream.ToArray();
        var actual = SerializePositionCodes(node);
        var expected = axis switch
        {
            "X" => new EncodedPosition(scalarCase.ExpectedCode, 0, 0),
            "Y" => new EncodedPosition(0, scalarCase.ExpectedCode, 0),
            "Z" => new EncodedPosition(0, 0, scalarCase.ExpectedCode),
            _ => throw new UnreachableException(),
        };
        Require(actual == expected,
            $"{scalarCase.Id}/{axis}: encoded {actual}, expected {expected}.");
        Require(scalarCase.ExpectedCode == ExpectedCode(scalarCase.Input),
            $"{scalarCase.Id}/{axis}: frozen expected code does not match the DT-07 formula.");

        stream.Position = 0;
        var decodedNode = new Node();
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            decodedNode.Deserialize(reader);
        }
        var expectedDecoded = new Vector3(
            expected.X / FixedPointFactor,
            expected.Y / FixedPointFactor,
            expected.Z / FixedPointFactor);
        Require(decodedNode.Uid == ProbeUid, $"{scalarCase.Id}/{axis}: UID did not round-trip.");
        Require(decodedNode.Position == expectedDecoded,
            $"{scalarCase.Id}/{axis}: decoded {decodedNode.Position}, expected {expectedDecoded}.");

        var inputAxis = AxisValue(position, axis);
        var decodedAxis = AxisValue(decodedNode.Position, axis);
        var scaledInput = inputAxis * FixedPointFactor;
        return new SerializerProbe(
            Case: scalarCase.Id,
            Axis: axis,
            InputFloat: inputAxis,
            InputFloatBitsHex: FloatBits(inputAxis),
            ScaledFloat: scaledInput,
            ScaledFloatBitsHex: FloatBits(scaledInput),
            ExpectedEncodedInteger: scalarCase.ExpectedCode,
            Encoded: actual,
            PositionBytesLittleEndianHex: Convert.ToHexString(bytes.AsSpan(8, 12)).ToLowerInvariant(),
            Decoded: new DecodedPosition(
                decodedNode.Position.X,
                decodedNode.Position.Y,
                decodedNode.Position.Z),
            SignedAxisErrorMetres: decodedAxis - inputAxis,
            AbsoluteAxisErrorMetres: Math.Abs((double)decodedAxis - inputAxis));
    }

    private static ExistingFixtureReport ValidateExistingFixtures(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var gateStatus = root.GetProperty("gateStatus").GetString();
        Require(gateStatus == ExpectedExistingGateStatus,
            $"Existing adapter report gate is '{gateStatus}', expected '{ExpectedExistingGateStatus}'.");

        var axes = new[] { "x", "y", "z" };
        var endpoints = new[] { "backward", "forward" };
        var roadCount = 0;
        var axisComponentCount = 0;
        var maxGridResidual = 0.0;
        var maxRuleResidual = 0.0;
        var allYComponentsZero = true;
        foreach (var map in root.GetProperty("maps").EnumerateArray())
        {
            foreach (var road in map.GetProperty("roads").EnumerateArray())
            {
                roadCount++;
                foreach (var endpoint in endpoints)
                {
                    foreach (var axis in axes)
                    {
                        axisComponentCount++;
                        var vectorInput = road
                            .GetProperty("vector3Input")
                            .GetProperty(endpoint)
                            .GetProperty(axis)
                            .GetDouble();
                        var readback = road
                            .GetProperty("generatedReadback")
                            .GetProperty(endpoint)
                            .GetProperty(axis)
                            .GetDouble();
                        if (axis == "y")
                        {
                            allYComponentsZero &= vectorInput == 0.0 && readback == 0.0;
                        }
                        var scaledReadback = readback * FixedPointFactor;
                        var nearestInteger = Math.Round(scaledReadback);
                        maxGridResidual = Math.Max(
                            maxGridResidual,
                            Math.Abs(scaledReadback - nearestInteger) / FixedPointFactor);
                        var expectedCode = (int)((float)vectorInput * FixedPointFactor);
                        var expectedReadback = expectedCode / FixedPointFactor;
                        maxRuleResidual = Math.Max(
                            maxRuleResidual,
                            Math.Abs(readback - expectedReadback));
                    }
                }
            }
        }

        Require(roadCount > 0, "Existing adapter report contains no roads.");
        Require(maxGridResidual == 0.0,
            $"Existing fixture readback has non-Q256 residual {maxGridResidual:R} m.");
        Require(maxRuleResidual == 0.0,
            $"Existing fixture readback does not exactly match the confirmed Q256 rule; residual {maxRuleResidual:R} m.");

        var maxima = root.GetProperty("maxima");
        return new ExistingFixtureReport(
            SourceReportFileName: Path.GetFileName(path),
            SourceReportSha256: Sha256(path),
            ExistingGateStatus: gateStatus!,
            RoadCount: roadCount,
            EndpointCount: roadCount * 2,
            AxisComponentCount: axisComponentCount,
            AllReadbackComponentsOnQ256Grid: true,
            MaximumGridResidualMetres: maxGridResidual,
            AllReadbackComponentsMatchConfirmedRule: true,
            MaximumRuleResidualMetres: maxRuleResidual,
            AllYComponentsZero: allYComponentsZero,
            MaximumDoubleToFloat3dMetres: maxima.GetProperty("doubleToFloat3dMetres").GetDouble(),
            MaximumFloatToReadback3dMetres: maxima.GetProperty("floatSerialization3dMetres").GetDouble(),
            MaximumDoubleToReadback3dMetres: maxima.GetProperty("generatedReadback3dMetres").GetDouble());
    }

    private static RuleClassificationReport ClassifyRule(IReadOnlyCollection<SerializerProbe> probes)
    {
        var truncationMatches = 0;
        var floorMatches = 0;
        var nearestMatches = 0;
        foreach (var probe in probes)
        {
            var encoded = probe.Axis switch
            {
                "X" => probe.Encoded.X,
                "Y" => probe.Encoded.Y,
                "Z" => probe.Encoded.Z,
                _ => throw new UnreachableException(),
            };
            truncationMatches += encoded == (int)probe.ScaledFloat ? 1 : 0;
            floorMatches += encoded == (int)MathF.Floor(probe.ScaledFloat) ? 1 : 0;
            nearestMatches += encoded == (int)MathF.Round(
                probe.ScaledFloat,
                MidpointRounding.ToEven) ? 1 : 0;
        }

        return new RuleClassificationReport(
            ProbeCount: probes.Count,
            TruncationTowardZeroMatches: truncationMatches,
            FloorMatches: floorMatches,
            RoundToNearestEvenMatches: nearestMatches,
            ConfirmedRule: "truncation toward zero after float32 multiplication by 256f");
    }

    private static float AxisValue(Vector3 value, string axis) => axis switch
    {
        "X" => value.X,
        "Y" => value.Y,
        "Z" => value.Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unknown axis."),
    };

    internal static string FloatBits(float value) =>
        $"0x{BitConverter.SingleToUInt32Bits(value):X8}";

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record ScalarProbeCase(string Id, float Input, int ExpectedCode);

    internal sealed record SerializerProbe(
        string Case,
        string Axis,
        float InputFloat,
        string InputFloatBitsHex,
        float ScaledFloat,
        string ScaledFloatBitsHex,
        int ExpectedEncodedInteger,
        EncodedPosition Encoded,
        string PositionBytesLittleEndianHex,
        DecodedPosition Decoded,
        float SignedAxisErrorMetres,
        double AbsoluteAxisErrorMetres);

    internal sealed record EncodedPosition(int X, int Y, int Z);

    internal sealed record DecodedPosition(float X, float Y, float Z);

    private sealed record QuantizerRcaReport(
        int SchemaVersion,
        string Poc,
        string StatusUnderFrozenCriteria,
        string Scope,
        EnvironmentReport Environment,
        UpstreamImplementationReport UpstreamImplementation,
        QuantizerRuleReport ConfirmedRule,
        RuleClassificationReport RuleClassification,
        BoundsReport TheoreticalBounds,
        ExistingFixtureReport ExistingFixtureEvidence,
        CurrentCriterionAssessmentReport CurrentCriterionAssessment,
        int DirectSerializerProbeCount,
        List<SerializerProbe> DirectSerializerProbes);

    private sealed record EnvironmentReport(
        string Runtime,
        string RuntimeVersion,
        string Os,
        string ProcessArchitecture,
        string TruckLibPackage,
        string TruckLibAssemblyVersion,
        string? TruckLibInformationalVersion);

    private sealed record UpstreamImplementationReport(
        string RepositoryCommit,
        string PositionInputType,
        string FixedPointFactorExpression,
        string SerializeExpression,
        string DeserializeExpression,
        string EncodedType,
        string SourceUrl);

    private sealed record QuantizerRuleReport(
        string InputDomain,
        string EncodedInteger,
        string DecodedMetres,
        double GridStepMetres,
        bool AxesIndependent,
        string BoundarySemantics);

    private sealed record RuleClassificationReport(
        int ProbeCount,
        int TruncationTowardZeroMatches,
        int FloorMatches,
        int RoundToNearestEvenMatches,
        string ConfirmedRule);

    private sealed record BoundsReport(
        string Scope,
        double TruncationPerAxisStrictUpperBoundMetres,
        double TruncationHorizontalXzStrictUpperBoundMetres,
        double TruncationThreeDimensionalStrictUpperBoundMetres,
        double NearestPerAxisUpperBoundMetres,
        double NearestHorizontalXzUpperBoundMetres,
        double NearestThreeDimensionalUpperBoundMetres);

    private sealed record ExistingFixtureReport(
        string SourceReportFileName,
        string SourceReportSha256,
        string ExistingGateStatus,
        int RoadCount,
        int EndpointCount,
        int AxisComponentCount,
        bool AllReadbackComponentsOnQ256Grid,
        double MaximumGridResidualMetres,
        bool AllReadbackComponentsMatchConfirmedRule,
        double MaximumRuleResidualMetres,
        bool AllYComponentsZero,
        double MaximumDoubleToFloat3dMetres,
        double MaximumFloatToReadback3dMetres,
        double MaximumDoubleToReadback3dMetres);

    private sealed record CurrentCriterionAssessmentReport(
        double FrozenNativeCriterionMetres,
        bool UniversallyAchievableForArbitraryQ256NodeCoordinates,
        string Reason,
        bool ObservedNativeOnlyErrorWithinTruncationXzBound,
        bool ObservedNativeOnlyErrorExceedsHypotheticalNearest3dBound,
        string StatusUnderFrozenCriterion);
}
