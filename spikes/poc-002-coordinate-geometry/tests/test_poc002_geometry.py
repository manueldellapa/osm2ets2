from __future__ import annotations

import json
import math
import tempfile
import unittest
from pathlib import Path

from pyproj import network
from shapely.geometry import Point, box

from poc002_geometry import (
    AREA_LIMIT_M2,
    DIAGONAL_LIMIT_M,
    DISCRETIZATION_LIMIT_M,
    LocalAeqd,
    NATIVE_RADIUS_LIMIT_M,
    Origin,
    ProjectedPoint,
    angle_difference_deg,
    build_neutral_model,
    build_python_validation,
    clip_crossing_segment,
    densify_projected_segment,
    deterministic_json,
    load_json,
    measure_bbox,
    measure_densification_deviation,
    origin_from_bbox,
    origin_from_candidate_extent,
    projected_azimuth_deg,
    require_exact_runtime,
    round_trip_error_m,
    runtime_metadata,
    scale_enh,
    validate_finite_json,
    validate_lon_lat,
    verify_frozen_hashes,
)


EXPERIMENT_DIR = Path(__file__).resolve().parents[1]
FIXTURE_DIR = EXPERIMENT_DIR / "fixtures"


class Poc002GeometryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.fixtures = load_json(FIXTURE_DIR / "frozen-fixtures.json")
        cls.references = load_json(FIXTURE_DIR / "independent-reference.json")
        cls.fixture_hashes = verify_frozen_hashes(FIXTURE_DIR)
        cls.origin = origin_from_bbox(cls.fixtures["control_bbox"])
        cls.projection = LocalAeqd(cls.origin)
        cls.reference_by_id = {
            item["id"]: item for item in cls.references["control_points"]
        }
        cls.control_by_id = {
            item["id"]: item for item in cls.fixtures["control_points"]
        }

    def test_exact_runtime_standard_gil_float64_and_proj_network_off(self) -> None:
        require_exact_runtime()
        metadata = runtime_metadata()
        self.assertEqual(metadata["python"], "3.14.7")
        self.assertEqual(metadata["pyproj"], "3.7.2")
        self.assertEqual(metadata["shapely"], "2.1.2")
        self.assertEqual(metadata["floatMantissaBits"], 53)
        self.assertTrue(metadata["standardGilEnabled"])
        self.assertEqual(metadata["projNetworkEnvironment"], "OFF")
        self.assertFalse(network.is_network_enabled())

    def test_frozen_fixture_hashes_are_verified(self) -> None:
        self.assertEqual(
            self.fixture_hashes,
            {
                "frozen-fixtures.json": (
                    "3df7f774af4b7a9e6b420871e0fc9a3115c3673964f22dea405e46a53ff43f4b"
                ),
                "independent-reference.json": (
                    "3ed376bcda2f8819cd5dd461f569d641e2ea19787bfc7219a9c9c7b67e166a9c"
                ),
            },
        )

    def test_longitude_latitude_order_and_domain_rejection(self) -> None:
        self.assertEqual(validate_lon_lat(12.4924, 41.8902), (12.4924, 41.8902))
        east = self.control_by_id["east"]
        projected = self.projection.project(east["lon_deg"], east["lat_deg"])
        self.assertGreater(projected.e, 100.0)
        self.assertAlmostEqual(projected.n, 0.0, delta=1e-6)
        with self.assertRaises(ValueError):
            validate_lon_lat(181.0, 41.0)
        with self.assertRaises(ValueError):
            validate_lon_lat(12.0, 80.0000001)
        with self.assertRaises(ValueError):
            validate_lon_lat(float("nan"), 41.0)
        with self.assertRaises(ValueError):
            self.projection.project(float("inf"), 41.0)

    def test_explicit_bbox_origin_is_deterministic(self) -> None:
        first = origin_from_bbox(self.fixtures["control_bbox"])
        second = origin_from_bbox(dict(reversed(self.fixtures["control_bbox"].items())))
        expected = self.references["expected_origin"]
        self.assertEqual(first, second)
        self.assertEqual(first.lon_deg, expected["lon_deg"])
        self.assertEqual(first.lat_deg, expected["lat_deg"])

    def test_candidate_extent_origin_is_selected_before_later_exclusions(self) -> None:
        geometries = self.fixtures["candidate_geometry_extent_without_bbox"][
            "geometries"
        ]
        selected = origin_from_candidate_extent(geometries)
        expected = self.references["candidate_extent_origin_without_bbox"]
        self.assertEqual(selected.lon_deg, expected["lon_deg"])
        self.assertEqual(selected.lat_deg, expected["lat_deg"])
        retained_later = [
            geometry
            for geometry in geometries
            if not geometry["later_mapping_excluded"]
        ]
        moved_if_incorrectly_reselected = origin_from_candidate_extent(retained_later)
        self.assertNotEqual(selected, moved_if_incorrectly_reselected)

    def test_crs_is_explicit_ellipsoidal_aeqd_with_zero_false_origin(self) -> None:
        metadata = self.projection.metadata()
        definition = metadata["inputDefinition"]
        self.assertIn("+proj=aeqd", definition)
        self.assertIn("+ellps=WGS84", definition)
        self.assertIn("+x_0=0", definition)
        self.assertIn("+y_0=0", definition)
        self.assertEqual(metadata["sourceCoordinateOrder"], ["longitude", "latitude"])
        self.assertTrue(metadata["alwaysXy"])
        self.assertEqual(metadata["numericType"], "IEEE-754 binary64 (Python float)")
        origin_projected = self.projection.project(
            self.origin.lon_deg, self.origin.lat_deg
        )
        self.assertAlmostEqual(origin_projected.e, 0.0, delta=1e-9)
        self.assertAlmostEqual(origin_projected.n, 0.0, delta=1e-9)
        self.assertIn("PROJCRS", metadata["targetWkt2_2019"])
        self.assertEqual(metadata["targetProjJson"]["type"], "ProjectedCRS")

    def test_cardinal_direction_signs_and_oblique_quadrant(self) -> None:
        projected = {
            control_id: self.projection.project(item["lon_deg"], item["lat_deg"])
            for control_id, item in self.control_by_id.items()
        }
        self.assertGreater(projected["east"].e, 0.0)
        self.assertGreater(projected["north"].n, 0.0)
        self.assertLess(projected["west"].e, 0.0)
        self.assertLess(projected["south"].n, 0.0)
        self.assertGreater(projected["oblique"].e, 0.0)
        self.assertGreater(projected["oblique"].n, 0.0)
        self.assertAlmostEqual(projected["east"].n, 0.0, delta=1e-6)
        self.assertAlmostEqual(projected["north"].e, 0.0, delta=1e-6)
        self.assertAlmostEqual(projected["west"].n, 0.0, delta=1e-6)
        self.assertAlmostEqual(projected["south"].e, 0.0, delta=1e-6)

    def test_forward_coordinates_match_independent_vincenty_references(self) -> None:
        for control_id, control in self.control_by_id.items():
            with self.subTest(control_id=control_id):
                projected = self.projection.project(
                    control["lon_deg"], control["lat_deg"]
                )
                reference = self.reference_by_id[control_id]
                self.assertAlmostEqual(
                    math.hypot(projected.e, projected.n),
                    reference["expected_distance_m"],
                    delta=0.001,
                )
                self.assertLessEqual(
                    angle_difference_deg(
                        projected_azimuth_deg(projected),
                        reference["expected_initial_azimuth_deg"],
                    ),
                    1e-9,
                )

    def test_forward_inverse_round_trip_is_sub_millimetre(self) -> None:
        errors = []
        for control in self.fixtures["control_points"]:
            projected = self.projection.project(
                control["lon_deg"], control["lat_deg"]
            )
            lon, lat = self.projection.inverse(projected.e, projected.n)
            errors.append(
                round_trip_error_m(
                    control["lon_deg"], control["lat_deg"], lon, lat
                )
            )
        self.assertLessEqual(max(errors), 0.001)
        with self.assertRaises(ValueError):
            self.projection.inverse(float("nan"), 0.0)

    def test_aeqd_radial_semantics_do_not_claim_arbitrary_pairwise_equality(self) -> None:
        semantics = self.references["distance_semantics"]
        self.assertIn("only from the centre", semantics["aeqd_projected"])
        east = self.control_by_id["east"]
        north = self.control_by_id["north"]
        projected_east = self.projection.project(east["lon_deg"], east["lat_deg"])
        projected_north = self.projection.project(north["lon_deg"], north["lat_deg"])
        self.assertTrue(
            math.isfinite(
                math.hypot(
                    projected_east.e - projected_north.e,
                    projected_east.n - projected_north.n,
                )
            )
        )

    def test_scale_is_applied_once_uniformly_at_one_and_one_tenth(self) -> None:
        for control in self.control_by_id.values():
            with self.subTest(control=control["id"]):
                projected = self.projection.project(
                    control["lon_deg"], control["lat_deg"]
                )
                full = scale_enh(projected.e, projected.n, 1.0)
                tenth = scale_enh(projected.e, projected.n, 0.1)
                if full["e"] != 0.0:
                    self.assertAlmostEqual(
                        tenth["e"] / full["e"], 0.1, delta=1e-15
                    )
                if full["n"] != 0.0:
                    self.assertAlmostEqual(
                        tenth["n"] / full["n"], 0.1, delta=1e-15
                    )
                self.assertAlmostEqual(
                    math.hypot(tenth["e"], tenth["n"])
                    / math.hypot(full["e"], full["n"]),
                    0.1,
                    delta=1e-15,
                )
                self.assertEqual(full["h"], 0.0)
        with self.assertRaises(ValueError):
            scale_enh(1.0, 2.0, float("inf"))

    def test_tiny_offsets_translate_valid_hundred_metre_roads(self) -> None:
        for item in self.fixtures["tiny_translated_roads"]:
            with self.subTest(item=item["id"]):
                start = item["start_enh_m"]
                end = item["end_enh_m"]
                self.assertIn(item["offset_m_pre_scale"], (0.001, 0.01, 0.1))
                self.assertAlmostEqual(
                    math.hypot(end["e"] - start["e"], end["n"] - start["n"]),
                    100.0,
                    delta=1e-12,
                )

    def test_area_limit_bbox_uses_geodesic_polygon_area(self) -> None:
        measured = measure_bbox(self.fixtures["area_limit_bbox"])
        reference = self.references["bbox_references"]["area_limit_bbox"]
        self.assertGreaterEqual(measured["areaRatio"], 0.95)
        self.assertLessEqual(measured["areaRatio"], 0.99)
        self.assertLessEqual(measured["maxCornerDiagonalM"], DIAGONAL_LIMIT_M)
        self.assertAlmostEqual(
            measured["ellipsoidalGeodesicPolygonAreaM2"],
            reference["ellipsoidal_geodesic_polygon_area_m2"],
            delta=0.001,
        )
        self.assertLess(measured["ellipsoidalGeodesicPolygonAreaM2"], AREA_LIMIT_M2)

    def test_diagonal_limit_bbox_is_measured_separately(self) -> None:
        measured = measure_bbox(self.fixtures["diagonal_limit_bbox"])
        reference = self.references["bbox_references"]["diagonal_limit_bbox"]
        self.assertGreaterEqual(measured["diagonalRatio"], 0.95)
        self.assertLessEqual(measured["diagonalRatio"], 0.99)
        self.assertLessEqual(measured["ellipsoidalGeodesicPolygonAreaM2"], AREA_LIMIT_M2)
        self.assertAlmostEqual(
            measured["maxCornerDiagonalM"],
            reference["max_corner_diagonal_m"],
            delta=0.001,
        )

    def test_native_radius_fixture_is_admitted_and_is_a_scaled_road(self) -> None:
        native = self.fixtures["native_radius_fixture"]
        admission = self.fixtures[native["bbox_id"]]
        self.assertEqual(origin_from_bbox(admission), self.origin)
        admission_polygon = box(
            admission["west"],
            admission["south"],
            admission["east"],
            admission["north"],
        )
        for endpoint in (native["backward"], native["forward"]):
            self.assertTrue(
                admission_polygon.covers(Point(endpoint["lon_deg"], endpoint["lat_deg"]))
            )
        backward = self.projection.project(
            native["backward"]["lon_deg"], native["backward"]["lat_deg"]
        )
        forward = self.projection.project(
            native["forward"]["lon_deg"], native["forward"]["lat_deg"]
        )
        scale = native["scale"]
        backward_scene = scale_enh(backward.e, backward.n, scale)
        forward_scene = scale_enh(forward.e, forward.n, scale)
        radius = math.hypot(forward_scene["e"], forward_scene["n"])
        road_length = math.hypot(
            forward_scene["e"] - backward_scene["e"],
            forward_scene["n"] - backward_scene["n"],
        )
        self.assertGreaterEqual(radius / NATIVE_RADIUS_LIMIT_M, 0.95)
        self.assertLessEqual(radius / NATIVE_RADIUS_LIMIT_M, 0.99)
        self.assertAlmostEqual(radius, native["expected_scaled_radius_m"], delta=0.001)
        self.assertAlmostEqual(
            road_length, native["expected_scaled_road_length_m"], delta=0.001
        )

    def test_crossing_segment_is_clipped_before_projection_with_provenance(self) -> None:
        segment = self.fixtures["crossing_segment"]
        bbox_fixture = self.fixtures[segment["bbox_id"]]
        clipped = clip_crossing_segment(segment, bbox_fixture)
        reference = self.references["crossing_clip_reference"]
        self.assertEqual(clipped["stage"], "WGS84 longitude/latitude before projection")
        for endpoint_name, reference_name, parameter_name in (
            ("start", "clipped_start", "t_enter"),
            ("end", "clipped_end", "t_exit"),
        ):
            endpoint = clipped[endpoint_name]
            expected = reference[reference_name]
            self.assertTrue(endpoint["synthetic"])
            self.assertEqual(endpoint["sourceWayId"], segment["source_way_id"])
            self.assertEqual(
                endpoint["sourceSegmentIndex"], segment["source_segment_index"]
            )
            self.assertAlmostEqual(endpoint["sourceT"], reference[parameter_name], delta=1e-12)
            self.assertAlmostEqual(endpoint["lonDeg"], expected["lon_deg"], delta=1e-12)
            self.assertAlmostEqual(endpoint["latDeg"], expected["lat_deg"], delta=1e-12)

    def test_projected_crossing_is_densified_within_budget(self) -> None:
        segment = self.fixtures["crossing_segment"]
        clipped = clip_crossing_segment(segment, self.fixtures[segment["bbox_id"]])
        densified = densify_projected_segment(segment, clipped, self.projection)
        self.assertGreater(len(densified), 2)
        coarse = measure_densification_deviation(
            segment, densified, self.projection, samples_per_interval=512
        )
        fine = measure_densification_deviation(
            segment, densified, self.projection, samples_per_interval=2048
        )
        endpoint_chord = [densified[0], densified[-1]]
        endpoint_chord_deviation = measure_densification_deviation(
            segment,
            endpoint_chord,
            self.projection,
            samples_per_interval=2048,
        )
        self.assertGreater(endpoint_chord_deviation, DISCRETIZATION_LIMIT_M)
        self.assertLessEqual(
            fine + abs(fine - coarse),
            DISCRETIZATION_LIMIT_M,
        )

    def test_neutral_json_contract_has_exact_keys_and_six_independent_maps(self) -> None:
        model = build_neutral_model(self.fixtures, self.projection)
        self.assertEqual(
            list(model), ["schemaVersion", "poc", "coordinateSystem", "maps"]
        )
        self.assertEqual(
            model["coordinateSystem"],
            {
                "sourceAxes": ["E", "N", "H"],
                "unit": "scene metre",
                "candidateMapping": {"x": "E", "y": "H", "z": "-N"},
            },
        )
        self.assertEqual(
            [item["id"] for item in model["maps"]],
            [
                "east-scale-1",
                "north-scale-1",
                "oblique-scale-1",
                "oblique-scale-0.1",
                "tiny-offsets",
                "near-native-radius",
            ],
        )
        self.assertEqual([len(item["roads"]) for item in model["maps"]], [1, 1, 1, 1, 3, 1])
        for map_fixture in model["maps"]:
            self.assertEqual(list(map_fixture), ["id", "roads"])
            for road in map_fixture["roads"]:
                self.assertEqual(
                    list(road),
                    ["id", "sourceFixtureId", "scale", "backward", "forward"],
                )
                self.assertEqual(list(road["backward"]), ["e", "n", "h"])
                self.assertEqual(list(road["forward"]), ["e", "n", "h"])

    def test_neutral_scale_one_vs_tenth_is_not_double_scaled(self) -> None:
        model = build_neutral_model(self.fixtures, self.projection)
        maps = {item["id"]: item for item in model["maps"]}
        full = maps["oblique-scale-1"]["roads"][0]["forward"]
        tenth = maps["oblique-scale-0.1"]["roads"][0]["forward"]
        self.assertAlmostEqual(tenth["e"] / full["e"], 0.1, delta=1e-15)
        self.assertAlmostEqual(tenth["n"] / full["n"], 0.1, delta=1e-15)

    def test_deterministic_serialization_and_nonfinite_rejection(self) -> None:
        model = build_neutral_model(self.fixtures, self.projection)
        first = deterministic_json(model)
        second = deterministic_json(build_neutral_model(self.fixtures, self.projection))
        self.assertEqual(first, second)
        self.assertNotIn("NormalScale", first)
        self.assertNotIn("CityScale", first)
        self.assertNotIn("NaN", first)
        self.assertNotIn("Infinity", first)
        with self.assertRaises(ValueError):
            deterministic_json({"bad": float("nan")})
        with self.assertRaises(ValueError):
            validate_finite_json({"nested": [0.0, float("inf")]})
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bad.json"
            path.write_text('{"bad": NaN}', encoding="utf-8")
            with self.assertRaises(ValueError):
                load_json(path)

    def test_python_validation_manifest_passes_every_automatic_check(self) -> None:
        neutral = build_neutral_model(self.fixtures, self.projection)
        validation = build_python_validation(
            self.fixtures,
            self.references,
            self.fixture_hashes,
            self.projection,
            neutral,
        )
        self.assertEqual(validation["automaticStatus"], "PASS")
        self.assertTrue(all(check["passed"] for check in validation["checks"]))
        self.assertLessEqual(
            validation["maximumErrors"]["geographicRoundTripM"], 0.001
        )
        self.assertLessEqual(
            validation["maximumErrors"]["projectedDiscretizationM"], 0.01
        )
        self.assertIn("hypothesis", validation["nativeCandidateMappingStatus"])


if __name__ == "__main__":
    unittest.main()
