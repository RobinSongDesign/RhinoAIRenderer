"""Lays out the Yak package folder for a multi-targeted Rhino 8 plug-in.

Usage: python3 tools/scripts/assemble_package.py <plugin bin/Release dir> <output dir>

Result:
  <output>/manifest.yml          (copied from packaging/)
  <output>/icon.png
  <output>/net7.0/Chameleon.rhp  + dependencies (Rhino 8/9, Windows and macOS)
  <output>/net48/Chameleon.rhp   + dependencies (Rhino 8 on Windows with /netfx)

Debug symbols are left out. Run `yak build --platform any` inside <output> afterwards.
"""
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FRAMEWORKS = ("net7.0", "net48")
EXCLUDED_SUFFIXES = (".pdb",)


def assemble(bin_dir: Path, out_dir: Path) -> list:
    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True)

    for name in ("manifest.yml", "icon.png"):
        shutil.copy2(ROOT / "packaging" / name, out_dir / name)

    written = []
    for framework in FRAMEWORKS:
        source = bin_dir / framework
        if not (source / "Chameleon.rhp").exists():
            raise SystemExit(f"Missing {source / 'Chameleon.rhp'}: build src/Chameleon.Rhino first.")
        target = out_dir / framework
        target.mkdir()
        for file in sorted(source.iterdir()):
            if file.is_file() and not file.name.endswith(EXCLUDED_SUFFIXES):
                shutil.copy2(file, target / file.name)
                written.append(f"{framework}/{file.name}")
    return written


def main(args: list) -> int:
    if len(args) != 2:
        print(__doc__)
        return 2
    for path in assemble(Path(args[0]), Path(args[1])):
        print(path)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
