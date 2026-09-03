#!/usr/bin/env python3
"""Freeze revised PoC-002 inputs, then run the Python validation stage."""

from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
import math
import os
import platform
import re
import shutil
import subprocess
import sys
import sysconfig
from collections.abc import Callable, Mapping
from datetime import UTC, datetime
from pathlib import Path
from typing import Any


CRITERIA_ID = "poc-002-q256-rerun-v2"
EXPECTED_UV_VERSION = "0.12.7"
EXPECTED_PYTHON_VERSION = "3.14.7"
EXPECTED_PYPROJ_VERSION = "3.7.2"
EXPECTED_SHAPELY_VERSION = "2.1.2"
EXPECTED_DOTNET_SDK_VERSION = "10.0.400"
EXPECTED_DOTNET_RUNTIME_VERSION = "10.0.11"

FROZEN_FILES: tuple[tuple[str, str, str], ...] = (
    (
        "canonical-prd",
        "tasks/prd-osm2ets2-mvp.md",
        "8729f28d18c09b636b027207a619ca5d1db4062eb4f04b749c2f58c663f77f44",
    ),
    (
        "canonical-spike-plan",
        "tasks/spikes-osm2ets2-mvp.md",
        "bdb0b35048c3e2fa3057b000f1054977feb21be4cc54ca55671bf1c1072cdc2c",
    ),
    (
        "revised-rerun-specification",
        "spikes/poc-002-coordinate-geometry/revised-rerun-spec.md",
        "d2d594ea582f91c0f4669d087b845c3b54d098e23341bebe08efa936625d8894",
    ),
    (
        "frozen-geographic-fixtures",
        "spikes/poc-002-coordinate-geometry/fixtures/frozen-fixtures.json",
        "3df7f774af4b7a9e6b420871e0fc9a3115c3673964f22dea405e46a53ff43f4b",
    ),
    (
        "independent-reference-values",
        "spikes/poc-002-coordinate-geometry/fixtures/independent-reference.json",
        "3ed376bcda2f8819cd5dd461f569d641e2ea19787bfc7219a9c9c7b67e166a9c",
    ),
    (
        "independent-reference-generator",
        "spikes/poc-002-coordinate-geometry/reference/freeze_reference.py",
        "17691ebcb230385a5a575d2032b45f4dda422032abe3a2d219bfb4a9ac395517",
    ),
    (
        "dotnet-sdk-selection",
        "spikes/poc-002-coordinate-geometry/csharp/global.json",
        "b54f95ccc7a0a2199f7f2ce71cef88c853e633b5f6d0c492095cd16bbf4d8506",
    ),
    (
        "trucklib-adapter-project",
        "spikes/poc-002-coordinate-geometry/csharp/Poc002Adapter.csproj",
        "3fe17ea473f5a093faf98f2ba96ca8cb4f69c47a18428f9c05810e27c4d0b7c7",
    ),
    (
        "locked-dotnet-dependencies",
        "spikes/poc-002-coordinate-geometry/csharp/packages.lock.json",
        "5f04d1417306f056a1c85b454a62db3e66a5e47d714594b27a28c73aa4d92aa7",
    ),
)

RUN_ID_PATTERN = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,127}\Z")


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _validate_run_id(run_id: str) -> str:
    if RUN_ID_PATTERN.fullmatch(run_id) is None:
        raise ValueError(
            "run ID must contain 1-128 ASCII letters, digits, '.', '_' or '-' "
            "and must start with a letter or digit"
        )
    return run_id


def probe_exact_uv(uv_executable: Path) -> dict[str, Any]:
    """Verify and identify the exact uv binary used to launch the rerun."""

    executable = uv_executable.expanduser().resolve(strict=True)
    if not executable.is_file() or not os.access(executable, os.X_OK):
        raise RuntimeError(f"uv executable is not an executable file: {executable}")
    completed = subprocess.run(
        [str(executable), "--version"],
        check=True,
        capture_output=True,
        text=True,
    )
    output = completed.stdout.strip()
    parts = output.split()
    if len(parts) < 2 or parts[0] != "uv" or parts[1] != EXPECTED_UV_VERSION:
        raise RuntimeError(
            f"expected uv {EXPECTED_UV_VERSION}, got {output or '<empty output>'}"
        )
    return {
        "path": str(executable),
        "sha256": _sha256(executable),
        "version": parts[1],
        "versionOutput": output,
    }


def probe_exact_dotnet(dotnet_executable: Path | None = None) -> dict[str, Any]:
    """Verify the pinned .NET SDK and required shared runtime."""

    selected = (
        str(dotnet_executable)
        if dotnet_executable is not None
        else shutil.which("dotnet")
    )
    if selected is None:
        raise RuntimeError("dotnet executable was not found on PATH")
    executable = Path(selected).expanduser().resolve(strict=True)
    if not executable.is_file() or not os.access(executable, os.X_OK):
        raise RuntimeError(f"dotnet executable is not an executable file: {executable}")
    sdk = subprocess.run(
        [str(executable), "--version"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    if sdk != EXPECTED_DOTNET_SDK_VERSION:
        raise RuntimeError(
            f"expected .NET SDK {EXPECTED_DOTNET_SDK_VERSION}, got {sdk or '<empty output>'}"
        )
    runtimes_output = subprocess.run(
        [str(executable), "--list-runtimes"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    runtime_pattern = re.compile(
        rf"^Microsoft\.NETCore\.App {re.escape(EXPECTED_DOTNET_RUNTIME_VERSION)} \[.+\]$",
        re.MULTILINE,
    )
    runtime_installed = runtime_pattern.search(runtimes_output) is not None
    if not runtime_installed:
        raise RuntimeError(
            "required Microsoft.NETCore.App "
            f"{EXPECTED_DOTNET_RUNTIME_VERSION} is not installed"
        )
    return {
        "path": str(executable),
        "sha256": _sha256(executable),
        "sdkVersion": sdk,
        "requiredRuntime": f"Microsoft.NETCore.App {EXPECTED_DOTNET_RUNTIME_VERSION}",
        "requiredRuntimeInstalled": True,
        "listRuntimesOutput": runtimes_output,
    }


def _preflight_environment(
    uv: Mapping[str, Any],
    dotnet: Mapping[str, Any],
) -> dict[str, Any]:
    def installed_version(distribution: str) -> str | None:
        try:
            return importlib.metadata.version(distribution)
        except importlib.metadata.PackageNotFoundError:
            return None

    standard_gil = bool(getattr(sys, "_is_gil_enabled", lambda: True)())
    return {
        "python": platform.python_version(),
        "pythonImplementation": platform.python_implementation(),
        "pythonExecutable": str(Path(sys.executable).resolve()),
        "pythonBuild": list(platform.python_build()),
        "operatingSystem": platform.platform(),
        "machine": platform.machine(),
        "standardGilEnabled": standard_gil,
        "pyGilDisabledBuildFlag": int(sysconfig.get_config_var("Py_GIL_DISABLED") or 0),
        "floatMantissaBits": sys.float_info.mant_dig,
        "pyprojPackage": installed_version("pyproj"),
        "shapelyPackage": installed_version("shapely"),
        "projNetworkEnvironment": os.environ.get("PROJ_NETWORK"),
        "uv": dict(uv),
        "dotnet": dict(dotnet),
    }


def build_pre_run_manifest(
    experiment_directory: Path,
    run_id: str,
    uv: Mapping[str, Any],
    dotnet: Mapping[str, Any],
    *,
    recorded_at_utc: str | None = None,
    external_blockers: tuple[str, ...] = (),
) -> dict[str, Any]:
    """Build the immutable input record without importing the PoC implementation."""

    checked_run_id = _validate_run_id(run_id)
    repository_root = experiment_directory.resolve().parents[1]
    frozen_files = []
    input_failures = []
    for role, relative_path, expected_sha256 in FROZEN_FILES:
        path = repository_root / relative_path
        actual_sha256 = _sha256(path) if path.is_file() else None
        matched = actual_sha256 == expected_sha256
        frozen_files.append(
            {
                "role": role,
                "path": relative_path,
                "expectedSha256": expected_sha256,
                "actualSha256": actual_sha256,
                "matched": matched,
            }
        )
        if not matched:
            input_failures.append(f"frozen hash mismatch: {relative_path}")

    environment = _preflight_environment(uv, dotnet)
    expected_environment = {
        "python": EXPECTED_PYTHON_VERSION,
        "pythonImplementation": "CPython",
        "standardGilEnabled": True,
        "pyGilDisabledBuildFlag": 0,
        "floatMantissaBits": 53,
        "pyprojPackage": EXPECTED_PYPROJ_VERSION,
        "shapelyPackage": EXPECTED_SHAPELY_VERSION,
        "projNetworkEnvironment": "OFF",
        "uvVersion": EXPECTED_UV_VERSION,
        "dotnetSdkVersion": EXPECTED_DOTNET_SDK_VERSION,
        "dotnetRuntimeInstalled": True,
    }
    actual_environment = {
        key: environment[key]
        for key in (
            "python",
            "pythonImplementation",
            "standardGilEnabled",
            "pyGilDisabledBuildFlag",
            "floatMantissaBits",
            "pyprojPackage",
            "shapelyPackage",
            "projNetworkEnvironment",
        )
    }
    actual_environment["uvVersion"] = environment["uv"].get("version")
    actual_environment["dotnetSdkVersion"] = environment["dotnet"].get("sdkVersion")
    actual_environment["dotnetRuntimeInstalled"] = environment["dotnet"].get(
        "requiredRuntimeInstalled"
    )
    environment_blockers = list(external_blockers)
    for key, expected in expected_environment.items():
        if actual_environment[key] != expected:
            environment_blockers.append(
                f"environment mismatch for {key}: expected {expected!r}, "
                f"got {actual_environment[key]!r}"
            )

    reference_path = experiment_directory / "fixtures" / "independent-reference.json"
    try:
        references = json.loads(reference_path.read_text(encoding="utf-8"))
        reference_method = references["method"]
        distance_semantics = references["distance_semantics"]
        clipping_reference = references["crossing_clip_reference"]["algorithm"]
    except (OSError, KeyError, json.JSONDecodeError) as error:
        input_failures.append(f"independent reference cannot be read: {error}")
        reference_method = {"unavailable": str(error)}
        distance_semantics = {"unavailable": str(error)}
        clipping_reference = f"unavailable: {error}"
    timestamp = recorded_at_utc or datetime.now(UTC).isoformat().replace("+00:00", "Z")
    validation = (
        "BLOCKED"
        if environment_blockers
        else "FAIL"
        if input_failures
        else "PASS"
    )
    return {
        "schemaVersion": 1,
        "criteriaId": CRITERIA_ID,
        "runId": checked_run_id,
        "phase": "input freeze before revised Python implementation execution",
        "recordedAtUtc": timestamp,
        "inputValidation": validation,
        "failures": [*input_failures, *environment_blockers],
        "frozenFiles": frozen_files,
        "environment": environment,
        "expectedCriteria": {
            "geographicRoundTripMaximumM": 0.001,
            "projectedDiscretizationMaximumPreScaleM": 0.01,
            "float64ToFloat32Maximum3dM": 0.001,
            "q256": {
                "gridStepM": 1.0 / 256.0,
                "expectedCode": "trunc_toward_zero(float32_axis * 256f)",
                "comparison": "exact signed Int32 equality for X, Y and Z",
                "perAxisStrictUpperBoundM": 1.0 / 256.0,
                "horizontalXzStrictUpperBoundM": math.sqrt(2.0) / 256.0,
                "threeDimensionalStrictUpperBoundM": math.sqrt(3.0) / 256.0,
            },
            "nativeStraightRoadHausdorffMaximumM": 1.0,
            "nativePlanarRadiusMaximumM": 10_000.0,
        },
        "measurementMethods": {
            "independentReference": reference_method,
            "distanceSemantics": distance_semantics,
            "clippingReference": clipping_reference,
            "densification": (
                "adaptive projected polyline checked with 512 and 2048 "
                "samples per interval; convergence included in the 0.01 m budget"
            ),
            "scaling": "compare E/N radial and component ratios at s=1 and s=0.1 in float64",
            "nativeBoundary": (
                "C# adapter measures mapped float64 to Vector3 float32 separately, "
                "then compares TruckLib 0.5.1 Node.Position signed Q256 codes exactly"
            ),
        },
    }


def create_fresh_run_root(runs_directory: Path, run_id: str) -> Path:
    """Create exactly one new child without accepting path traversal or reuse."""

    checked_run_id = _validate_run_id(run_id)
    root = runs_directory.resolve() / checked_run_id
    root.mkdir(parents=True, exist_ok=False)
    return root


def _write_json(path: Path, value: Any) -> None:
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n",
        encoding="utf-8",
    )


def _bind_revised_python_outputs(
    outputs: Mapping[str, Path],
    manifest_path: Path,
    run_id: str,
) -> dict[str, Path]:
    """Bind revised Python evidence to this run's frozen inputs."""

    required_paths: dict[str, Path] = {}
    for name in ("neutralModel", "pythonValidation"):
        value = outputs.get(name)
        if not isinstance(value, Path) or not value.is_file():
            raise RuntimeError(
                f"revised Python writer did not return an existing {name} file"
            )
        required_paths[name] = value

    validation_path = required_paths["pythonValidation"]
    try:
        validation = json.loads(validation_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise RuntimeError(
            f"revised Python validation cannot be read: {error}"
        ) from error
    if not isinstance(validation, dict):
        raise RuntimeError("revised Python validation root must be a JSON object")

    validation.update(
        {
            "criteriaId": CRITERIA_ID,
            "runId": _validate_run_id(run_id),
            "preRunManifestSha256": _sha256(manifest_path),
            "neutralModelSha256": _sha256(required_paths["neutralModel"]),
        }
    )
    _write_json(validation_path, validation)
    return {**outputs, **required_paths}


def execute_revised_python_stage(
    experiment_directory: Path,
    run_id: str,
    uv_executable: Path,
    *,
    dotnet_executable: Path | None = None,
    runs_directory: Path | None = None,
    write_outputs: Callable[[Path, Path], dict[str, Path]] | None = None,
    recorded_at_utc: str | None = None,
) -> dict[str, Path]:
    """Create a fresh run, freeze its inputs, then call the Python UUT."""

    base = runs_directory or experiment_directory / "output" / "revised-rerun"
    run_root = create_fresh_run_root(base, run_id)
    external_blockers = []
    try:
        uv = probe_exact_uv(uv_executable)
    except (OSError, RuntimeError, subprocess.SubprocessError) as error:
        uv = {
            "path": str(uv_executable),
            "version": None,
            "error": str(error),
        }
        external_blockers.append(f"uv preflight failed: {error}")
    try:
        dotnet = probe_exact_dotnet(dotnet_executable)
    except (OSError, RuntimeError, subprocess.SubprocessError) as error:
        dotnet = {
            "path": str(dotnet_executable) if dotnet_executable else None,
            "sdkVersion": None,
            "requiredRuntimeInstalled": False,
            "error": str(error),
        }
        external_blockers.append(f"dotnet preflight failed: {error}")
    manifest = build_pre_run_manifest(
        experiment_directory,
        run_id,
        uv,
        dotnet,
        recorded_at_utc=recorded_at_utc,
        external_blockers=tuple(external_blockers),
    )
    manifest_path = run_root / "pre-run-input-manifest.json"
    _write_json(manifest_path, manifest)
    if manifest["inputValidation"] != "PASS":
        raise RuntimeError(
            "revised PoC-002 pre-run validation failed; implementation was not executed"
        )

    if write_outputs is None:
        # Deliberately import only after the immutable pre-run record is on disk.
        from poc002_geometry import write_revised_automatic_outputs

        write_outputs = write_revised_automatic_outputs
    outputs = write_outputs(experiment_directory, run_root / "python")
    outputs = _bind_revised_python_outputs(
        outputs,
        manifest_path,
        manifest["runId"],
    )
    return {"preRunManifest": manifest_path, **outputs}


def _parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Execute only the Python stage of the frozen PoC-002 Q256 rerun."
    )
    parser.add_argument("--run-id", required=True)
    parser.add_argument(
        "--uv-executable",
        required=True,
        type=Path,
        help="Path to the exact uv 0.12.7 executable used for this run.",
    )
    parser.add_argument(
        "--dotnet-executable",
        type=Path,
        help="Optional explicit dotnet host; defaults to the host selected on PATH.",
    )
    return parser.parse_args()


def main() -> None:
    arguments = _parse_arguments()
    experiment_directory = Path(__file__).resolve().parent
    outputs = execute_revised_python_stage(
        experiment_directory,
        arguments.run_id,
        arguments.uv_executable,
        dotnet_executable=arguments.dotnet_executable,
    )
    for name, path in outputs.items():
        print(f"{name}: {path.relative_to(experiment_directory)}")


if __name__ == "__main__":
    main()
