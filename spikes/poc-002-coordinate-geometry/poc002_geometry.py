"""Narrow coordinate/geometry experiment for PoC-002.

This module deliberately contains no OSM parsing, HTTP access, CLI framework,
TruckLib concepts, or production intermediate representation.
"""

from __future__ import annotations

import hashlib
import json
import math
import os
import platform
import sys
import sysconfig
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Mapping, Sequence

# Defense in depth: set the PROJ environment switch before importing pyproj,
# then also disable its process-global network API below.
os.environ["PROJ_NETWORK"] = "OFF"

import pyproj
import shapely
from pyproj import CRS, Geod, Transformer, network
from shapely.geometry import LineString, Point, box

network.set_network_enabled(False)

EXPECTED_PYTHON = "3.14.7"
EXPECTED_PYPROJ = "3.7.2"
EXPECTED_SHAPELY = "2.1.2"

ROUND_TRIP_LIMIT_M = 0.001
DISCRETIZATION_LIMIT_M = 0.01
AREA_LIMIT_M2 = 25_000_000.0
DIAGONAL_LIMIT_M = 10_000.0
NATIVE_RADIUS_LIMIT_M = 10_000.0

WGS84_A_M = 6_378_137.0
WGS84_INVERSE_FLATTENING = 298.257223563


@dataclass(frozen=True)
class Origin:
    lon_deg: float
    lat_deg: float

    def as_json(self) -> dict[str, float]:
        return {"lonDeg": self.lon_deg, "latDeg": self.lat_deg}


@dataclass(frozen=True)
class ProjectedPoint:
    e: float
    n: float


def _require_finite(value: float, name: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise TypeError(f"{name} must be a real number")
    result = float(value)
    if not math.isfinite(result):
        raise ValueError(f"{name} must be finite")
    return result


def validate_lon_lat(lon_deg: float, lat_deg: float) -> tuple[float, float]:
    """Validate the experiment's explicit longitude, latitude convention."""

    lon = _require_finite(lon_deg, "longitude")
    lat = _require_finite(lat_deg, "latitude")
    if not -180.0 <= lon <= 180.0:
        raise ValueError("longitude must be in [-180, 180]")
    if not -80.0 <= lat <= 80.0:
        raise ValueError("latitude must be in the PoC domain [-80, 80]")
    return lon, lat


def validate_bbox(bbox: Mapping[str, float]) -> dict[str, float]:
    west = _require_finite(bbox["west"], "bbox west")
    south = _require_finite(bbox["south"], "bbox south")
    east = _require_finite(bbox["east"], "bbox east")
    north = _require_finite(bbox["north"], "bbox north")
    validate_lon_lat(west, south)
    validate_lon_lat(east, north)
    if west >= east:
        raise ValueError("bbox must not cross the antimeridian and west must be < east")
    if south >= north:
        raise ValueError("bbox south must be < north")
    return {"west": west, "south": south, "east": east, "north": north}


def origin_from_bbox(bbox: Mapping[str, float]) -> Origin:
    checked = validate_bbox(bbox)
    return Origin(
        lon_deg=(checked["west"] + checked["east"]) / 2.0,
        lat_deg=(checked["south"] + checked["north"]) / 2.0,
    )


def origin_from_candidate_extent(
    geometries: Sequence[Mapping[str, Any]],
) -> Origin:
    """Select the origin from every candidate before later mapping exclusions."""

    coordinates: list[tuple[float, float]] = []
    for geometry in geometries:
        for point in geometry["points"]:
            coordinates.append(validate_lon_lat(point["lon_deg"], point["lat_deg"]))
    if not coordinates:
        raise ValueError("candidate geometry extent cannot be empty")
    lons, lats = zip(*coordinates, strict=True)
    if max(lons) - min(lons) >= 180.0:
        raise ValueError("candidate extent may cross the antimeridian")
    return Origin(
        lon_deg=(min(lons) + max(lons)) / 2.0,
        lat_deg=(min(lats) + max(lats)) / 2.0,
    )


class LocalAeqd:
    """Explicit WGS84 ellipsoidal AEQD with zero false origin."""

    def __init__(self, origin: Origin):
        validate_lon_lat(origin.lon_deg, origin.lat_deg)
        if network.is_network_enabled():
            raise RuntimeError("PROJ network must be disabled")
        self.origin = origin
        self.input_definition = (
            "+proj=aeqd "
            f"+lat_0={origin.lat_deg:.15g} "
            f"+lon_0={origin.lon_deg:.15g} "
            "+x_0=0 +y_0=0 +ellps=WGS84 +units=m +type=crs"
        )
        self.source_crs = CRS.from_epsg(4326)
        self.target_crs = CRS.from_proj4(self.input_definition)
        self.forward_transformer = Transformer.from_crs(
            self.source_crs,
            self.target_crs,
            always_xy=True,
        )
        self.inverse_transformer = Transformer.from_crs(
            self.target_crs,
            self.source_crs,
            always_xy=True,
        )

    def project(self, lon_deg: float, lat_deg: float) -> ProjectedPoint:
        lon, lat = validate_lon_lat(lon_deg, lat_deg)
        e, n = self.forward_transformer.transform(lon, lat, errcheck=True)
        return ProjectedPoint(
            _require_finite(e, "projected easting"),
            _require_finite(n, "projected northing"),
        )

    def inverse(self, e: float, n: float) -> tuple[float, float]:
        checked_e = _require_finite(e, "projected easting")
        checked_n = _require_finite(n, "projected northing")
        lon, lat = self.inverse_transformer.transform(
            checked_e,
            checked_n,
            errcheck=True,
        )
        return validate_lon_lat(lon, lat)

    def metadata(self) -> dict[str, Any]:
        return {
            "origin": self.origin.as_json(),
            "sourceCoordinateOrder": ["longitude", "latitude"],
            "alwaysXy": True,
            "numericType": "IEEE-754 binary64 (Python float)",
            "inputDefinition": self.input_definition,
            "sourceWkt2_2019": self.source_crs.to_wkt(version="WKT2_2019"),
            "targetWkt2_2019": self.target_crs.to_wkt(version="WKT2_2019"),
            "targetProjJson": self.target_crs.to_json_dict(),
            "forwardTransformerDefinition": self.forward_transformer.definition,
            "inverseTransformerDefinition": self.inverse_transformer.definition,
            "falseEastingM": 0.0,
            "falseNorthingM": 0.0,
            "ellipsoid": "WGS 84",
        }


def round_trip_error_m(
    expected_lon_deg: float,
    expected_lat_deg: float,
    actual_lon_deg: float,
    actual_lat_deg: float,
) -> float:
    """Measure tiny angular differences on the WGS84 local tangent plane.

    This measurement is separate from the projection forward/inverse pair.
    It is more than adequate at the sub-millimetre errors under test.
    """

    lon0, lat0 = validate_lon_lat(expected_lon_deg, expected_lat_deg)
    lon1, lat1 = validate_lon_lat(actual_lon_deg, actual_lat_deg)
    latitude = math.radians((lat0 + lat1) / 2.0)
    flattening = 1.0 / WGS84_INVERSE_FLATTENING
    eccentricity_squared = flattening * (2.0 - flattening)
    sin_lat = math.sin(latitude)
    denominator = math.sqrt(1.0 - eccentricity_squared * sin_lat * sin_lat)
    prime_vertical_radius = WGS84_A_M / denominator
    meridional_radius = (
        WGS84_A_M * (1.0 - eccentricity_squared) / denominator**3
    )
    east = (
        math.radians(lon1 - lon0)
        * prime_vertical_radius
        * math.cos(latitude)
    )
    north = math.radians(lat1 - lat0) * meridional_radius
    return math.hypot(east, north)


def angle_difference_deg(actual: float, expected: float) -> float:
    return abs((actual - expected + 180.0) % 360.0 - 180.0)


def projected_azimuth_deg(point: ProjectedPoint) -> float:
    return math.degrees(math.atan2(point.e, point.n)) % 360.0


def scale_enh(e: float, n: float, scale: float) -> dict[str, float]:
    """Apply the PoC geometry scale exactly once and uniformly."""

    checked_e = _require_finite(e, "easting")
    checked_n = _require_finite(n, "northing")
    checked_scale = _require_finite(scale, "scale")
    if checked_scale <= 0.0:
        raise ValueError("scale must be positive")
    return {
        "e": _clean_zero(checked_scale * checked_e),
        "n": _clean_zero(checked_scale * checked_n),
        "h": 0.0,
    }


def _clean_zero(value: float) -> float:
    return 0.0 if value == 0.0 else float(value)


def measure_bbox(bbox: Mapping[str, float]) -> dict[str, float]:
    """Measure geodesic polygon area and corner diagonal independently."""

    checked = validate_bbox(bbox)
    west, south = checked["west"], checked["south"]
    east, north = checked["east"], checked["north"]
    geod = Geod(a=WGS84_A_M, rf=WGS84_INVERSE_FLATTENING)
    lons = [west, east, east, west]
    lats = [south, south, north, north]
    signed_area, perimeter = geod.polygon_area_perimeter(lons, lats)
    diagonal_distances = []
    for first, second in (
        ((west, south), (east, north)),
        ((west, north), (east, south)),
    ):
        _, _, distance = geod.inv(*first, *second)
        diagonal_distances.append(abs(distance))
    middle_lat = (south + north) / 2.0
    middle_lon = (west + east) / 2.0
    _, _, middle_width = geod.inv(west, middle_lat, east, middle_lat)
    _, _, middle_height = geod.inv(middle_lon, south, middle_lon, north)
    area = abs(signed_area)
    diagonal = max(diagonal_distances)
    return {
        "ellipsoidalGeodesicPolygonAreaM2": area,
        "areaRatio": area / AREA_LIMIT_M2,
        "perimeterM": perimeter,
        "maxCornerDiagonalM": diagonal,
        "diagonalRatio": diagonal / DIAGONAL_LIMIT_M,
        "middleWidthM": abs(middle_width),
        "middleHeightM": abs(middle_height),
    }


def _source_parameter(
    source_start: tuple[float, float],
    source_end: tuple[float, float],
    point: tuple[float, float],
) -> float:
    dx = source_end[0] - source_start[0]
    dy = source_end[1] - source_start[1]
    if abs(dx) >= abs(dy) and dx != 0.0:
        return (point[0] - source_start[0]) / dx
    if dy != 0.0:
        return (point[1] - source_start[1]) / dy
    raise ValueError("source segment must have distinct endpoints")


def clip_crossing_segment(
    segment: Mapping[str, Any],
    bbox_fixture: Mapping[str, float],
) -> dict[str, Any]:
    """Clip a source lon/lat segment before projection using Shapely."""

    checked_bbox = validate_bbox(bbox_fixture)
    source_start = validate_lon_lat(
        segment["start"]["lon_deg"], segment["start"]["lat_deg"]
    )
    source_end = validate_lon_lat(
        segment["end"]["lon_deg"], segment["end"]["lat_deg"]
    )
    clip_box = box(
        checked_bbox["west"],
        checked_bbox["south"],
        checked_bbox["east"],
        checked_bbox["north"],
    )
    if clip_box.covers(Point(source_start)) or clip_box.covers(Point(source_end)):
        raise ValueError("crossing fixture endpoints must both be outside the bbox")
    intersection = LineString([source_start, source_end]).intersection(clip_box)
    if not isinstance(intersection, LineString) or intersection.is_empty:
        raise ValueError("source segment does not produce one clipped line")
    clipped_coordinates = list(intersection.coords)
    if len(clipped_coordinates) < 2:
        raise ValueError("clipped line must have two distinct endpoints")
    endpoints = []
    for coordinate in (clipped_coordinates[0], clipped_coordinates[-1]):
        lon, lat = validate_lon_lat(coordinate[0], coordinate[1])
        source_t = _source_parameter(source_start, source_end, (lon, lat))
        endpoints.append(
            {
                "lonDeg": lon,
                "latDeg": lat,
                "synthetic": True,
                "sourceWayId": str(segment["source_way_id"]),
                "sourceSegmentIndex": int(segment["source_segment_index"]),
                "sourceT": source_t,
            }
        )
    endpoints.sort(key=lambda item: item["sourceT"])
    return {
        "stage": "WGS84 longitude/latitude before projection",
        "start": endpoints[0],
        "end": endpoints[1],
    }


def _point_segment_distance(
    point: ProjectedPoint,
    start: ProjectedPoint,
    end: ProjectedPoint,
) -> float:
    dx = end.e - start.e
    dy = end.n - start.n
    denominator = dx * dx + dy * dy
    if denominator == 0.0:
        return math.hypot(point.e - start.e, point.n - start.n)
    fraction = ((point.e - start.e) * dx + (point.n - start.n) * dy) / denominator
    fraction = min(1.0, max(0.0, fraction))
    closest_e = start.e + fraction * dx
    closest_n = start.n + fraction * dy
    return math.hypot(point.e - closest_e, point.n - closest_n)


def _interpolate_source(
    source_start: tuple[float, float],
    source_end: tuple[float, float],
    source_t: float,
) -> tuple[float, float]:
    return (
        source_start[0] + source_t * (source_end[0] - source_start[0]),
        source_start[1] + source_t * (source_end[1] - source_start[1]),
    )


def densify_projected_segment(
    segment: Mapping[str, Any],
    clipped: Mapping[str, Any],
    projection: LocalAeqd,
    acceptance_m: float = 0.005,
) -> list[dict[str, float]]:
    """Adaptively approximate the projected image of the clipped lon/lat line."""

    tolerance = _require_finite(acceptance_m, "densification acceptance")
    if tolerance <= 0.0 or tolerance >= DISCRETIZATION_LIMIT_M:
        raise ValueError("densification acceptance must be in (0, 0.01) m")
    source_start = validate_lon_lat(
        segment["start"]["lon_deg"], segment["start"]["lat_deg"]
    )
    source_end = validate_lon_lat(
        segment["end"]["lon_deg"], segment["end"]["lat_deg"]
    )

    def projected_at(source_t: float) -> ProjectedPoint:
        lon, lat = _interpolate_source(source_start, source_end, source_t)
        return projection.project(lon, lat)

    t_start = _require_finite(clipped["start"]["sourceT"], "clipped start t")
    t_end = _require_finite(clipped["end"]["sourceT"], "clipped end t")
    if not 0.0 <= t_start < t_end <= 1.0:
        raise ValueError("clipped source parameters must be ordered in [0, 1]")

    def refine(
        first_t: float,
        first_point: ProjectedPoint,
        last_t: float,
        last_point: ProjectedPoint,
        depth: int,
    ) -> list[tuple[float, ProjectedPoint]]:
        if depth > 30:
            raise RuntimeError("densification did not converge")
        samples = []
        for fraction in (0.25, 0.5, 0.75):
            sample_t = first_t + (last_t - first_t) * fraction
            sample_point = projected_at(sample_t)
            samples.append(
                _point_segment_distance(sample_point, first_point, last_point)
            )
        if max(samples) <= tolerance:
            return [(first_t, first_point), (last_t, last_point)]
        middle_t = (first_t + last_t) / 2.0
        middle_point = projected_at(middle_t)
        left = refine(first_t, first_point, middle_t, middle_point, depth + 1)
        right = refine(middle_t, middle_point, last_t, last_point, depth + 1)
        return left[:-1] + right

    refined = refine(
        t_start,
        projected_at(t_start),
        t_end,
        projected_at(t_end),
        0,
    )
    output = []
    for source_t, projected in refined:
        lon, lat = _interpolate_source(source_start, source_end, source_t)
        output.append(
            {
                "sourceT": source_t,
                "lonDeg": lon,
                "latDeg": lat,
                "e": projected.e,
                "n": projected.n,
            }
        )
    return output


def measure_densification_deviation(
    segment: Mapping[str, Any],
    densified: Sequence[Mapping[str, float]],
    projection: LocalAeqd,
    samples_per_interval: int,
) -> float:
    """Independently sample the projected source curve against its polyline."""

    if samples_per_interval < 2:
        raise ValueError("at least two samples per interval are required")
    source_start = validate_lon_lat(
        segment["start"]["lon_deg"], segment["start"]["lat_deg"]
    )
    source_end = validate_lon_lat(
        segment["end"]["lon_deg"], segment["end"]["lat_deg"]
    )
    maximum = 0.0
    for first, last in zip(densified, densified[1:]):
        first_t = float(first["sourceT"])
        last_t = float(last["sourceT"])
        first_projected = ProjectedPoint(float(first["e"]), float(first["n"]))
        last_projected = ProjectedPoint(float(last["e"]), float(last["n"]))
        for index in range(1, samples_per_interval):
            source_t = first_t + (last_t - first_t) * index / samples_per_interval
            lon, lat = _interpolate_source(source_start, source_end, source_t)
            actual = projection.project(lon, lat)
            maximum = max(
                maximum,
                _point_segment_distance(actual, first_projected, last_projected),
            )
    return maximum


def _road(
    road_id: str,
    source_fixture_id: str,
    scale: float,
    backward: Mapping[str, float],
    forward: Mapping[str, float],
) -> dict[str, Any]:
    return {
        "id": road_id,
        "sourceFixtureId": source_fixture_id,
        "scale": float(scale),
        "backward": {
            "e": _clean_zero(_require_finite(backward["e"], "backward e")),
            "n": _clean_zero(_require_finite(backward["n"], "backward n")),
            "h": _clean_zero(_require_finite(backward["h"], "backward h")),
        },
        "forward": {
            "e": _clean_zero(_require_finite(forward["e"], "forward e")),
            "n": _clean_zero(_require_finite(forward["n"], "forward n")),
            "h": _clean_zero(_require_finite(forward["h"], "forward h")),
        },
    }


def build_neutral_model(
    fixtures: Mapping[str, Any],
    projection: LocalAeqd,
) -> dict[str, Any]:
    """Build only the six preregistered, independent native map fixtures."""

    controls = {point["id"]: point for point in fixtures["control_points"]}
    maps = []
    for plan in fixtures["native_fixture_plan"]:
        map_id = plan["id"]
        if plan["kind"] == "projected_control":
            control = controls[plan["control_id"]]
            projected = projection.project(control["lon_deg"], control["lat_deg"])
            scale = float(plan["scale"])
            maps.append(
                {
                    "id": map_id,
                    "roads": [
                        _road(
                            f"{map_id}-road",
                            control["id"],
                            scale,
                            scale_enh(0.0, 0.0, scale),
                            scale_enh(projected.e, projected.n, scale),
                        )
                    ],
                }
            )
        elif plan["kind"] == "tiny_translated_roads":
            roads = []
            for item in fixtures["tiny_translated_roads"]:
                scale = float(item["scale"])
                start = item["start_enh_m"]
                end = item["end_enh_m"]
                roads.append(
                    _road(
                        item["id"],
                        item["id"],
                        scale,
                        scale_enh(start["e"], start["n"], scale),
                        scale_enh(end["e"], end["n"], scale),
                    )
                )
            maps.append({"id": map_id, "roads": roads})
        elif plan["kind"] == "native_radius":
            native = fixtures["native_radius_fixture"]
            scale = float(native["scale"])
            backward_projected = projection.project(
                native["backward"]["lon_deg"], native["backward"]["lat_deg"]
            )
            forward_projected = projection.project(
                native["forward"]["lon_deg"], native["forward"]["lat_deg"]
            )
            maps.append(
                {
                    "id": map_id,
                    "roads": [
                        _road(
                            "near-native-radius-road",
                            "native-radius",
                            scale,
                            scale_enh(backward_projected.e, backward_projected.n, scale),
                            scale_enh(forward_projected.e, forward_projected.n, scale),
                        )
                    ],
                }
            )
        else:
            raise ValueError(f"unsupported experimental fixture kind: {plan['kind']}")

    expected_ids = [item["id"] for item in fixtures["native_fixture_plan"]]
    if [item["id"] for item in maps] != expected_ids or len(maps) != 6:
        raise ValueError("neutral model must contain exactly the six frozen map fixtures")
    return {
        "schemaVersion": 1,
        "poc": fixtures["poc"],
        "coordinateSystem": {
            "sourceAxes": ["E", "N", "H"],
            "unit": "scene metre",
            "candidateMapping": {"x": "E", "y": "H", "z": "-N"},
        },
        "maps": maps,
    }


def build_revised_neutral_model(
    fixtures: Mapping[str, Any],
    projection: LocalAeqd,
) -> dict[str, Any]:
    """Build the v2 ETS2-independent E/N/H transport for the revised rerun."""

    original = build_neutral_model(fixtures, projection)
    return {
        "schemaVersion": 2,
        "poc": original["poc"],
        "coordinateSystem": {
            "axes": ["E", "N", "H"],
            "unit": "scene metre",
        },
        "maps": original["maps"],
    }


def validate_finite_json(value: Any, path: str = "$") -> None:
    if isinstance(value, float) and not math.isfinite(value):
        raise ValueError(f"non-finite JSON number at {path}")
    if isinstance(value, Mapping):
        for key, child in value.items():
            validate_finite_json(child, f"{path}.{key}")
    elif isinstance(value, (list, tuple)):
        for index, child in enumerate(value):
            validate_finite_json(child, f"{path}[{index}]")


def deterministic_json(value: Any) -> str:
    """Serialize inspectable JSON while rejecting JSON's NaN extensions."""

    validate_finite_json(value)
    return json.dumps(
        value,
        ensure_ascii=False,
        indent=2,
        allow_nan=False,
    ) + "\n"


def load_json(path: Path) -> dict[str, Any]:
    def reject_constant(value: str) -> None:
        raise ValueError(f"non-finite JSON constant {value} in {path}")

    loaded = json.loads(path.read_text(encoding="utf-8"), parse_constant=reject_constant)
    if not isinstance(loaded, dict):
        raise ValueError(f"expected JSON object in {path}")
    validate_finite_json(loaded)
    return loaded


def verify_frozen_hashes(fixtures_directory: Path) -> dict[str, str]:
    expected = {}
    for line in (fixtures_directory / "SHA256SUMS").read_text(encoding="utf-8").splitlines():
        digest, filename = line.split(maxsplit=1)
        expected[filename] = digest
    actual = {}
    for filename, expected_digest in expected.items():
        digest = hashlib.sha256((fixtures_directory / filename).read_bytes()).hexdigest()
        if digest != expected_digest:
            raise ValueError(f"frozen fixture hash mismatch for {filename}")
        actual[filename] = digest
    return actual


def runtime_metadata() -> dict[str, Any]:
    standard_gil = bool(getattr(sys, "_is_gil_enabled", lambda: True)())
    return {
        "python": platform.python_version(),
        "pythonImplementation": platform.python_implementation(),
        "standardGilEnabled": standard_gil,
        "pyGilDisabledBuildFlag": int(sysconfig.get_config_var("Py_GIL_DISABLED") or 0),
        "pyproj": pyproj.__version__,
        "proj": pyproj.proj_version_str,
        "shapely": shapely.__version__,
        "projNetworkEnvironment": os.environ.get("PROJ_NETWORK"),
        "projNetworkEnabled": network.is_network_enabled(),
        "floatMantissaBits": sys.float_info.mant_dig,
    }


def require_exact_runtime() -> None:
    metadata = runtime_metadata()
    expected = {
        "python": EXPECTED_PYTHON,
        "pyproj": EXPECTED_PYPROJ,
        "shapely": EXPECTED_SHAPELY,
    }
    for key, version in expected.items():
        if metadata[key] != version:
            raise RuntimeError(f"expected {key} {version}, got {metadata[key]}")
    if not metadata["standardGilEnabled"] or metadata["pyGilDisabledBuildFlag"] != 0:
        raise RuntimeError("PoC-002 requires a standard GIL CPython build")
    if metadata["projNetworkEnabled"] or metadata["projNetworkEnvironment"] != "OFF":
        raise RuntimeError("PROJ network is not fully disabled")
    if metadata["floatMantissaBits"] != 53:
        raise RuntimeError("PoC-002 requires IEEE-754 binary64 Python floats")


def _control_measurements(
    fixtures: Mapping[str, Any],
    references: Mapping[str, Any],
    projection: LocalAeqd,
) -> tuple[list[dict[str, Any]], float, float, float]:
    reference_by_id = {item["id"]: item for item in references["control_points"]}
    output = []
    max_round_trip = 0.0
    max_distance_difference = 0.0
    max_azimuth_difference = 0.0
    for control in fixtures["control_points"]:
        reference = reference_by_id[control["id"]]
        projected = projection.project(control["lon_deg"], control["lat_deg"])
        inverse_lon, inverse_lat = projection.inverse(projected.e, projected.n)
        round_trip = round_trip_error_m(
            control["lon_deg"],
            control["lat_deg"],
            inverse_lon,
            inverse_lat,
        )
        projected_radius = math.hypot(projected.e, projected.n)
        projected_azimuth = projected_azimuth_deg(projected)
        distance_difference = abs(projected_radius - reference["expected_distance_m"])
        azimuth_difference = angle_difference_deg(
            projected_azimuth,
            reference["expected_initial_azimuth_deg"],
        )
        max_round_trip = max(max_round_trip, round_trip)
        max_distance_difference = max(max_distance_difference, distance_difference)
        max_azimuth_difference = max(max_azimuth_difference, azimuth_difference)
        output.append(
            {
                "id": control["id"],
                "wgs84": {
                    "lonDeg": control["lon_deg"],
                    "latDeg": control["lat_deg"],
                },
                "projected": {"eM": projected.e, "nM": projected.n},
                "inverse": {"lonDeg": inverse_lon, "latDeg": inverse_lat},
                "roundTripErrorM": round_trip,
                "independentVincentyReference": {
                    "geodesicDistanceM": reference["expected_distance_m"],
                    "initialAzimuthDeg": reference["expected_initial_azimuth_deg"],
                },
                "aeqdProjectedRadiusM": projected_radius,
                "aeqdProjectedAzimuthDeg": projected_azimuth,
                "radialDistanceDifferenceM": distance_difference,
                "initialAzimuthDifferenceDeg": azimuth_difference,
            }
        )
    return output, max_round_trip, max_distance_difference, max_azimuth_difference


def build_python_validation(
    fixtures: Mapping[str, Any],
    references: Mapping[str, Any],
    fixture_hashes: Mapping[str, str],
    projection: LocalAeqd,
    neutral_model: Mapping[str, Any],
) -> dict[str, Any]:
    controls, max_round_trip, max_distance_difference, max_azimuth_difference = (
        _control_measurements(fixtures, references, projection)
    )

    explicit_origin = origin_from_bbox(fixtures["control_bbox"])
    derived_origin = origin_from_candidate_extent(
        fixtures["candidate_geometry_extent_without_bbox"]["geometries"]
    )

    bbox_measurements = {}
    max_bbox_reference_area_difference = 0.0
    max_bbox_reference_diagonal_difference = 0.0
    for fixture_id in ("area_limit_bbox", "diagonal_limit_bbox"):
        measurement = measure_bbox(fixtures[fixture_id])
        independent = references["bbox_references"][fixture_id]
        area_difference = abs(
            measurement["ellipsoidalGeodesicPolygonAreaM2"]
            - independent["ellipsoidal_geodesic_polygon_area_m2"]
        )
        diagonal_difference = abs(
            measurement["maxCornerDiagonalM"]
            - independent["max_corner_diagonal_m"]
        )
        max_bbox_reference_area_difference = max(
            max_bbox_reference_area_difference, area_difference
        )
        max_bbox_reference_diagonal_difference = max(
            max_bbox_reference_diagonal_difference, diagonal_difference
        )
        bbox_measurements[fixture_id] = {
            **measurement,
            "independentReferenceAreaM2": independent[
                "ellipsoidal_geodesic_polygon_area_m2"
            ],
            "independentReferenceAreaRefinementDeltaM2": independent[
                "area_refinement_delta_m2"
            ],
            "areaReferenceDifferenceM2": area_difference,
            "independentReferenceDiagonalM": independent["max_corner_diagonal_m"],
            "diagonalReferenceDifferenceM": diagonal_difference,
        }

    clipped = clip_crossing_segment(
        fixtures["crossing_segment"],
        fixtures[fixtures["crossing_segment"]["bbox_id"]],
    )
    densified = densify_projected_segment(
        fixtures["crossing_segment"], clipped, projection
    )
    deviation_coarse = measure_densification_deviation(
        fixtures["crossing_segment"], densified, projection, 512
    )
    deviation_fine = measure_densification_deviation(
        fixtures["crossing_segment"], densified, projection, 2048
    )
    deviation_sampling_convergence = abs(deviation_fine - deviation_coarse)
    endpoint_chord_deviation = measure_densification_deviation(
        fixtures["crossing_segment"],
        [densified[0], densified[-1]],
        projection,
        2048,
    )

    scaled_controls = []
    scale_ratio_errors = []
    for control in controls:
        e = control["projected"]["eM"]
        n = control["projected"]["nM"]
        full = scale_enh(e, n, 1.0)
        tenth = scale_enh(e, n, 0.1)
        ratios = {
            "radialRatio": math.hypot(tenth["e"], tenth["n"])
            / math.hypot(full["e"], full["n"]),
        }
        if full["e"] != 0.0:
            ratios["eRatio"] = tenth["e"] / full["e"]
        if full["n"] != 0.0:
            ratios["nRatio"] = tenth["n"] / full["n"]
        scale_ratio_errors.extend(abs(value - 0.1) for value in ratios.values())
        scaled_controls.append(
            {
                "id": control["id"],
                "scale1": full,
                "scale0.1": tenth,
                "ratios": ratios,
            }
        )
    max_scale_ratio_error = max(scale_ratio_errors)

    tiny_measurements = []
    for item in fixtures["tiny_translated_roads"]:
        start = item["start_enh_m"]
        end = item["end_enh_m"]
        scene_start = scale_enh(start["e"], start["n"], item["scale"])
        scene_end = scale_enh(end["e"], end["n"], item["scale"])
        length = math.hypot(
            scene_end["e"] - scene_start["e"],
            scene_end["n"] - scene_start["n"],
        )
        tiny_measurements.append(
            {
                "id": item["id"],
                "offsetMPreScale": item["offset_m_pre_scale"],
                "sceneRoadLengthM": length,
            }
        )

    native = fixtures["native_radius_fixture"]
    native_backward = projection.project(
        native["backward"]["lon_deg"], native["backward"]["lat_deg"]
    )
    native_forward = projection.project(
        native["forward"]["lon_deg"], native["forward"]["lat_deg"]
    )
    scale = native["scale"]
    scaled_backward = scale_enh(native_backward.e, native_backward.n, scale)
    scaled_forward = scale_enh(native_forward.e, native_forward.n, scale)
    scaled_forward_radius = math.hypot(scaled_forward["e"], scaled_forward["n"])
    scaled_road_length = math.hypot(
        scaled_forward["e"] - scaled_backward["e"],
        scaled_forward["n"] - scaled_backward["n"],
    )
    native_admission_origin = origin_from_bbox(fixtures[native["bbox_id"]])

    clip_reference = references["crossing_clip_reference"]
    max_clip_coordinate_difference_deg = max(
        abs(clipped["start"]["lonDeg"] - clip_reference["clipped_start"]["lon_deg"]),
        abs(clipped["start"]["latDeg"] - clip_reference["clipped_start"]["lat_deg"]),
        abs(clipped["end"]["lonDeg"] - clip_reference["clipped_end"]["lon_deg"]),
        abs(clipped["end"]["latDeg"] - clip_reference["clipped_end"]["lat_deg"]),
    )
    max_clip_parameter_difference = max(
        abs(clipped["start"]["sourceT"] - clip_reference["t_enter"]),
        abs(clipped["end"]["sourceT"] - clip_reference["t_exit"]),
    )

    origin_reference = references["expected_origin"]
    derived_reference = references["candidate_extent_origin_without_bbox"]
    map_ids = [item["id"] for item in neutral_model["maps"]]
    checks = [
        {
            "id": "exact-runtime-and-offline-proj",
            "passed": (
                runtime_metadata()["python"] == EXPECTED_PYTHON
                and runtime_metadata()["pyproj"] == EXPECTED_PYPROJ
                and runtime_metadata()["shapely"] == EXPECTED_SHAPELY
                and not network.is_network_enabled()
            ),
        },
        {
            "id": "explicit-origin",
            "passed": (
                explicit_origin.lon_deg == origin_reference["lon_deg"]
                and explicit_origin.lat_deg == origin_reference["lat_deg"]
            ),
        },
        {
            "id": "candidate-extent-origin-before-exclusions",
            "passed": (
                derived_origin.lon_deg == derived_reference["lon_deg"]
                and derived_origin.lat_deg == derived_reference["lat_deg"]
            ),
        },
        {
            "id": "forward-inverse-round-trip",
            "passed": max_round_trip <= ROUND_TRIP_LIMIT_M,
        },
        {
            "id": "independent-vincenty-radial-reference",
            "passed": max_distance_difference <= ROUND_TRIP_LIMIT_M,
        },
        {
            "id": "independent-direction-reference",
            "passed": max_azimuth_difference <= 1e-9,
        },
        {
            "id": "bbox-geodesic-area-and-diagonal",
            "passed": (
                0.95 <= bbox_measurements["area_limit_bbox"]["areaRatio"] <= 0.99
                and bbox_measurements["area_limit_bbox"]["diagonalRatio"] <= 1.0
                and bbox_measurements["diagonal_limit_bbox"]["areaRatio"] <= 1.0
                and 0.95 <= bbox_measurements["diagonal_limit_bbox"]["diagonalRatio"] <= 0.99
            ),
        },
        {
            "id": "clip-before-project-with-provenance",
            "passed": (
                max_clip_coordinate_difference_deg <= 1e-12
                and max_clip_parameter_difference <= 1e-12
                and clipped["start"]["synthetic"]
                and clipped["end"]["synthetic"]
            ),
        },
        {
            "id": "projected-discretization",
            "passed": (
                len(densified) > 2
                and endpoint_chord_deviation > DISCRETIZATION_LIMIT_M
                and deviation_fine + deviation_sampling_convergence
                <= DISCRETIZATION_LIMIT_M
            ),
        },
        {
            "id": "single-uniform-scaling",
            "passed": max_scale_ratio_error <= 1e-15,
        },
        {
            "id": "tiny-offsets-are-translated-roads",
            "passed": all(
                abs(item["sceneRoadLengthM"] - 100.0) <= 1e-12
                for item in tiny_measurements
            ),
        },
        {
            "id": "native-radius-inside-limit",
            "passed": (
                0.95 <= scaled_forward_radius / NATIVE_RADIUS_LIMIT_M <= 0.99
                and native_admission_origin == explicit_origin
                and abs(scaled_forward_radius - native["expected_scaled_radius_m"])
                <= ROUND_TRIP_LIMIT_M
                and abs(scaled_road_length - native["expected_scaled_road_length_m"])
                <= ROUND_TRIP_LIMIT_M
            ),
        },
        {
            "id": "neutral-model-six-independent-maps",
            "passed": map_ids
            == [
                "east-scale-1",
                "north-scale-1",
                "oblique-scale-1",
                "oblique-scale-0.1",
                "tiny-offsets",
                "near-native-radius",
            ],
        },
    ]

    return {
        "schemaVersion": 1,
        "poc": fixtures["poc"],
        "phase": "automatic Python coordinate validation",
        "automaticStatus": "PASS" if all(item["passed"] for item in checks) else "FAIL",
        "runtime": runtime_metadata(),
        "frozenFixtureHashes": dict(fixture_hashes),
        "distanceSemantics": references["distance_semantics"],
        "projection": projection.metadata(),
        "originSelection": {
            "explicitBbox": explicit_origin.as_json(),
            "candidateExtentBeforeLaterMappingExclusions": derived_origin.as_json(),
        },
        "controlPoints": controls,
        "bboxMeasurements": bbox_measurements,
        "clipping": {
            "result": clipped,
            "independentReferenceMethod": clip_reference["algorithm"],
            "maxCoordinateDifferenceDeg": max_clip_coordinate_difference_deg,
            "maxSourceParameterDifference": max_clip_parameter_difference,
        },
        "densification": {
            "acceptanceM": 0.005,
            "pointCount": len(densified),
            "points": densified,
            "independentSampleCountPerInterval": 2048,
            "endpointChordMaxDeviationM": endpoint_chord_deviation,
            "maxDeviationM": deviation_fine,
            "samplingConvergenceM": deviation_sampling_convergence,
            "maxDeviationIncludingSamplingConvergenceM": (
                deviation_fine + deviation_sampling_convergence
            ),
        },
        "scaling": {
            "formula": {"E": "s * e", "N": "s * n", "H": "0"},
            "controlsAtScale1And0.1": scaled_controls,
            "maxRatioError": max_scale_ratio_error,
            "nativeMetadataScaleUsed": False,
            "tinyTranslatedRoads": tiny_measurements,
        },
        "nativeRadiusFixture": {
            "admissionBboxId": native["bbox_id"],
            "admissionBboxOrigin": native_admission_origin.as_json(),
            "scaledForwardRadiusM": scaled_forward_radius,
            "nativeRadiusRatio": scaled_forward_radius / NATIVE_RADIUS_LIMIT_M,
            "scaledRoadLengthM": scaled_road_length,
        },
        "maximumErrors": {
            "geographicRoundTripM": max_round_trip,
            "independentRadialDistanceDifferenceM": max_distance_difference,
            "independentInitialAzimuthDifferenceDeg": max_azimuth_difference,
            "bboxAreaReferenceDifferenceM2": max_bbox_reference_area_difference,
            "bboxDiagonalReferenceDifferenceM": max_bbox_reference_diagonal_difference,
            "projectedDiscretizationM": deviation_fine,
            "projectedDiscretizationSamplingConvergenceM": deviation_sampling_convergence,
            "projectedDiscretizationIncludingSamplingConvergenceM": (
                deviation_fine + deviation_sampling_convergence
            ),
            "scaleRatio": max_scale_ratio_error,
        },
        "checks": checks,
        "nativeCandidateMappingStatus": (
            "encoded as hypothesis only; requires C# conversion and Windows Map Editor validation"
        ),
    }


def _write_automatic_outputs_to(
    experiment_directory: Path,
    output_directory: Path,
    *,
    neutral_model_builder: Callable[
        [Mapping[str, Any], LocalAeqd], dict[str, Any]
    ] = build_neutral_model,
) -> dict[str, Path]:
    require_exact_runtime()
    fixtures_directory = experiment_directory / "fixtures"
    hashes = verify_frozen_hashes(fixtures_directory)
    fixtures = load_json(fixtures_directory / "frozen-fixtures.json")
    references = load_json(fixtures_directory / "independent-reference.json")
    origin = origin_from_bbox(fixtures["control_bbox"])
    projection = LocalAeqd(origin)
    neutral = neutral_model_builder(fixtures, projection)
    validation = build_python_validation(
        fixtures, references, hashes, projection, neutral
    )
    output_directory.mkdir(parents=True, exist_ok=True)
    neutral_path = output_directory / "neutral-model.json"
    validation_path = output_directory / "python-validation.json"
    neutral_path.write_text(deterministic_json(neutral), encoding="utf-8")
    validation_path.write_text(deterministic_json(validation), encoding="utf-8")
    if validation["automaticStatus"] != "PASS":
        raise RuntimeError("one or more automatic Python validation checks failed")
    return {"neutralModel": neutral_path, "pythonValidation": validation_path}


def write_automatic_outputs(experiment_directory: Path) -> dict[str, Path]:
    """Reproduce the original PoC-002 v1 Python output location."""

    return _write_automatic_outputs_to(
        experiment_directory,
        experiment_directory / "output" / "run-automatic",
    )


def write_revised_automatic_outputs(
    experiment_directory: Path,
    output_directory: Path,
) -> dict[str, Path]:
    """Write revised-rerun Python artifacts without reusing a prior directory."""

    if output_directory.exists():
        raise FileExistsError(
            f"revised rerun output directory must not already exist: {output_directory}"
        )
    output_directory.mkdir(parents=True, exist_ok=False)
    return _write_automatic_outputs_to(
        experiment_directory,
        output_directory,
        neutral_model_builder=build_revised_neutral_model,
    )
