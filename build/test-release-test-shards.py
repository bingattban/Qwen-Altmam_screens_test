#!/usr/bin/env python3
"""Fail-closed regression tests for all nine independent Release xUnit shards."""
import importlib.util
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

MODULE = Path(__file__).with_name("verify-release-test-shards.py")
NS = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"


def write_report(root: Path, shard: str, names: list[str], outcome="Passed",
                 summary=None, filename="full-suite.trx"):
    folder = root / ("test-results-windows-" + shard)
    folder.mkdir(parents=True, exist_ok=True)
    test_run = ET.Element("TestRun", xmlns=NS)
    results = ET.SubElement(test_run, "Results")
    for idx, name in enumerate(names):
        ET.SubElement(results, "UnitTestResult", {
            "testId": shard + "-" + str(idx), "testName": name, "outcome": outcome
        })
    s = ET.SubElement(test_run, "ResultSummary", outcome="Completed")
    ET.SubElement(s, "Counters", {
        "total": str(len(names) if summary is None else summary),
        "executed": str(len(names)), "passed": str(len(names) if outcome == "Passed" else 0),
        "failed": str(len(names) if outcome == "Failed" else 0)
    })
    ET.ElementTree(test_run).write(folder / filename, encoding="utf-8")


def baseline(root):
    write_report(root, "r01-config", ["AlTamam.Tests.Bridge.R01NativeConfigurationLifecycleTests.Real"])
    write_report(root, "r01-excel", ["AlTamam.Tests.Bridge.R01NativeExcelLifecycleTests.Real"])
    write_report(root, "r01-ops", ["AlTamam.Tests.Bridge.R01NativeOperationalLifecycleTests.Real"])
    write_report(root, "r01-other", ["AlTamam.Tests.Bridge.R01NativeRecoveryLifecycleTests.Real"])
    write_report(root, "bridge-heavy", ["AlTamam.Tests.Bridge.NativeRestoreActivationCoordinatorTests.Restore"])
    write_report(root, "bridge-light", ["AlTamam.Tests.Bridge.AttendanceCommitIntegrationTests.Commit"])
    write_report(root, "performance", ["AlTamam.Tests.Performance.Phase15TenThousandMultiYearDiagnostics.Measured"])
    write_report(root, "security", ["AlTamam.Tests.Security.BridgeFuzzPayloadTests.Fuzz"])
    write_report(root, "other", ["AlTamam.Tests.Persistence.OfflineRestoreSwitchTests.Safe"])
    write_report(root, "other", ["Phase02.Cipher"], filename="phase02-integration.trx")
    write_report(root, "other", ["Phase03.Roles"], filename="phase03-unit.trx")


class ShardContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location("shard_verify", MODULE)
        cls.mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.mod)

    def run_fixture(self, mutate=None):
        with tempfile.TemporaryDirectory() as path:
            root = Path(path)
            baseline(root)
            if mutate:
                mutate(root)
            return self.mod.validate(
                root, minimums={"r01-config": 1, "r01-excel": 1, "r01-ops": 1, "r01-other": 1, "bridge-heavy": 1, "bridge-light": 1, "performance": 1, "security": 1, "other": 1},
                phase_minimums={"phase02-integration.trx": 1, "phase03-unit.trx": 1}
            )

    def test_all_nine_disjoint_shards_and_independent_suites_pass(self):
        self.assertEqual([], self.run_fixture())

    def test_missing_shard_is_fatal(self):
        self.assertTrue(self.run_fixture(lambda root: (
            root / "test-results-windows-r01-other" / "full-suite.trx").unlink()))

    def test_test_failure_is_fatal(self):
        self.assertTrue(self.run_fixture(lambda root: write_report(
            root, "r01-other", ["AlTamam.Tests.Bridge.R01NativeRecoveryLifecycleTests.Real"], outcome="Failed")))

    def test_wrong_shard_is_fatal(self):
        self.assertTrue(self.run_fixture(lambda root: write_report(
            root, "performance", ["AlTamam.Tests.Bridge.AttendanceCommitIntegrationTests.Commit"])))

    def test_duplicate_execution_is_fatal(self):
        self.assertTrue(self.run_fixture(lambda root: write_report(
            root, "other", ["AlTamam.Tests.Bridge.R01NativeRecoveryLifecycleTests.Real",
                             "AlTamam.Tests.Persistence.OfflineRestoreSwitchTests.Safe"])))

    def test_fake_counters_are_fatal(self):
        self.assertTrue(self.run_fixture(lambda root: write_report(
            root, "bridge-light", ["AlTamam.Tests.Bridge.AttendanceCommitIntegrationTests.Commit"], summary=2)))

    def test_missing_one_native_r01_subshard_is_fatal(self):
        self.assertTrue(self.run_fixture(lambda root: (
            root / "test-results-windows-r01-config" / "full-suite.trx").unlink()))

    def test_native_excel_misplaced_into_other_r01_shard_fails(self):
        self.assertTrue(self.run_fixture(lambda root: write_report(
            root, "r01-other", ["AlTamam.Tests.Bridge.R01NativeExcelLifecycleTests.Real"])))

    def test_missing_new_security_shard_is_fatal(self):
        self.assertTrue(self.run_fixture(lambda root: (
            root / "test-results-windows-security" / "full-suite.trx").unlink()))

    def test_heavy_bridge_case_cannot_run_in_light_shard(self):
        self.assertTrue(self.run_fixture(lambda root: write_report(
            root, "bridge-light",
            ["AlTamam.Tests.Bridge.NativeRestoreActivationCoordinatorTests.Restore"])))

    def test_security_case_cannot_run_in_other_shard(self):
        self.assertTrue(self.run_fixture(lambda root: write_report(
            root, "other", ["AlTamam.Tests.Security.BridgeFuzzPayloadTests.Fuzz"])))

    def test_missing_phase02_safety_suite_is_fatal(self):
        self.assertTrue(self.run_fixture(lambda root: (
            root / "test-results-windows-other" / "phase02-integration.trx").unlink()))

    def test_missing_phase03_safety_suite_is_fatal(self):
        self.assertTrue(self.run_fixture(lambda root: (
            root / "test-results-windows-other" / "phase03-unit.trx").unlink()))

    def test_additional_or_misnamed_result_folder_is_fatal(self):
        self.assertTrue(self.run_fixture(lambda root: (root / "test-results-windows-unexpected").mkdir()))

    def test_below_baseline_count_is_fatal(self):
        with tempfile.TemporaryDirectory() as path:
            root = Path(path)
            baseline(root)
            self.assertTrue(self.mod.validate(
                root, minimums={"r01-config": 2, "r01-excel": 1, "r01-ops": 1, "r01-other": 1, "bridge-heavy": 1, "bridge-light": 1, "performance": 1, "security": 1, "other": 1},
                phase_minimums={"phase02-integration.trx": 1, "phase03-unit.trx": 1}
            ))


if __name__ == "__main__":
    unittest.main()
