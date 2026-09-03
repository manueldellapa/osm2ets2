from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from run_revised_automatic import (
    CRITERIA_ID,
    EXPECTED_UV_VERSION,
    FROZEN_FILES,
    build_pre_run_manifest,
    create_fresh_run_root,
    execute_revised_python_stage,
    probe_exact_dotnet,
    probe_exact_uv,
)
from poc002_geometry import write_revised_automatic_outputs
from poc002_geometry import (
    LocalAeqd,
    build_neutral_model,
    build_revised_neutral_model,
    deterministic_json,
    load_json,
    origin_from_bbox,
)


EXPERIMENT_DIR = Path(__file__).resolve().parents[1]
FAKE_UV = {
    "path": "/frozen/tools/uv",
    "sha256": "0" * 64,
    "version": EXPECTED_UV_VERSION,
    "versionOutput": f"uv {EXPECTED_UV_VERSION}",
}
FAKE_DOTNET = {
    "path": "/frozen/tools/dotnet",
    "sha256": "1" * 64,
    "sdkVersion": "10.0.400",
    "requiredRuntime": "Microsoft.NETCore.App 10.0.11",
    "requiredRuntimeInstalled": True,
    "listRuntimesOutput": "Microsoft.NETCore.App 10.0.11 [/frozen/dotnet]",
}


class RevisedRunnerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        fixtures = load_json(EXPERIMENT_DIR / "fixtures" / "frozen-fixtures.json")
        projection = LocalAeqd(origin_from_bbox(fixtures["control_bbox"]))
        cls.fixtures = fixtures
        cls.projection = projection

    def test_historical_v1_neutral_json_remains_byte_exact(self) -> None:
        serialized = deterministic_json(
            build_neutral_model(self.fixtures, self.projection)
        ).encode("utf-8")
        self.assertEqual(
            hashlib.sha256(serialized).hexdigest(),
            "169c6b77226ca9d3d5d6f79a25b10d70b76ddb2d6613248d857ac33027c0e33e",
        )

    def test_revised_neutral_json_contains_only_the_enh_coordinate_contract(self) -> None:
        model = build_revised_neutral_model(self.fixtures, self.projection)
        self.assertEqual(model["schemaVersion"], 2)
        self.assertEqual(
            model["coordinateSystem"],
            {"axes": ["E", "N", "H"], "unit": "scene metre"},
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
        all_keys: set[str] = set()

        def collect_keys(value: object) -> None:
            if isinstance(value, dict):
                all_keys.update(value)
                for child in value.values():
                    collect_keys(child)
            elif isinstance(value, list):
                for child in value:
                    collect_keys(child)

        collect_keys(model)
        self.assertTrue(
            {"candidateMapping", "x", "y", "z", "expectedQ"}.isdisjoint(all_keys)
        )
        serialized = deterministic_json(model)
        self.assertNotIn("TruckLib", serialized)
        self.assertNotIn("Q256", serialized)

    def test_pre_run_manifest_has_exact_frozen_hashes_environment_and_methods(self) -> None:
        with patch.dict("os.environ", {"PROJ_NETWORK": "OFF"}):
            manifest = build_pre_run_manifest(
                EXPERIMENT_DIR,
                "poc-002-q256-rerun-v2-test",
                FAKE_UV,
                FAKE_DOTNET,
                recorded_at_utc="2026-09-02T00:00:00Z",
            )

        self.assertEqual(manifest["criteriaId"], CRITERIA_ID)
        self.assertEqual(manifest["inputValidation"], "PASS")
        self.assertEqual(manifest["failures"], [])
        self.assertEqual(len(manifest["frozenFiles"]), len(FROZEN_FILES))
        for item, (_, expected_path, expected_sha256) in zip(
            manifest["frozenFiles"], FROZEN_FILES, strict=True
        ):
            self.assertEqual(item["path"], expected_path)
            self.assertEqual(item["expectedSha256"], expected_sha256)
            self.assertEqual(item["actualSha256"], expected_sha256)
            self.assertTrue(item["matched"])
        self.assertEqual(manifest["environment"]["uv"]["version"], "0.12.7")
        self.assertEqual(manifest["environment"]["dotnet"]["sdkVersion"], "10.0.400")
        self.assertEqual(
            manifest["expectedCriteria"]["q256"]["expectedCode"],
            "trunc_toward_zero(float32_axis * 256f)",
        )
        self.assertIn("independentReference", manifest["measurementMethods"])

    def test_fresh_run_root_rejects_any_existing_directory(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            runs = Path(directory)
            created = create_fresh_run_root(runs, "run-a")
            self.assertTrue(created.is_dir())
            with self.assertRaises(FileExistsError):
                create_fresh_run_root(runs, "run-a")
            with self.assertRaises(ValueError):
                create_fresh_run_root(runs, "../escape")

    def test_revised_python_writer_rejects_existing_output_before_validation(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "python"
            output.mkdir()
            sentinel = output / "historical.json"
            sentinel.write_text("preserve me\n", encoding="utf-8")

            with self.assertRaises(FileExistsError):
                write_revised_automatic_outputs(EXPERIMENT_DIR, output)

            self.assertEqual(sentinel.read_text(encoding="utf-8"), "preserve me\n")

    def test_revised_python_writer_selects_only_the_v2_neutral_builder(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "python"
            with patch(
                "poc002_geometry._write_automatic_outputs_to",
                return_value={},
            ) as write_outputs:
                write_revised_automatic_outputs(EXPERIMENT_DIR, output)

            self.assertTrue(output.is_dir())
            write_outputs.assert_called_once_with(
                EXPERIMENT_DIR,
                output,
                neutral_model_builder=build_revised_neutral_model,
            )

    def test_pre_run_manifest_exists_before_output_writer_is_called(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            runs = Path(directory)

            def fake_writer(experiment: Path, output: Path) -> dict[str, Path]:
                self.assertEqual(experiment, EXPERIMENT_DIR)
                manifest_path = output.parent / "pre-run-input-manifest.json"
                self.assertTrue(manifest_path.is_file())
                manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
                self.assertEqual(manifest["inputValidation"], "PASS")
                output.mkdir()
                neutral = output / "neutral-model.json"
                neutral.write_text('{"schemaVersion": 2}\n', encoding="utf-8")
                validation = output / "python-validation.json"
                validation.write_text(
                    json.dumps(
                        {
                            "automaticStatus": "PASS",
                            "criteriaId": "untrusted-writer-value",
                            "runId": "untrusted-writer-value",
                            "preRunManifestSha256": "2" * 64,
                            "neutralModelSha256": "3" * 64,
                        }
                    )
                    + "\n",
                    encoding="utf-8",
                )
                generated = output / "sentinel.json"
                generated.write_text("{}\n", encoding="utf-8")
                return {
                    "neutralModel": neutral,
                    "pythonValidation": validation,
                    "sentinel": generated,
                }

            with (
                patch(
                    "run_revised_automatic.probe_exact_uv",
                    return_value=FAKE_UV,
                ),
                patch(
                    "run_revised_automatic.probe_exact_dotnet",
                    return_value=FAKE_DOTNET,
                ),
                patch.dict("os.environ", {"PROJ_NETWORK": "OFF"}),
            ):
                outputs = execute_revised_python_stage(
                    EXPERIMENT_DIR,
                    "ordered-run",
                    Path("/not-used/uv"),
                    runs_directory=runs,
                    write_outputs=fake_writer,
                    recorded_at_utc="2026-09-02T00:00:00Z",
                )

            self.assertTrue(outputs["preRunManifest"].is_file())
            self.assertTrue(outputs["sentinel"].is_file())
            validation = json.loads(
                outputs["pythonValidation"].read_text(encoding="utf-8")
            )
            self.assertEqual(validation["criteriaId"], CRITERIA_ID)
            self.assertEqual(validation["runId"], "ordered-run")
            self.assertEqual(
                validation["preRunManifestSha256"],
                hashlib.sha256(outputs["preRunManifest"].read_bytes()).hexdigest(),
            )
            self.assertEqual(
                validation["neutralModelSha256"],
                hashlib.sha256(outputs["neutralModel"].read_bytes()).hexdigest(),
            )

    def test_python_stage_rejects_writer_without_required_evidence_paths(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            runs = Path(directory)

            def incomplete_writer(
                _experiment: Path, output: Path
            ) -> dict[str, Path]:
                output.mkdir()
                neutral = output / "neutral-model.json"
                neutral.write_text('{"schemaVersion": 2}\n', encoding="utf-8")
                return {"neutralModel": neutral}

            with (
                patch(
                    "run_revised_automatic.probe_exact_uv",
                    return_value=FAKE_UV,
                ),
                patch(
                    "run_revised_automatic.probe_exact_dotnet",
                    return_value=FAKE_DOTNET,
                ),
                patch.dict("os.environ", {"PROJ_NETWORK": "OFF"}),
            ):
                with self.assertRaisesRegex(
                    RuntimeError,
                    "existing pythonValidation file",
                ):
                    execute_revised_python_stage(
                        EXPERIMENT_DIR,
                        "missing-evidence-run",
                        Path("/not-used/uv"),
                        runs_directory=runs,
                        write_outputs=incomplete_writer,
                        recorded_at_utc="2026-09-02T00:00:00Z",
                    )

    def test_uv_probe_rejects_non_0127_version(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            executable = Path(directory) / "uv"
            executable.write_text("placeholder", encoding="utf-8")
            executable.chmod(0o700)
            completed = type(
                "Completed",
                (),
                {"stdout": "uv 0.11.28 (test)\n"},
            )()
            with patch("run_revised_automatic.subprocess.run", return_value=completed):
                with self.assertRaisesRegex(RuntimeError, "expected uv 0.12.7"):
                    probe_exact_uv(executable)

    def test_dotnet_probe_requires_sdk_and_runtime_baseline(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            executable = Path(directory) / "dotnet"
            executable.write_text("placeholder", encoding="utf-8")
            executable.chmod(0o700)
            sdk = type("Completed", (), {"stdout": "10.0.400\n"})()
            runtimes = type(
                "Completed",
                (),
                {
                    "stdout": (
                        "Microsoft.AspNetCore.App 10.0.11 [/frozen/dotnet]\n"
                        "Microsoft.NETCore.App 10.0.11 [/frozen/dotnet]\n"
                    )
                },
            )()
            with patch(
                "run_revised_automatic.subprocess.run",
                side_effect=[sdk, runtimes],
            ):
                observed = probe_exact_dotnet(executable)

            self.assertEqual(observed["sdkVersion"], "10.0.400")
            self.assertTrue(observed["requiredRuntimeInstalled"])

    def test_environment_failure_writes_blocked_manifest_without_calling_uut(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            runs = Path(directory)
            called = False

            def forbidden_writer(_experiment: Path, _output: Path) -> dict[str, Path]:
                nonlocal called
                called = True
                raise AssertionError("UUT must not run after a blocked preflight")

            with (
                patch(
                    "run_revised_automatic.probe_exact_uv",
                    side_effect=RuntimeError("wrong uv"),
                ),
                patch(
                    "run_revised_automatic.probe_exact_dotnet",
                    return_value=FAKE_DOTNET,
                ),
                patch.dict("os.environ", {"PROJ_NETWORK": "OFF"}),
            ):
                with self.assertRaisesRegex(RuntimeError, "pre-run validation failed"):
                    execute_revised_python_stage(
                        EXPERIMENT_DIR,
                        "blocked-run",
                        Path("/wrong/uv"),
                        runs_directory=runs,
                        write_outputs=forbidden_writer,
                        recorded_at_utc="2026-09-03T00:00:00Z",
                    )

            self.assertFalse(called)
            manifest_path = runs / "blocked-run" / "pre-run-input-manifest.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            self.assertEqual(manifest["inputValidation"], "BLOCKED")
            self.assertIn("uv preflight failed", manifest["failures"][0])


if __name__ == "__main__":
    unittest.main()
