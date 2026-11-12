"""Provision a scoped service identity token without printing credentials.

Run on deployment and renew before expiry. JWT_SIGNING_KEY must be accepted by
Inventory; INVENTORY_JWT_SIGNING_KEY can select a separate Inventory key.
"""
import base64
import hashlib
import hmac
import json
import os
import subprocess
import time


def build_token(key, issuer, audience, ttl_minutes, now=None):
    if len(key.encode()) < 32 or not issuer.strip() or not audience.strip():
        raise ValueError('Inventory signing key (32 UTF-8 bytes), issuer and audience are required.')
    if not 1 <= ttl_minutes <= 43200:
        raise ValueError('Inventory token TTL must be between 1 and 43200 minutes.')
    now = int(time.time()) if now is None else now
    payload = {'sub': 'gearflow-operation-service', 'iss': issuer, 'aud': audience,
               'iat': now, 'exp': now + ttl_minutes * 60}

    def encode(value):
        return base64.urlsafe_b64encode(value).rstrip(b'=').decode()

    unsigned = '.'.join(encode(json.dumps(part, separators=(',', ':')).encode())
                        for part in ({'alg': 'HS256', 'typ': 'JWT'}, payload))
    signature = hmac.new(key.encode(), unsigned.encode(), hashlib.sha256).digest()
    return unsigned + '.' + encode(signature)


def main():
    try:
        token = build_token(
            os.environ.get('INVENTORY_JWT_SIGNING_KEY') or os.environ.get('JWT_SIGNING_KEY', ''),
            os.environ.get('INVENTORY_JWT_ISSUER', 'GearFlow.Api'),
            os.environ.get('INVENTORY_JWT_AUDIENCE', 'GearFlow.Client'),
            int(os.environ.get('INVENTORY_TOKEN_TTL_MINUTES', '10080')))
    except ValueError as error:
        raise SystemExit(str(error)) from None
    # stdin avoids placing credentials in command-line arguments or log output.
    patch = {'apiVersion': 'v1', 'kind': 'Secret',
             'metadata': {'name': 'gearflow-operation-secret', 'namespace': 'gearflow'},
             'data': {'Inventory__BearerToken': base64.b64encode(token.encode()).decode()}}
    result = subprocess.run(
        ['kubectl', 'apply', '--server-side', '--force-conflicts',
         '--field-manager=operation-inventory-credential', '-f', '-'],
        input=json.dumps(patch), text=True, capture_output=True)
    if result.returncode:
        raise SystemExit('Inventory credential configuration failed; check cluster access and the Operation Secret.')
    print('Inventory service token configured; renew before TTL expires and restart Operation to load it.')


if __name__ == '__main__':
    main()
