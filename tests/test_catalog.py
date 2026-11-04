from pathlib import Path
import sys
import unittest
from unittest.mock import patch
import urllib.error
import json

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'scripts'))
from catalog import select_installer, validate_app, version_key, get_manifest, resolve
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

    def test_installed_identity_metadata_is_preserved(self):
        data = self.fixture()
        data['ProductCode'] = '{11111111-1111-1111-1111-111111111111}'
        data['AppsAndFeaturesEntries'] = [{'DisplayName': 'Example Tool', 'Publisher': 'Example Ltd',
            'UpgradeCode': '{22222222-2222-2222-2222-222222222222}'}]
        item = select_installer(data)
        self.assertIn(data['ProductCode'], item['productCodes'])
        self.assertIn(data['AppsAndFeaturesEntries'][0]['UpgradeCode'], item['upgradeCodes'])
        self.assertEqual(item['detectionNames'], ['Example Tool'])
        self.assertEqual(item['detectionPublishers'], ['Example Ltd'])

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

    def test_portable_requires_explicit_support_and_x64(self):
        data = self.fixture(kind='portable')
        with self.assertRaises(ValueError):
            select_installer(data)
        self.assertEqual(select_installer(data, allow_portable=True)['type'], 'portable')
        with self.assertRaises(ValueError):
            select_installer(self.fixture('x86', 'portable'), allow_portable=True)

    def test_child_package_directory_is_not_a_version(self):
        missing = urllib.error.HTTPError('https://example.com', 404, 'Not found', {}, None)
        manifest = 'PackageIdentifier: Microsoft.Office\nPackageVersion: 16.0.1\nInstallers: []'
        with patch('catalog.get', side_effect=[json.dumps([{'name': '2010', 'type': 'dir'}, {'name': '16.0.1', 'type': 'dir'}]), missing, manifest]):
            version, _ = get_manifest('Microsoft.Office')
            self.assertEqual(version, '16.0.1')

    def test_manifest_server_error_is_not_hidden(self):
        failure = urllib.error.HTTPError('https://example.com', 503, 'Unavailable', {}, None)
        with patch('catalog.get', side_effect=[json.dumps([{'name': '1.0', 'type': 'dir'}]), failure]):
            with self.assertRaises(urllib.error.HTTPError):
                get_manifest('Test.App')

    def test_x86_exception_limited_to_anydesk(self):
        with self.assertRaises(ValueError):
            resolve({'id': 'Other.App', 'name': 'Other', 'allowX86': True})
        data = self.fixture('x86', 'exe')
        data['InstallerSwitches'] = {'Silent': '--install --silent'}
        with patch('catalog.get_manifest', return_value=('9.0', data)):
            app = resolve({'id': 'AnyDesk.AnyDesk', 'name': 'AnyDesk', 'allowX86': True})
            self.assertEqual(app['architecture'], 'x86')

    def test_office_uses_odt_for_x64_payload(self):
        data = self.fixture('x86', 'exe')
        with patch('catalog.get_manifest', return_value=('16.0.1', data)) as fetch:
            app = resolve({'id': 'Microsoft.Office', 'name': 'Office'})
            fetch.assert_called_once_with('Microsoft.OfficeDeploymentTool')
            self.assertEqual((app['architecture'], app['bootstrapArchitecture'], app['type']), ('x64', 'x86', 'office-odt'))

    def test_anydesk_prefers_x64_if_vendor_adds_it(self):
        data = self.fixture('x86', 'exe')
        data['InstallerSwitches'] = {'Silent': '--silent'}
        data['Installers'].append({**data['Installers'][0], 'Architecture': 'x64'})
        with patch('catalog.get_manifest', return_value=('10.0', data)):
            app = resolve({'id': 'AnyDesk.AnyDesk', 'name': 'AnyDesk', 'allowX86': True})
            self.assertEqual(app['architecture'], 'x64')

    def test_issue_crlf_and_optional_publisher(self):
        app = parse('### ID de WinGet\r\n\r\n7zip.7zip\r\n\r\n### Nombre\r\n\r\n7-Zip\r\n\r\n### Fabricante\r\n\r\n_No response_')
        self.assertEqual(app, {'id': '7zip.7zip', 'name': '7-Zip', 'publisher': ''})

    def test_shell_and_path_injection_in_id_rejected(self):
        for value in ['../../evil', 'App.Test;echo hi', 'App.Test$(whoami)', 'App.Test\nBad', 'App/Test']:
            with self.assertRaises(ValueError):
                validate_app({'id': value, 'name': 'Test'})


if __name__ == '__main__':
    unittest.main()
