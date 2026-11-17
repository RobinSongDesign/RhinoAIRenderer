"""Keeps the product version identical everywhere it is written down.

Usage:
  python3 tools/scripts/set_version.py 2.1.0          # write the version
  python3 tools/scripts/set_version.py v2.1.0-beta.1  # a leading "v" (git tag) is accepted
  python3 tools/scripts/set_version.py --check        # fail when the files disagree

Files: Directory.Build.props (<Version>), src/Chameleon.UI/package.json ("version"),
packaging/manifest.yml (version:).
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROPS = ROOT / "Directory.Build.props"
PACKAGE_JSON = ROOT / "src" / "Chameleon.UI" / "package.json"
MANIFEST = ROOT / "packaging" / "manifest.yml"

SEMVER = re.compile(r"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")
PROPS_VERSION = re.compile(r"<Version>([^<]*)</Version>")
MANIFEST_VERSION = re.compile(r"^version:\s*(\S+)\s*$", re.M)


def read_versions() -> dict:
    return {
        PROPS.name: PROPS_VERSION.search(PROPS.read_text(encoding="utf-8")).group(1),
        "package.json": json.loads(PACKAGE_JSON.read_text(encoding="utf-8"))["version"],
        MANIFEST.name: MANIFEST_VERSION.search(MANIFEST.read_text(encoding="utf-8")).group(1),
    }


def write_version(version: str) -> None:
    props = PROPS.read_text(encoding="utf-8")
    PROPS.write_text(PROPS_VERSION.sub(f"<Version>{version}</Version>", props, count=1), encoding="utf-8")

    package = PACKAGE_JSON.read_text(encoding="utf-8")
    # Edit the one field textually so the file's formatting is kept.
    PACKAGE_JSON.write_text(re.sub(r'("version":\s*")[^"]*(")', rf"\g<1>{version}\g<2>", package, count=1), encoding="utf-8")

    manifest = MANIFEST.read_text(encoding="utf-8")
    MANIFEST.write_text(MANIFEST_VERSION.sub(f"version: {version}", manifest, count=1), encoding="utf-8")


def main(args: list) -> int:
    if args == ["--check"]:
        versions = read_versions()
        if len(set(versions.values())) != 1:
            print("Version mismatch: " + ", ".join(f"{k}={v}" for k, v in versions.items()))
            return 1
        print("Version " + next(iter(versions.values())) + " is consistent.")
        return 0

    if len(args) != 1:
        print(__doc__)
        return 2

    version = args[0][1:] if args[0].startswith("v") else args[0]
    if not SEMVER.match(version):
        print(f"'{args[0]}' is not a version like 2.1.0 or 2.1.0-beta.1.")
        return 2

    write_version(version)
    print(f"Version set to {version}.")
    return main(["--check"])


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
