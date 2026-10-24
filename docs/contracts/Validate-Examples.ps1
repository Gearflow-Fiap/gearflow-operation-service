# Requires PowerShell 7 with Test-Json. Validates logical bodies, not transport envelopes.
$ErrorActionPreference = 'Stop'
$contractRoot = Join-Path $PSScriptRoot 'v1'
$contractManifest = Get-Content (Join-Path $contractRoot 'manifest.json') -Raw | ConvertFrom-Json
foreach ($contractMessage in $contractManifest.messages) {
    $contractSchema = Get-Content (Join-Path $contractRoot 'operation.schema.json') -Raw | ConvertFrom-Json -AsHashtable
    $contractSchema['$ref'] = '#/$defs/' + $contractMessage.name
    $contractSchemaText = ConvertTo-Json -InputObject $contractSchema -Depth 100 -Compress
    $contractExample = Get-Content (Join-Path $contractRoot $contractMessage.example) -Raw
    if (-not (Test-Json -Json $contractExample -Schema $contractSchemaText)) {
        throw "Invalid example: $($contractMessage.name)"
    }
    Write-Output "Validated $($contractMessage.name)"
}
