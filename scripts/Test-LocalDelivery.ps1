#Requires -Version 7.0
param([uri]$BaseUrl = 'http://127.0.0.1:5085/', [string]$EnvFile = '.env.local')
$ErrorActionPreference = 'Stop'
if (!$BaseUrl.IsLoopback -or $BaseUrl.Scheme -ne 'http') { throw 'Smoke local exige HTTP loopback.' }
$operationSecretLine = Get-Content -LiteralPath $EnvFile | Where-Object { $_.StartsWith('OPERATION_JWT_SECRET=') } | Select-Object -First 1
if (!$operationSecretLine) { throw 'Secret JWT local ausente.' }
$operationSecret = $operationSecretLine.Substring('OPERATION_JWT_SECRET='.Length)
function ConvertTo-Base64Url([byte[]]$Bytes) { [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+','-').Replace('/','_') }
function New-LocalToken([string]$Actor) {
    $operationNow = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $operationHeader = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $operationClaims = @{ iss='gearflow-local'; aud='gearflow-operation'; sub=[guid]::NewGuid().ToString(); actor_type=$Actor; nbf=$operationNow-5; exp=$operationNow+120 } | ConvertTo-Json -Compress
    $operationPayload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($operationClaims))
    $operationUnsigned = "$operationHeader.$operationPayload"
    $operationMac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($operationSecret))
    try { "$operationUnsigned.$(ConvertTo-Base64Url ($operationMac.ComputeHash([Text.Encoding]::UTF8.GetBytes($operationUnsigned))))" }
    finally { $operationMac.Dispose() }
}
function Request([string]$Method, [string]$Path, [hashtable]$Headers = @{}, [string]$Body = '') {
    $operationArgs = @{ Uri=[uri]::new($BaseUrl, $Path); Method=$Method; Headers=$Headers; SkipHttpErrorCheck=$true; TimeoutSec=10 }
    if ($Body) { $operationArgs.Body=$Body; $operationArgs.ContentType='application/json' }
    Invoke-WebRequest @operationArgs
}
function Require([object]$Response, [int]$Status) { if ([int]$Response.StatusCode -ne $Status) { throw "Status inesperado: $($Response.StatusCode), esperado $Status." } }
$operationLive=Request GET 'health/live'; Require $operationLive 200
$operationReady=Request GET 'health/ready'; Require $operationReady 200
$operationAnonymous=Request GET 'api/catalog/jobs/'; Require $operationAnonymous 401
$operationCustomer=Request GET 'api/catalog/jobs/' @{ Authorization='Bearer '+(New-LocalToken customer) }; Require $operationCustomer 403
$operationStaff=@{ Authorization='Bearer '+(New-LocalToken staff) }
$operationBody=@{ name='delivery-smoke-'+[guid]::NewGuid().ToString('N'); description='Verificação local'; priceCents=0 } | ConvertTo-Json -Compress
$operationCreated=Request POST 'api/catalog/jobs/' $operationStaff $operationBody; Require $operationCreated 201
$operationItem=$operationCreated.Content | ConvertFrom-Json
$operationItemId=[guid]::Parse($operationItem.id)
$operationRead=Request GET "api/catalog/jobs/$operationItemId" $operationStaff; Require $operationRead 200
$operationDelete=@{ Authorization=$operationStaff.Authorization; 'If-Match'=@($operationCreated.Headers.ETag)[0] }
$operationDeactivated=Request DELETE "api/catalog/jobs/$operationItemId" $operationDelete; Require $operationDeactivated 200
[ordered]@{ checkedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); liveness=200; readiness=200; anonymous=401; customer=403; catalogCreate=201; catalogRead=200; catalogDeactivate=200 } | ConvertTo-Json
