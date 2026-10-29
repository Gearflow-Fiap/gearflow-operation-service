param([string]$EnvFile = (Join-Path (Split-Path $PSScriptRoot -Parent) '.env.local'))
$ErrorActionPreference = 'Stop'

if (Test-Path -LiteralPath $EnvFile) {
    $operationSettings = @{}
    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        if ($line -match '^(OPERATION_POSTGRES_PASSWORD|OPERATION_JWT_SECRET)=(.*)$') {
            $operationSettings[$Matches[1]] = $Matches[2]
        }
    }
    if ($operationSettings['OPERATION_POSTGRES_PASSWORD'].Length -lt 16 -or $operationSettings['OPERATION_JWT_SECRET'].Length -lt 32) {
        throw 'Existing local environment is incomplete. Configure password (16+ characters) and JWT secret (32+). Existing values were preserved.'
    }
    Write-Output 'Local environment already configured; existing credentials preserved.'
    return
}

function New-OperationSecret([int]$Length) {
    $operationBytes = New-Object byte[] $Length
    $operationRng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $operationRng.GetBytes($operationBytes) }
    finally { $operationRng.Dispose() }
    -join ($operationBytes | ForEach-Object { $_.ToString('x2') })
}

$operationContent = "OPERATION_POSTGRES_PASSWORD=$(New-OperationSecret 24)`nOPERATION_JWT_SECRET=$(New-OperationSecret 48)`nOPERATION_INVENTORY_URL=`nOPERATION_INVENTORY_TOKEN=`nOPERATION_INVENTORY_HTTP=false`n"
$operationStream = [IO.File]::Open([IO.Path]::GetFullPath($EnvFile), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $operationData = [Text.Encoding]::UTF8.GetBytes($operationContent)
    $operationStream.Write($operationData, 0, $operationData.Length)
}
finally { $operationStream.Dispose() }
Write-Output 'Local environment created with random credentials. Values are not printed and must not be committed.'
