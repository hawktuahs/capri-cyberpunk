"""Retain wheel license files and package provenance in the redistributable."""
import importlib.metadata
import json
import shutil
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "voice" / "licenses"
packages = []
for package in importlib.metadata.distributions():
    name = package.metadata["Name"]
    packages.append({"name": name, "version": package.version,
                     "home": package.metadata.get("Home-page"),
                     "project_urls": package.metadata.get_all("Project-URL") or []})
    for file in package.files or []:
        if any(term in str(file).lower() for term in ("license", "copying", "notice")):
            source = Path(package.locate_file(file))
            if source.is_file():
                destination = root / name / str(file).replace("../", "")
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, destination)
(root / "packages.json").write_text(json.dumps(packages, indent=2), encoding="utf-8")
python_license = Path(sys.base_prefix) / "LICENSE.txt"
if python_license.is_file():
    shutil.copy2(python_license, root / "Python-LICENSE.txt")
