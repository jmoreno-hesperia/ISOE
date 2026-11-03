from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'scripts'))
from catalog import select_installer, validate_app, version_key
from add_app import parse


class CatalogTests(unittest.TestCase):
    def fixture(self, architecture='x64', kind='msi'):
        return {'InstallerType': kind, 'Installers': [{'Architecture': architecture,
                'InstallerUrl': 'https://example.com/app', 'InstallerSha256': 'A' * 64}]}

    def test_never_falls_back_to_x86_or_arm(self):
        for arch in ['x86', 'arm64', 'neutral']:
            with self.assertRaises(ValueError):
                select_installer(self.fixture(arch))

    def test_rejects_http_and_invalid_hash(self):
        for field, value in [('InstallerUrl', 'http://example.com/app'), ('InstallerSha256', 'BAD')]:
            data = self.fixture()
            data['Installers'][0][field] = value
            with self.assertRaises(ValueError):
                select_installer(data)

    def test_silent_switch_inheritance(self):
        data = self.fixture(kind='exe')
        data['InstallerSwitches'] = {'Silent': '/silent', 'Custom': 'NO_REBOOT=1'}
        data['Installers'][0]['InstallerSwitches'] = {'Silent': '/quiet'}
        self.assertEqual(select_installer(data)['arguments'], '/quiet NO_REBOOT=1')

    def test_unknown_exe_is_rejected(self):
        with self.assertRaises(ValueError):
            select_installer(self.fixture(kind='exe'))

    def test_user_scope_dependencies_and_interactive_rejected(self):
        for field, value in [('Scope', 'user'), ('Dependencies', {'PackageDependencies': [{'PackageIdentifier': 'Test.Dependency'}]}), ('InstallModes', ['interactive'])]:
            data = self.fixture()
            data[field] = value
            with self.assertRaises(ValueError):
                select_installer(data)

    def test_placeholders_rejected(self):
        data = self.fixture(kind='exe')
        data['InstallerSwitches'] = {'Silent': '--install <INSTALLPATH>'}
        with self.assertRaises(ValueError):
            select_installer(data)

    def test_numeric_version_sort(self):
        self.assertEqual(max(['9.9', '10.2', '10.11'], key=version_key), '10.11')

    def test_issue_crlf_and_optional_publisher(self):
        app = parse('### ID de WinGet\r\n\r\n7zip.7zip\r\n\r\n### Nombre\r\n\r\n7-Zip\r\n\r\n### Fabricante\r\n\r\n_No response_')
        self.assertEqual(app, {'id': '7zip.7zip', 'name': '7-Zip', 'publisher': ''})

    def test_shell_and_path_injection_in_id_rejected(self):
        for value in ['../../evil', 'App.Test;echo hi', 'App.Test$(whoami)', 'App.Test\nBad', 'App/Test']:
            with self.assertRaises(ValueError):
                validate_app({'id': value, 'name': 'Test'})


if __name__ == '__main__':
    unittest.main()
