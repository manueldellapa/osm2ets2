#!/usr/bin/env python3
"""Generate the deterministic PoC-002 Python automatic-run artifacts."""

from pathlib import Path

from poc002_geometry import write_automatic_outputs


def main() -> None:
    experiment_directory = Path(__file__).resolve().parent
    outputs = write_automatic_outputs(experiment_directory)
    for name, path in outputs.items():
        print(f"{name}: {path.relative_to(experiment_directory)}")


if __name__ == "__main__":
    main()
