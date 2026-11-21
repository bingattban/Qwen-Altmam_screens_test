#!/usr/bin/env python3
"""Compatibility entry point: delegates to the current nine-shard CI contract.

Kept only so older manual invocations still fail closed against the CURRENT
1,162-test nine-way inventory, never against an obsolete six-way shard map.
"""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).with_name("test-nine-shard-coverage-contract.py")), run_name="__main__")
