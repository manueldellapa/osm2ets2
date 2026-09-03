using System.Text.Json.Serialization;

namespace Poc002.Adapter;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class NeutralRoot
{
    public required int SchemaVersion { get; set; }

    public required string Poc { get; set; }

    public required CoordinateSystemContract CoordinateSystem { get; set; }

    public required List<NeutralMap> Maps { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class CoordinateSystemContract
{
    public List<string>? SourceAxes { get; set; }

    public List<string>? Axes { get; set; }

    public required string Unit { get; set; }

    public CandidateMapping? CandidateMapping { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class CandidateMapping
{
    public required string X { get; set; }

    public required string Y { get; set; }

    public required string Z { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class NeutralMap
{
    public required string Id { get; set; }

    public required List<NeutralRoad> Roads { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class NeutralRoad
{
    public required string Id { get; set; }

    public required string SourceFixtureId { get; set; }

    public required double Scale { get; set; }

    public required NeutralPoint Backward { get; set; }

    public required NeutralPoint Forward { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class NeutralPoint
{
    public required double E { get; set; }

    public required double N { get; set; }

    public required double H { get; set; }
}
