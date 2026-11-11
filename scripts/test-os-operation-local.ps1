param(
    [string]$OsUrl = 'http://localhost:5080',
    [string]$OperationUrl = 'http://localhost:5085',
    [string]$JwtSecret = 'dev-only-super-secret-change-me-32chars-minimum',
    [string]$Issuer = 'GearFlow.Api',
    [string]$Audience = 'GearFlow.Client'
)
$ErrorActionPreference = 'Stop'

function Base64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}
function Request([string]$Method, [string]$Url, $Body = $null, [hashtable]$ExtraHeaders = @{}) {
    $headers = @{ Authorization = "Bearer $script:token" }
    foreach ($key in $ExtraHeaders.Keys) { $headers[$key] = $ExtraHeaders[$key] }
    $parameters = @{ Method = $Method; Uri = $Url; Headers = $headers; TimeoutSec = 15 }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $Body | ConvertTo-Json -Depth 15 -Compress
    }
    Invoke-RestMethod @parameters
}
function New-TestCpf {
    $digits = @(1..9 | ForEach-Object { Get-Random -Minimum 0 -Maximum 10 })
    foreach ($weight in @(10, 11)) {
        $sum = 0
        for ($i = 0; $i -lt $digits.Count; $i++) { $sum += $digits[$i] * ($weight - $i) }
        $remainder = $sum % 11
        $digits += $(if ($remainder -lt 2) { 0 } else { 11 - $remainder })
    }
    $digits -join ''
}
function Wait-Operation([guid]$Id) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { return Request 'Get' "$OperationUrl/api/operation/service-orders/$Id" }
        catch {
            if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw }
        }
        Start-Sleep -Seconds 1
    }
    throw "Operation nao recebeu a OS $Id em 30s. Verifique outbox, logs e operation-start-diagnostic_error."
}

Write-Host '[1/6] Verificando saude das APIs...'
Invoke-RestMethod "$OsUrl/health/ready" -TimeoutSec 10 | Out-Host
Invoke-RestMethod "$OperationUrl/health/ready" -TimeoutSec 10 | Out-Host

$head = Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$claims = @{ sub = [guid]::NewGuid().ToString(); actor_type = 'staff'; iss = $Issuer; aud = $Audience; exp = [DateTimeOffset]::UtcNow.AddHours(1).ToUnixTimeSeconds() } | ConvertTo-Json -Compress
$payload = Base64Url ([Text.Encoding]::UTF8.GetBytes($claims))
$unsigned = "$head.$payload"
$hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($JwtSecret))
try { $script:token = "$unsigned.$(Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($unsigned))))" }
finally { $hmac.Dispose() }

Write-Host '[2/6] Verificando bloqueio de acesso sem token...'
try {
    Invoke-RestMethod "$OperationUrl/api/catalog/jobs/" -TimeoutSec 10 | Out-Null
    throw 'Falha: endpoint protegido aceitou acesso anonimo.'
} catch {
    if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 401) { throw }
    Write-Host 'OK: acesso anonimo retornou 401.'
}

Write-Host '[3/6] Cadastrando servico, cliente e veiculo de teste...'
$suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
$job = Request 'Post' "$OperationUrl/api/catalog/jobs/" @{ name = "Integracao $suffix"; description = 'Mao de obra de teste sem pecas ou consumiveis'; priceCents = 15000 }
$client = Request 'Post' "$OsUrl/api/clientes/" @{ name = "Teste integracao $suffix"; document = (New-TestCpf); email = "teste-$suffix@example.com"; phone = '11999999999' }
$letters = -join (1..3 | ForEach-Object { [char](Get-Random -Minimum 65 -Maximum 91) })
$plate = $letters + (Get-Random -Minimum 1000 -Maximum 10000).ToString()
$vehicle = Request 'Post' "$OsUrl/api/veiculos/" @{ clientId = $client.id; plate = $plate; model = 'Veiculo de teste' }

Write-Host '[4/6] Abrindo OS e aguardando entrega pelo RabbitMQ...'
$order = Request 'Post' "$OsUrl/api/ordens-servico/" @{ vehicleId = $vehicle.id; services = @(@{ catalogJobId = $job.id; description = $job.name }) }
$orderId = [guid]$order.id
$operation = Wait-Operation $orderId
Write-Host "OK: OS $orderId chegou ao Operation."
$operation | ConvertTo-Json -Depth 15 | Out-Host

Write-Host '[5/6] Iniciando diagnostico e verificando replay idempotente...'
$operationUri = "$OperationUrl/api/operation/service-orders/$orderId"
$read = Invoke-WebRequest -UseBasicParsing -Uri $operationUri -Headers @{ Authorization = "Bearer $script:token" } -TimeoutSec 15
$headers = @{ 'If-Match' = [string]$read.Headers['ETag']; 'Idempotency-Key' = [guid]::NewGuid().ToString() }
$started = Request 'Post' "$operationUri/diagnostic/start" $null $headers
$replayed = Request 'Post' "$operationUri/diagnostic/start" $null $headers
if ($started.version -ne $replayed.version) { throw 'Replay alterou a versao.' }
Write-Host "OK: mesmo comando repetido manteve a versao $($started.version)."

Write-Host '[6/6] Concluindo diagnostico sem dependencia de Inventory...'
$read = Invoke-WebRequest -UseBasicParsing -Uri $operationUri -Headers @{ Authorization = "Bearer $script:token" } -TimeoutSec 15
$headers = @{ 'If-Match' = [string]$read.Headers['ETag']; 'Idempotency-Key' = [guid]::NewGuid().ToString() }
Request 'Post' "$operationUri/diagnostic/finish" @{ consumables = @() } $headers | Out-Host
Write-Host 'Diagnostico concluido. Confira o retorno na OS com o comando SQL indicado no roteiro.'
Write-Host "OS_ID=$orderId"
Write-Host "CATALOG_JOB_ID=$($job.id)"
Write-Host 'O teste cria dados locais e nao simula aprovacao, pagamento ou execucao comercial.'
