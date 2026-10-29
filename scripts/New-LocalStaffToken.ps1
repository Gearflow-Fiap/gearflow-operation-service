#Requires -Version 7.0
param(
    [string]$EnvFile = (Join-Path (Split-Path $PSScriptRoot -Parent) '.env.local'),
    [ValidateRange(1, 60)][int]$ExpiresInMinutes = 30
)
$ErrorActionPreference = 'Stop'
$operationSecretLine = Get-Content -LiteralPath $EnvFile | Where-Object { $_.StartsWith('OPERATION_JWT_SECRET=') } | Select-Object -First 1
if (!$operationSecretLine) { throw 'Local JWT secret missing. Initialize .env.local first.' }
$operationSecret = $operationSecretLine.Substring('OPERATION_JWT_SECRET='.Length)
if ($operationSecret.Length -lt 32) { throw 'Local JWT secret must have at least 32 characters.' }

function ConvertTo-OperationBase64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$operationNow = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$operationHeader = ConvertTo-OperationBase64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$operationClaims = @{
    iss = 'gearflow-local'; aud = 'gearflow-operation'; sub = [guid]::NewGuid().ToString()
    actor_type = 'staff'; nbf = $operationNow - 5; exp = $operationNow + $ExpiresInMinutes * 60
} | ConvertTo-Json -Compress
$operationPayload = ConvertTo-OperationBase64Url ([Text.Encoding]::UTF8.GetBytes($operationClaims))
$operationUnsigned = "$operationHeader.$operationPayload"
$operationMac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($operationSecret))
try { "$operationUnsigned.$(ConvertTo-OperationBase64Url ($operationMac.ComputeHash([Text.Encoding]::UTF8.GetBytes($operationUnsigned))))" }
finally { $operationMac.Dispose() }
