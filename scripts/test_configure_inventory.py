import base64
import hashlib
import hmac
import json
import os
import unittest
from unittest.mock import patch
from types import SimpleNamespace

import configure_inventory as config


class InventoryCredentialTests(unittest.TestCase):
    def test_signed_service_identity_has_configured_expiry_and_no_staff(self):
        key = 'inventory-test-key-at-least-32-bytes'
        token = config.build_token(key, 'GearFlow.Api', 'GearFlow.Client', 60, now=100)
        header, payload, signature = token.split('.')
        decode = lambda s: base64.urlsafe_b64decode(s + '=' * (-len(s) % 4))
        data = json.loads(decode(payload))
        self.assertEqual('gearflow-operation-service', data['sub'])
        self.assertEqual(3700, data['exp'])
        self.assertEqual('GearFlow.Api', data['iss'])
        self.assertEqual('GearFlow.Client', data['aud'])
        self.assertNotIn('actor_type', data)
        self.assertEqual(hmac.new(key.encode(), (header + '.' + payload).encode(), hashlib.sha256).digest(), decode(signature))

    def test_invalid_key_and_unbounded_expiry_are_rejected(self):
        for key, ttl in [('short', 60), ('x' * 32, 0), ('x' * 32, 43201)]:
            with self.assertRaises(ValueError):
                config.build_token(key, 'issuer', 'audience', ttl)

    @staticmethod
    def existing_secret():
        keys = ('ConnectionStrings__Operation', 'Jwt__Secret', 'Jwt__Issuer',
                'Jwt__Audience', 'RabbitMq__Username', 'RabbitMq__Password', 'Unrelated__Setting')
        return {'apiVersion': 'v1', 'kind': 'Secret', 'type': 'Opaque',
                'metadata': {'name': 'gearflow-operation-secret', 'namespace': 'gearflow',
                             'resourceVersion': '42', 'annotations': {'owner': 'deployment'}},
                'data': {key: base64.b64encode(('test-only-' + key).encode()).decode() for key in keys}}

    @patch.dict(os.environ, {'JWT_SIGNING_KEY': 'x' * 32}, clear=True)
    @patch('configure_inventory.subprocess.run')
    def test_update_preserves_existing_data_metadata_and_concurrency_version(self, run):
        original = self.existing_secret()
        run.side_effect = [SimpleNamespace(returncode=0, stdout=json.dumps(original)), SimpleNamespace(returncode=0)]
        config.main()
        self.assertEqual(2, run.call_count)
        args, kwargs = run.call_args
        self.assertEqual(['kubectl', 'replace', '-f', '-'], args[0])
        updated = json.loads(kwargs['input'])
        self.assertEqual(original['metadata'], updated['metadata'])
        for key, value in original['data'].items():
            self.assertEqual(value, updated['data'][key])
        token = base64.b64decode(updated['data']['Inventory__BearerToken']).decode()
        self.assertNotIn(token, ' '.join(args[0]))

    @patch('configure_inventory.subprocess.run')
    def test_incomplete_secret_is_not_overwritten(self, run):
        original = self.existing_secret()
        del original['data']['RabbitMq__Password']
        run.return_value = SimpleNamespace(returncode=0, stdout=json.dumps(original))
        with self.assertRaises(SystemExit):
            config.store_token('test-token')
        self.assertEqual(1, run.call_count)

    @patch('configure_inventory.subprocess.run')
    def test_read_failure_does_not_attempt_write(self, run):
        run.return_value = SimpleNamespace(returncode=1)
        with self.assertRaises(SystemExit):
            config.store_token('test-token')
        self.assertEqual(1, run.call_count)

    @patch('configure_inventory.subprocess.run')
    def test_conflicting_update_fails_without_force_or_delete(self, run):
        run.side_effect = [SimpleNamespace(returncode=0, stdout=json.dumps(self.existing_secret())), SimpleNamespace(returncode=1)]
        with self.assertRaises(SystemExit):
            config.store_token('test-token')
        self.assertEqual(['kubectl', 'replace', '-f', '-'], run.call_args.args[0])

    @patch.dict(os.environ, {}, clear=True)
    @patch('configure_inventory.subprocess.run')
    def test_missing_key_does_not_touch_cluster(self, run):
        with self.assertRaises(SystemExit):
            config.main()
        run.assert_not_called()


if __name__ == '__main__':
    unittest.main()
