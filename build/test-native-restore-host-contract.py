#!/usr/bin/env python3
"""Supplementary static boundaries for the real native-WPF restore entry.
Native SQLCipher integration tests remain mandatory; static evidence alone
never proves a live restore, successful power-loss recovery or physical UAT.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / "src/AlTamam.DesktopHost"
APP = ROOT / "src/AlTamam.Application/Bridge"

class NativeRestoreHostContract(unittest.TestCase):
    def test_restore_button_is_native_and_reauth_is_a_password_box(self):
        xaml = (HOST / "MainWindow.xaml").read_text(encoding="utf-8")
        dialog = (HOST / "Bridge/NativeRestoreConfirmationDialog.cs").read_text(encoding="utf-8")
        host = (HOST / "MainWindow.xaml.cs").read_text(encoding="utf-8")
        self.assertIn('Click="OnNativeRestoreMenuClick"', xaml)
        self.assertIn("PasswordBox", dialog)
        self.assertIn("ShowDialog()", dialog)
        self.assertIn("OnNativeRestoreMenuClick", host)
        self.assertIn("NativeRestoreActivationCoordinator", host)

    def test_native_host_quiesces_bridge_and_all_three_schedulers(self):
        host = (HOST / "MainWindow.xaml.cs").read_text(encoding="utf-8")
        for needle in ("StopReceivingAndDrainAsync",
                       "if (_scheduler is not null)",
                       "if (_backupScheduler is not null)",
                       "if (_autoMonthlyExcelScheduler is not null)",
                       "_scheduler = null;", "_backupScheduler = null;",
                       "_autoMonthlyExcelScheduler = null;",
                       "await scheduled.DisposeAsync()",
                       "_shutdownGate.StopAndDrainAsync",
                       "_restoreQuiescing", "TryActivateAsync"):
            self.assertIn(needle, host)
        gateway = (HOST / "Bridge/DesktopBridgeGateway.cs").read_text(encoding="utf-8")
        self.assertIn("CurrentGeneralAdministratorAsync", gateway)
        self.assertIn("ResolveSessionAsync", gateway)

    def test_untrusted_bridge_cannot_directly_activate_restore(self):
        catalog = (APP / "BridgeHandlerCatalog.cs").read_text(encoding="utf-8")
        self.assertIn('"RestoreDatabaseAtomically" =>', catalog)
        self.assertIn("استخدم قائمة «استعادة نسخة احتياطية بأمان»", catalog)
        self.assertNotIn("NativeRestoreActivationCoordinator", catalog)
        for script in (ROOT / "src/AlTamam.WebUI/assets/js").glob("*.js"):
            self.assertNotIn('.invoke("RestoreDatabaseAtomically"', script.read_text(encoding="utf-8"))

    def test_native_backup_upgrade_is_separate_from_restore_and_requires_review(self):
        native = ROOT / "src/AlTamam.NativeDesktop"
        view = (native / "Views/NativeManualBackupView.xaml").read_text(encoding="utf-8")
        handler = (native / "Views/NativeManualBackupView.xaml.cs").read_text(encoding="utf-8")
        dialog = (native / "Views/NativeBackupUpgradeDialog.cs").read_text(encoding="utf-8")
        host = (native / "MainWindow.xaml.cs").read_text(encoding="utf-8")
        backend = (native / "Auth/NativeSqlCipherAuthenticationBackend.cs").read_text(encoding="utf-8")
        review = (ROOT / "src/AlTamam.NativePresentation/NativeBackupInspectionReview.cs").read_text(encoding="utf-8")
        self.assertIn('Click="OnBackupUpgradeClick"', view)
        self.assertIn('Inspection.CanRequestUpgrade', view)
        self.assertIn('host.RequestBackupUpgradeAsync(settings)', handler)
        self.assertIn('PasswordBox', dialog)
        self.assertIn('password.Clear()', dialog)
        self.assertIn('window.ShowDialog()', dialog)
        self.assertIn('SchemaVersion==CurrentSchemaVersion-1', review)
        self.assertIn('ReferenceEquals(Evidence,intent.Evidence)', review)
        method = host.split("internal async Task RequestBackupUpgradeAsync",1)[1].split("internal async Task RequestRecoveryExportAsync",1)[0]
        for needle in ("GetUpgradeIntent()", "MatchesUpgradeIntent(intent)",
                       "_recoveryExportInProgress=true", "PrepareBackupUpgradeAsync",
                       "settings.Inspection.ForceClear()", "VerifyCurrentSessionAsync"):
            self.assertIn(needle,method)
        self.assertNotIn("TryRestoreAsync",method)
        self.assertNotIn("ActivateOfflineAsync",method)
        self.assertIn("NativeBackupForwardUpgradeService",backend)
        self.assertNotIn("NativeBackupForwardUpgradeService",(APP / "BridgeHandlerCatalog.cs").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
