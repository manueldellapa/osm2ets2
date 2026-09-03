#!/usr/bin/env python3
"""Freeze PoC-002 fixtures with a pyproj-independent WGS84 reference.

The geodesic calculations use the Vincenty direct/inverse algorithms and only
the Python standard library. This program must be run before the pyproj-based
implementation under test. It writes deterministic JSON into a caller-supplied
directory so the results can be reviewed before they are copied into fixtures/.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import subprocess
from pathlib import Path
from typing import Any

WGS84_A_M = 6_378_137.0
WGS84_INVERSE_FLATTENING = 298.257_223_563
WGS84_F = 1.0 / WGS84_INVERSE_FLATTENING
WGS84_B_M = (1.0 - WGS84_F) * WGS84_A_M
ORIGIN_LON_DEG = 12.4924
ORIGIN_LAT_DEG = 41.8902
FREEZE_DATE = "2026-09-01"


def _normalize_degrees(value: float) -> float:
    return value % 360.0


def vincenty_direct(
    lon_deg: float,
    lat_deg: float,
    azimuth_deg: float,
    distance_m: float,
) -> tuple[float, float, float]:
    """Return lon, lat and final forward azimuth on the WGS84 ellipsoid."""

    if distance_m < 0.0:
        raise ValueError("distance must be non-negative")

    phi1 = math.radians(lat_deg)
    alpha1 = math.radians(azimuth_deg)
    tan_u1 = (1.0 - WGS84_F) * math.tan(phi1)
    cos_u1 = 1.0 / math.sqrt(1.0 + tan_u1 * tan_u1)
    sin_u1 = tan_u1 * cos_u1
    sin_alpha1 = math.sin(alpha1)
    cos_alpha1 = math.cos(alpha1)
    sigma1 = math.atan2(tan_u1, cos_alpha1)
    sin_alpha = cos_u1 * sin_alpha1
    cos_sq_alpha = 1.0 - sin_alpha * sin_alpha
    u_sq = cos_sq_alpha * (
        (WGS84_A_M * WGS84_A_M - WGS84_B_M * WGS84_B_M)
        / (WGS84_B_M * WGS84_B_M)
    )
    coefficient_a = 1.0 + u_sq / 16_384.0 * (
        4_096.0 + u_sq * (-768.0 + u_sq * (320.0 - 175.0 * u_sq))
    )
    coefficient_b = u_sq / 1_024.0 * (
        256.0 + u_sq * (-128.0 + u_sq * (74.0 - 47.0 * u_sq))
    )

    sigma = distance_m / (WGS84_B_M * coefficient_a)
    for _ in range(200):
        two_sigma_m = 2.0 * sigma1 + sigma
        sin_sigma = math.sin(sigma)
        cos_sigma = math.cos(sigma)
        cos_two_sigma_m = math.cos(two_sigma_m)
        delta_sigma = coefficient_b * sin_sigma * (
            cos_two_sigma_m
            + coefficient_b
            / 4.0
            * (
                cos_sigma * (-1.0 + 2.0 * cos_two_sigma_m**2)
                - coefficient_b
                / 6.0
                * cos_two_sigma_m
                * (-3.0 + 4.0 * sin_sigma**2)
                * (-3.0 + 4.0 * cos_two_sigma_m**2)
            )
        )
        next_sigma = distance_m / (WGS84_B_M * coefficient_a) + delta_sigma
        if abs(next_sigma - sigma) <= 1e-13:
            sigma = next_sigma
            break
        sigma = next_sigma
    else:
        raise RuntimeError("Vincenty direct calculation did not converge")

    two_sigma_m = 2.0 * sigma1 + sigma
    sin_sigma = math.sin(sigma)
    cos_sigma = math.cos(sigma)
    cos_two_sigma_m = math.cos(two_sigma_m)
    tmp = sin_u1 * sin_sigma - cos_u1 * cos_sigma * cos_alpha1
    phi2 = math.atan2(
        sin_u1 * cos_sigma + cos_u1 * sin_sigma * cos_alpha1,
        (1.0 - WGS84_F) * math.sqrt(sin_alpha**2 + tmp**2),
    )
    lam = math.atan2(
        sin_sigma * sin_alpha1,
        cos_u1 * cos_sigma - sin_u1 * sin_sigma * cos_alpha1,
    )
    coefficient_c = WGS84_F / 16.0 * cos_sq_alpha * (
        4.0 + WGS84_F * (4.0 - 3.0 * cos_sq_alpha)
    )
    longitude_delta = lam - (1.0 - coefficient_c) * WGS84_F * sin_alpha * (
        sigma
        + coefficient_c
        * sin_sigma
        * (
            cos_two_sigma_m
            + coefficient_c * cos_sigma * (-1.0 + 2.0 * cos_two_sigma_m**2)
        )
    )
    alpha2 = math.atan2(sin_alpha, -tmp)
    lon2 = ((lon_deg + math.degrees(longitude_delta) + 540.0) % 360.0) - 180.0
    return lon2, math.degrees(phi2), _normalize_degrees(math.degrees(alpha2))


def vincenty_inverse(
    lon1_deg: float,
    lat1_deg: float,
    lon2_deg: float,
    lat2_deg: float,
) -> tuple[float, float, float]:
    """Return distance, initial azimuth and reverse-point forward azimuth."""

    phi1 = math.radians(lat1_deg)
    phi2 = math.radians(lat2_deg)
    longitude_delta = math.radians(lon2_deg - lon1_deg)
    u1 = math.atan((1.0 - WGS84_F) * math.tan(phi1))
    u2 = math.atan((1.0 - WGS84_F) * math.tan(phi2))
    sin_u1, cos_u1 = math.sin(u1), math.cos(u1)
    sin_u2, cos_u2 = math.sin(u2), math.cos(u2)
    lam = longitude_delta

    for _ in range(200):
        sin_lam, cos_lam = math.sin(lam), math.cos(lam)
        sin_sigma = math.sqrt(
            (cos_u2 * sin_lam) ** 2
            + (cos_u1 * sin_u2 - sin_u1 * cos_u2 * cos_lam) ** 2
        )
        if sin_sigma == 0.0:
            return 0.0, 0.0, 0.0
        cos_sigma = sin_u1 * sin_u2 + cos_u1 * cos_u2 * cos_lam
        sigma = math.atan2(sin_sigma, cos_sigma)
        sin_alpha = cos_u1 * cos_u2 * sin_lam / sin_sigma
        cos_sq_alpha = 1.0 - sin_alpha * sin_alpha
        cos_two_sigma_m = (
            cos_sigma - 2.0 * sin_u1 * sin_u2 / cos_sq_alpha
            if cos_sq_alpha > 1e-30
            else 0.0
        )
        coefficient_c = WGS84_F / 16.0 * cos_sq_alpha * (
            4.0 + WGS84_F * (4.0 - 3.0 * cos_sq_alpha)
        )
        next_lam = longitude_delta + (1.0 - coefficient_c) * WGS84_F * sin_alpha * (
            sigma
            + coefficient_c
            * sin_sigma
            * (
                cos_two_sigma_m
                + coefficient_c
                * cos_sigma
                * (-1.0 + 2.0 * cos_two_sigma_m**2)
            )
        )
        if abs(next_lam - lam) <= 1e-13:
            lam = next_lam
            break
        lam = next_lam
    else:
        raise RuntimeError("Vincenty inverse calculation did not converge")

    sin_lam, cos_lam = math.sin(lam), math.cos(lam)
    sin_sigma = math.sqrt(
        (cos_u2 * sin_lam) ** 2
        + (cos_u1 * sin_u2 - sin_u1 * cos_u2 * cos_lam) ** 2
    )
    cos_sigma = sin_u1 * sin_u2 + cos_u1 * cos_u2 * cos_lam
    sigma = math.atan2(sin_sigma, cos_sigma)
    sin_alpha = cos_u1 * cos_u2 * sin_lam / sin_sigma
    cos_sq_alpha = 1.0 - sin_alpha * sin_alpha
    cos_two_sigma_m = (
        cos_sigma - 2.0 * sin_u1 * sin_u2 / cos_sq_alpha
        if cos_sq_alpha > 1e-30
        else 0.0
    )
    u_sq = cos_sq_alpha * (
        (WGS84_A_M * WGS84_A_M - WGS84_B_M * WGS84_B_M)
        / (WGS84_B_M * WGS84_B_M)
    )
    coefficient_a = 1.0 + u_sq / 16_384.0 * (
        4_096.0 + u_sq * (-768.0 + u_sq * (320.0 - 175.0 * u_sq))
    )
    coefficient_b = u_sq / 1_024.0 * (
        256.0 + u_sq * (-128.0 + u_sq * (74.0 - 47.0 * u_sq))
    )
    delta_sigma = coefficient_b * sin_sigma * (
        cos_two_sigma_m
        + coefficient_b
        / 4.0
        * (
            cos_sigma * (-1.0 + 2.0 * cos_two_sigma_m**2)
            - coefficient_b
            / 6.0
            * cos_two_sigma_m
            * (-3.0 + 4.0 * sin_sigma**2)
            * (-3.0 + 4.0 * cos_two_sigma_m**2)
        )
    )
    distance_m = WGS84_B_M * coefficient_a * (sigma - delta_sigma)
    initial = math.atan2(
        cos_u2 * sin_lam,
        cos_u1 * sin_u2 - sin_u1 * cos_u2 * cos_lam,
    )
    final = math.atan2(
        cos_u1 * sin_lam,
        -sin_u1 * cos_u2 + cos_u1 * sin_u2 * cos_lam,
    )
    return (
        distance_m,
        _normalize_degrees(math.degrees(initial)),
        _normalize_degrees(math.degrees(final)),
    )


def _metres_per_degree(lon_lat: tuple[float, float]) -> tuple[float, float]:
    _, lat_deg = lon_lat
    phi = math.radians(lat_deg)
    eccentricity_sq = WGS84_F * (2.0 - WGS84_F)
    denominator = math.sqrt(1.0 - eccentricity_sq * math.sin(phi) ** 2)
    prime_vertical = WGS84_A_M / denominator
    meridional = WGS84_A_M * (1.0 - eccentricity_sq) / denominator**3
    return (
        math.pi / 180.0 * prime_vertical * math.cos(phi),
        math.pi / 180.0 * meridional,
    )


def _bbox_for_dimensions(width_m: float, height_m: float) -> dict[str, float]:
    metres_lon, metres_lat = _metres_per_degree((ORIGIN_LON_DEG, ORIGIN_LAT_DEG))
    half_lon = width_m / (2.0 * metres_lon)
    half_lat = height_m / (2.0 * metres_lat)
    return {
        "west": ORIGIN_LON_DEG - half_lon,
        "south": ORIGIN_LAT_DEG - half_lat,
        "east": ORIGIN_LON_DEG + half_lon,
        "north": ORIGIN_LAT_DEG + half_lat,
    }


def _ellipsoidal_area_potential(lat_rad: float, reference_lat_rad: float) -> float:
    """Primitive of the WGS84 surface-area element, shifted for stability."""

    eccentricity_sq = WGS84_F * (2.0 - WGS84_F)
    eccentricity = math.sqrt(eccentricity_sq)

    def primitive(phi: float) -> float:
        sin_phi = math.sin(phi)
        return WGS84_A_M**2 * (1.0 - eccentricity_sq) / 2.0 * (
            sin_phi / (1.0 - eccentricity_sq * sin_phi**2)
            + math.atanh(eccentricity * sin_phi) / eccentricity
        )

    return primitive(lat_rad) - primitive(reference_lat_rad)


def ellipsoidal_geodesic_polygon_area(
    points_lon_lat: list[tuple[float, float]],
    subdivisions_per_edge: int,
) -> float:
    """Numerically integrate a Vincenty-geodesic polygon on WGS84.

    The line integral uses a primitive of the exact ellipsoidal surface-area
    element. Densely sampled geodesic edges come from the independent Vincenty
    implementation above. The fixtures do not cross the antimeridian.
    """

    if subdivisions_per_edge < 1:
        raise ValueError("subdivisions_per_edge must be positive")
    reference_lat_rad = math.radians(
        sum(lat for _, lat in points_lon_lat) / len(points_lon_lat)
    )
    integral = 0.0
    closed = points_lon_lat + [points_lon_lat[0]]
    for start, end in zip(closed, closed[1:]):
        distance_m, azimuth_deg, _ = vincenty_inverse(*start, *end)
        previous_lon_rad = math.radians(start[0])
        previous_potential = _ellipsoidal_area_potential(
            math.radians(start[1]), reference_lat_rad
        )
        for index in range(1, subdivisions_per_edge + 1):
            if index == subdivisions_per_edge:
                lon_deg, lat_deg = end
            else:
                lon_deg, lat_deg, _ = vincenty_direct(
                    *start,
                    azimuth_deg,
                    distance_m * index / subdivisions_per_edge,
                )
            lon_rad = math.radians(lon_deg)
            longitude_step = lon_rad - previous_lon_rad
            if longitude_step > math.pi:
                longitude_step -= 2.0 * math.pi
            elif longitude_step < -math.pi:
                longitude_step += 2.0 * math.pi
            potential = _ellipsoidal_area_potential(
                math.radians(lat_deg), reference_lat_rad
            )
            integral += 0.5 * (previous_potential + potential) * longitude_step
            previous_lon_rad = lon_rad
            previous_potential = potential
    return abs(integral)


def _bbox_reference(bbox: dict[str, float]) -> dict[str, float]:
    west, south, east, north = (
        bbox["west"],
        bbox["south"],
        bbox["east"],
        bbox["north"],
    )
    middle_lat = (south + north) / 2.0
    middle_lon = (west + east) / 2.0
    width_m = vincenty_inverse(west, middle_lat, east, middle_lat)[0]
    height_m = vincenty_inverse(middle_lon, south, middle_lon, north)[0]
    diagonals = (
        vincenty_inverse(west, south, east, north)[0],
        vincenty_inverse(west, north, east, south)[0],
    )
    polygon = [
        (west, south),
        (east, south),
        (east, north),
        (west, north),
    ]
    area_coarse_m2 = ellipsoidal_geodesic_polygon_area(polygon, 2_048)
    area_m2 = ellipsoidal_geodesic_polygon_area(polygon, 4_096)
    return {
        "middle_width_m": width_m,
        "middle_height_m": height_m,
        "max_corner_diagonal_m": max(diagonals),
        "ellipsoidal_geodesic_polygon_area_m2": area_m2,
        "ellipsoidal_geodesic_polygon_area_ratio": area_m2 / 25_000_000.0,
        "area_refinement_delta_m2": abs(area_m2 - area_coarse_m2),
        "diagonal_ratio": max(diagonals) / 10_000.0,
    }


def _clip_liang_barsky(
    start: tuple[float, float],
    end: tuple[float, float],
    bbox: dict[str, float],
) -> tuple[float, float, tuple[float, float], tuple[float, float]]:
    x0, y0 = start
    x1, y1 = end
    dx, dy = x1 - x0, y1 - y0
    t_enter, t_exit = 0.0, 1.0
    for p, q in (
        (-dx, x0 - bbox["west"]),
        (dx, bbox["east"] - x0),
        (-dy, y0 - bbox["south"]),
        (dy, bbox["north"] - y0),
    ):
        if p == 0.0:
            if q < 0.0:
                raise ValueError("segment does not cross bbox")
            continue
        ratio = q / p
        if p < 0.0:
            t_enter = max(t_enter, ratio)
        else:
            t_exit = min(t_exit, ratio)
        if t_enter > t_exit:
            raise ValueError("segment does not cross bbox")

    interpolate = lambda t: (x0 + t * dx, y0 + t * dy)
    return t_enter, t_exit, interpolate(t_enter), interpolate(t_exit)


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _dump(path: Path, value: Any) -> None:
    path.write_text(
        json.dumps(value, indent=2, sort_keys=True, allow_nan=False) + "\n",
        encoding="utf-8",
    )


def build_documents(repo_root: Path) -> tuple[dict[str, Any], dict[str, Any]]:
    origin = {"lon_deg": ORIGIN_LON_DEG, "lat_deg": ORIGIN_LAT_DEG}
    control_specs = (
        ("east", 90.0, 123.456),
        ("north", 0.0, 234.567),
        ("west", 270.0, 345.678),
        ("south", 180.0, 456.789),
        ("oblique", 37.125, 321.123),
    )
    control_points: list[dict[str, Any]] = []
    reference_controls: list[dict[str, Any]] = []
    for identifier, azimuth_deg, distance_m in control_specs:
        lon_deg, lat_deg, final_azimuth_deg = vincenty_direct(
            ORIGIN_LON_DEG,
            ORIGIN_LAT_DEG,
            azimuth_deg,
            distance_m,
        )
        inverse_distance, inverse_azimuth, inverse_final = vincenty_inverse(
            ORIGIN_LON_DEG,
            ORIGIN_LAT_DEG,
            lon_deg,
            lat_deg,
        )
        control_points.append(
            {
                "id": identifier,
                "lon_deg": lon_deg,
                "lat_deg": lat_deg,
            }
        )
        reference_controls.append(
            {
                "id": identifier,
                "expected_initial_azimuth_deg": inverse_azimuth,
                "expected_final_forward_azimuth_deg": inverse_final,
                "expected_distance_m": inverse_distance,
                "construction_azimuth_deg": azimuth_deg,
                "construction_distance_m": distance_m,
                "direct_final_forward_azimuth_deg": final_azimuth_deg,
            }
        )

    area_bbox = _bbox_for_dimensions(5_000.0, 4_900.0)
    diagonal_bbox = _bbox_for_dimensions(9_700.0, 1_000.0)
    control_bbox = _bbox_for_dimensions(1_500.0, 1_500.0)
    height = diagonal_bbox["north"] - diagonal_bbox["south"]
    crossing_start = (
        diagonal_bbox["west"] - 0.02,
        diagonal_bbox["south"] + 0.20 * height,
    )
    crossing_end = (
        diagonal_bbox["east"] + 0.02,
        diagonal_bbox["north"] - 0.15 * height,
    )
    t_enter, t_exit, clipped_start, clipped_end = _clip_liang_barsky(
        crossing_start,
        crossing_end,
        diagonal_bbox,
    )

    radius_backward_lon, radius_backward_lat, _ = vincenty_direct(
        ORIGIN_LON_DEG,
        ORIGIN_LAT_DEG,
        87.0,
        4_700.0,
    )
    radius_forward_lon, radius_forward_lat, _ = vincenty_direct(
        ORIGIN_LON_DEG,
        ORIGIN_LAT_DEG,
        87.0,
        4_800.0,
    )
    radius_backward_distance, radius_azimuth, _ = vincenty_inverse(
        ORIGIN_LON_DEG,
        ORIGIN_LAT_DEG,
        radius_backward_lon,
        radius_backward_lat,
    )
    radius_forward_distance, radius_forward_azimuth, _ = vincenty_inverse(
        ORIGIN_LON_DEG,
        ORIGIN_LAT_DEG,
        radius_forward_lon,
        radius_forward_lat,
    )
    radius_road_distance, radius_road_azimuth, _ = vincenty_inverse(
        radius_backward_lon,
        radius_backward_lat,
        radius_forward_lon,
        radius_forward_lat,
    )
    radius_scale = 2.06

    fixture_document: dict[str, Any] = {
        "schema_version": 1,
        "poc": "PoC-002 — Coordinate and Geometry Validation",
        "frozen_on": FREEZE_DATE,
        "wgs84": {
            "semi_major_axis_m": WGS84_A_M,
            "inverse_flattening": WGS84_INVERSE_FLATTENING,
        },
        "origin": origin,
        "control_bbox": control_bbox,
        "control_points": control_points,
        "scale_factors": [1.0, 0.1],
        "tiny_translated_roads": [
            {
                "id": f"tiny-offset-{offset:g}",
                "offset_m_pre_scale": offset,
                "scale": 1.0,
                "start_enh_m": {"e": offset, "n": 0.0, "h": 0.0},
                "end_enh_m": {"e": 100.0 + offset, "n": 0.0, "h": 0.0},
            }
            for offset in (0.001, 0.01, 0.1)
        ],
        "area_limit_bbox": area_bbox,
        "diagonal_limit_bbox": diagonal_bbox,
        "native_radius_fixture": {
            "bbox_id": "diagonal_limit_bbox",
            "scale": radius_scale,
            "backward": {
                "lon_deg": radius_backward_lon,
                "lat_deg": radius_backward_lat,
                "expected_unscaled_radius_m": radius_backward_distance,
            },
            "forward": {
                "lon_deg": radius_forward_lon,
                "lat_deg": radius_forward_lat,
                "expected_unscaled_radius_m": radius_forward_distance,
            },
            "expected_unscaled_road_length_m": radius_road_distance,
            "expected_scaled_road_length_m": radius_road_distance * radius_scale,
            "expected_scaled_radius_m": radius_forward_distance * radius_scale,
        },
        "crossing_segment": {
            "source_way_id": "synthetic-way-crossing-001",
            "source_segment_index": 0,
            "bbox_id": "diagonal_limit_bbox",
            "start": {"lon_deg": crossing_start[0], "lat_deg": crossing_start[1]},
            "end": {"lon_deg": crossing_end[0], "lat_deg": crossing_end[1]},
        },
        "candidate_geometry_extent_without_bbox": {
            "geometries": [
                {
                    "id": "candidate-west-south",
                    "later_mapping_excluded": False,
                    "points": [
                        {"lon_deg": 12.5900, "lat_deg": 41.7950},
                        {"lon_deg": 12.5980, "lat_deg": 41.8020},
                    ],
                },
                {
                    "id": "candidate-east-north-excluded-later",
                    "later_mapping_excluded": True,
                    "points": [
                        {"lon_deg": 12.6030, "lat_deg": 41.7990},
                        {"lon_deg": 12.6100, "lat_deg": 41.8050},
                    ],
                },
            ]
        },
        "native_fixture_plan": [
            {"id": "east-scale-1", "kind": "projected_control", "control_id": "east", "scale": 1.0},
            {"id": "north-scale-1", "kind": "projected_control", "control_id": "north", "scale": 1.0},
            {"id": "oblique-scale-1", "kind": "projected_control", "control_id": "oblique", "scale": 1.0},
            {"id": "oblique-scale-0.1", "kind": "projected_control", "control_id": "oblique", "scale": 0.1},
            {"id": "tiny-offsets", "kind": "tiny_translated_roads"},
            {"id": "near-native-radius", "kind": "native_radius"},
        ],
    }

    script_path = Path(__file__).resolve()
    reference_document: dict[str, Any] = {
        "schema_version": 1,
        "poc": "PoC-002 — Coordinate and Geometry Validation",
        "frozen_on": FREEZE_DATE,
        "method": {
            "name": "Vincenty direct/inverse on the WGS84 ellipsoid",
            "implementation": "Python standard library only; no pyproj, PROJ, Shapely, or GeographicLib",
            "primary_method_provenance": "https://www.ngs.noaa.gov/PC_PROD/Inv_Fwd/readme.htm",
            "ellipsoid_provenance": "https://epsg.org/ellipsoid_7030/WGS-84.html",
            "source_path": str(script_path.relative_to(repo_root)),
            "source_sha256": _sha256(script_path),
        },
        "canonical_baseline": {
            "head": subprocess.run(
                ["git", "rev-parse", "HEAD"],
                cwd=repo_root,
                check=True,
                capture_output=True,
                text=True,
            ).stdout.strip(),
            "prd_sha256": _sha256(repo_root / "tasks/prd-osm2ets2-mvp.md"),
            "spike_plan_sha256": _sha256(repo_root / "tasks/spikes-osm2ets2-mvp.md"),
        },
        "expected_origin": origin,
        "control_points": reference_controls,
        "bbox_references": {
            "area_limit_bbox": _bbox_reference(area_bbox),
            "diagonal_limit_bbox": _bbox_reference(diagonal_bbox),
        },
        "native_radius_reference": {
            "expected_initial_azimuth_deg": radius_azimuth,
            "expected_forward_initial_azimuth_deg": radius_forward_azimuth,
            "expected_road_initial_azimuth_deg": radius_road_azimuth,
            "expected_backward_unscaled_radius_m": radius_backward_distance,
            "expected_forward_unscaled_radius_m": radius_forward_distance,
            "expected_unscaled_road_length_m": radius_road_distance,
            "scale": radius_scale,
            "expected_scaled_road_length_m": radius_road_distance * radius_scale,
            "expected_scaled_radius_m": radius_forward_distance * radius_scale,
            "native_radius_ratio": radius_forward_distance * radius_scale / 10_000.0,
            "admission_bbox_id": "diagonal_limit_bbox",
        },
        "crossing_clip_reference": {
            "algorithm": "analytic Liang-Barsky in longitude/latitude",
            "t_enter": t_enter,
            "t_exit": t_exit,
            "clipped_start": {"lon_deg": clipped_start[0], "lat_deg": clipped_start[1]},
            "clipped_end": {"lon_deg": clipped_end[0], "lat_deg": clipped_end[1]},
        },
        "candidate_extent_origin_without_bbox": {
            "lon_deg": 12.6000,
            "lat_deg": 41.8000,
            "selection_stage": "all candidate geometry before later mapping exclusions",
        },
        "distance_semantics": {
            "geodesic": "Vincenty ellipsoidal surface distance on WGS84",
            "aeqd_projected": "Euclidean easting/northing distance before scale; radial equality is expected only from the centre",
            "scene_native": "Euclidean E/N/H or mapped X/Y/Z distance after explicit geometry scale",
        },
    }
    return fixture_document, reference_document


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    repo_root = args.repo_root.resolve()
    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    fixtures, reference = build_documents(repo_root)
    fixture_path = output_dir / "frozen-fixtures.json"
    reference_path = output_dir / "independent-reference.json"
    _dump(fixture_path, fixtures)
    _dump(reference_path, reference)
    print(f"wrote {fixture_path} sha256={_sha256(fixture_path)}")
    print(f"wrote {reference_path} sha256={_sha256(reference_path)}")


if __name__ == "__main__":
    main()
