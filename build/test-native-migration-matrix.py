#!/usr/bin/env python3
"""Fail closed on route inventory drift or false claims of native parity."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
canonical = json.loads((ROOT / "src/AlTamam.WebUI/screen-manifest.json").read_text(encoding="utf-8"))
matrix = json.loads((ROOT / "docs/native-wpf-migration/SCREEN_MIGRATION_MATRIX.json").read_text(encoding="utf-8"))
assert canonical["screenCount"] == len(canonical["screens"]) == 69, "Original manifest route count changed"
assert matrix["canonicalRouteCount"] == 69, "Native migration matrix omitted routes"
assert len(matrix["routes"]) == 69, "Native migration matrix incomplete"
orig = {(r["screenId"], r["routeKey"], r["localAssetPath"]) for r in canonical["screens"]}
native = {(r["screenId"], r["routeKey"], r["legacyAssetPath"]) for r in matrix["routes"]}
assert orig == native, "Native migration route identities differ from canonical manifest"
assert all(r["migrationState"] == "not_migrated" and not r["functionalParityVerified"]
           for r in matrix["routes"]), "Pilot must never claim completed native functional parity"
print("Native migration matrix PASS: 69 canonical routes tracked, 0 falsely claimed migrated")
