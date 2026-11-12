import base64
import hashlib
import hmac
import json
import os
import unittest
from unittest.mock import patch

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

    @patch.dict(os.environ, {'JWT_SIGNING_KEY': 'x' * 32}, clear=True)
    @patch('configure_inventory.subprocess.run')
    def test_patch_preserves_other_secret_keys_and_never_uses_token_as_argument(self, run):
        run.return_value.returncode = 0
        config.main()
        args, kwargs = run.call_args
        self.assertIn('--server-side', args[0])
        patch_body = json.loads(kwargs['input'])
        self.assertEqual(['Inventory__BearerToken'], list(patch_body['data']))
        token = base64.b64decode(patch_body['data']['Inventory__BearerToken']).decode()
        self.assertNotIn(token, ' '.join(args[0]))

    @patch.dict(os.environ, {}, clear=True)
    @patch('configure_inventory.subprocess.run')
    def test_missing_key_does_not_touch_cluster(self, run):
        with self.assertRaises(SystemExit):
            config.main()
        run.assert_not_called()


if __name__ == '__main__':
    unittest.main()
