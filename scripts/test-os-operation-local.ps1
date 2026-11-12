param(
    [ValidateSet('DiagnosticSuccess', 'DiagnosticFailure', 'BrokerRecovery', 'OperationRecovery', 'OsRecovery')]
    [string]$Scenario = 'DiagnosticSuccess',
    [string]$OsUrl = 'http://localhost:5080',
    [string]$OperationUrl = 'http://localhost:5085',
    [string]$JwtSecret = 'dev-only-super-secret-change-me-32chars-minimum',
    [string]$Issuer = 'GearFlow.Api',
    [string]$Audience = 'GearFlow.Client',
    [string]$OsComposeFile = (Join-Path $PSScriptRoot '../../gearflow-os-service/docker-compose.yml'),
    [string]$OperationComposeFile = (Join-Path $PSScriptRoot '../compose.yml')
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
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        try { return Request 'Get' "$OperationUrl/api/operation/service-orders/$Id" }
        catch {
            if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw }
        }
        Start-Sleep -Seconds 1
    }
    throw "Operation nao recebeu a OS $Id em 120s. Verifique outbox, logs e operation-start-diagnostic_error."
}

function Compose-Control([string]$File, [string]$Action, [string]$Service) {
    & docker compose -f $File $Action $Service | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Docker falhou: $Action $Service" }
}
function Read-Sql([string]$File, [string]$Database, [string]$Sql) {
    $result = $Sql | & docker compose -f $File exec -T postgres psql -U gearflow -d $Database -v ON_ERROR_STOP=1 -At
    if ($LASTEXITCODE -ne 0) { throw "Consulta de verificacao falhou: $Database" }
    ($result -join "`n").Trim()
}
function Wait-Ready([string]$Url) {
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        try { Invoke-RestMethod "$Url/health/ready" -TimeoutSec 3 | Out-Null; return } catch { }
        Start-Sleep -Seconds 1
    }
    throw "API nao recuperou readiness: $Url"
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
if ($Scenario -in @('BrokerRecovery', 'OperationRecovery')) {
    $controlFile = if ($Scenario -eq 'BrokerRecovery') { $OsComposeFile } else { $OperationComposeFile }
    $controlService = if ($Scenario -eq 'BrokerRecovery') { 'rabbitmq' } else { 'api' }
    Write-Host "Parando temporariamente $controlService. O finally sempre tentara religa-lo."
    try {
        Compose-Control $controlFile 'stop' $controlService
        $order = Request 'Post' "$OsUrl/api/ordens-servico/" @{ vehicleId = $vehicle.id; services = @(@{ catalogJobId = $job.id; description = $job.name }) }
        $orderId = [guid]$order.id
        $countSql = "SELECT count(*) FROM operation.operations WHERE service_order_id = '$orderId';"
        $pendingSql = "SELECT count(*) FROM outbox_messages WHERE ""CorrelationId"" = '$orderId' AND ""Queue"" = 'operation-start-diagnostic' AND ""ProcessedOn"" IS NULL;"
        if ($Scenario -eq 'BrokerRecovery') {
            Start-Sleep -Seconds 3
            if ((Read-Sql $OsComposeFile 'gearflow_os' $pendingSql) -ne '1') { throw 'Comando nao permaneceu pendente na outbox com RabbitMQ parado.' }
            Write-Host 'OK: OS aberta com RabbitMQ parado; StartDiagnostic preservado na outbox.'
        } else {
            $sentSql = "SELECT count(*) FROM outbox_messages WHERE ""CorrelationId"" = '$orderId' AND ""Queue"" = 'operation-start-diagnostic' AND ""ProcessedOn"" IS NOT NULL;"
            for ($attempt = 0; $attempt -lt 30; $attempt++) {
                if ((Read-Sql $OsComposeFile 'gearflow_os' $sentSql) -eq '1') { break }
                Start-Sleep -Seconds 1
            }
            if ((Read-Sql $OsComposeFile 'gearflow_os' $sentSql) -ne '1') { throw 'OS nao publicou o comando enquanto o Operation estava parado.' }
            Write-Host 'OK: OS publicou o comando com consumidor Operation parado.'
        }
        if ((Read-Sql $OperationComposeFile 'gearflow_operation' $countSql) -ne '0') { throw 'Operation recebeu a OS antes da recuperacao esperada.' }
    } finally {
        Compose-Control $controlFile 'start' $controlService
    }
    Wait-Ready $OsUrl
    Wait-Ready $OperationUrl
} else {
$order = Request 'Post' "$OsUrl/api/ordens-servico/" @{ vehicleId = $vehicle.id; services = @(@{ catalogJobId = $job.id; description = $job.name }) }
$orderId = [guid]$order.id
}
$operation = Wait-Operation $orderId
if ($Scenario -in @('BrokerRecovery', 'OperationRecovery')) {
    Start-Sleep -Seconds 5
    $operation = Request 'Get' "$OperationUrl/api/operation/service-orders/$orderId"
    if ((Read-Sql $OperationComposeFile 'gearflow_operation' $countSql) -ne '1') { throw 'Contagem de operacoes apos recuperacao diferente de 1.' }
    if ($operation.version -ne 1 -or @($operation.history).Count -ne 1 -or $operation.status -ne 'Received') {
        throw 'Recuperacao produziu estado inicial inesperado ou efeitos repetidos.'
    }
    Write-Host 'OK: entrega apos recuperacao, versao inicial 1 e um unico registro de recebimento.'
}
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

Write-Host "[6/6] Executando cenario $Scenario..."
$read = Invoke-WebRequest -UseBasicParsing -Uri $operationUri -Headers @{ Authorization = "Bearer $script:token" } -TimeoutSec 15
$headers = @{ 'If-Match' = [string]$read.Headers['ETag']; 'Idempotency-Key' = [guid]::NewGuid().ToString() }
if ($Scenario -eq 'DiagnosticFailure') {
    $reason = 'Teste de integracao: impossibilidade de concluir o diagnostico'
    $failed = Request 'Post' "$operationUri/diagnostic/fail" @{ reason = $reason } $headers
    if ($failed.status -ne 'DiagnosticFailed') { throw 'Operation nao registrou DiagnosticFailed.' }
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $osState = Request 'Get' "$OsUrl/api/ordens-servico/$orderId"
        $operationState = Request 'Get' $operationUri
        $confirmed = @($osState.history | Where-Object { $_.message -match 'Cancelamento confirmado pelo Operation' }).Count -gt 0
        if ($osState.status -eq 'Cancelled' -and $operationState.status -eq 'Canceled' -and $confirmed) { break }
        Start-Sleep -Seconds 1
    }
    if ($osState.status -ne 'Cancelled' -or $operationState.status -ne 'Canceled' -or !$confirmed) {
        throw "Falha nao convergiu em 30s: OS=$($osState.status), Operation=$($operationState.status), confirmacao=$confirmed. OS_ID=$orderId"
    }
    if ($operationState.isActive -ne $false -or $operationState.cancellationReason -ne 'diagnostic-failed' -or $operationState.diagnosticFailureReason -ne $reason) {
        throw "Estado final nao corresponde a falha de diagnostico. OS_ID=$orderId"
    }
    Write-Host 'OK: DiagnosticFailed chegou a OS, que enviou CancelOperation e recebeu OperationCanceled.'
    $osState | Select-Object id, status | Format-Table | Out-Host
    $osState.history | Select-Object status, message | Format-Table -AutoSize -Wrap | Out-Host
    $operationState | Select-Object serviceOrderId, status, isActive, diagnosticFailureReason, cancellationReason | Format-List | Out-Host
} else {
    if ($Scenario -eq 'OsRecovery') {
        try {
            Compose-Control $OsComposeFile 'stop' 'os-service'
            Request 'Post' "$operationUri/diagnostic/finish" @{ consumables = @() } $headers | Out-Host
            $publishedSql = "SELECT count(*) FROM operation.outbox_events WHERE service_order_id = '$orderId' AND version = 3 AND dispatched_at IS NOT NULL;"
            for ($attempt = 0; $attempt -lt 30; $attempt++) {
                if ((Read-Sql $OperationComposeFile 'gearflow_operation' $publishedSql) -eq '1') { break }
                Start-Sleep -Seconds 1
            }
            if ((Read-Sql $OperationComposeFile 'gearflow_operation' $publishedSql) -ne '1') { throw 'Operation nao publicou DiagnosticFinished com OS parada.' }
            $notReceivedSql = "SELECT count(*) FROM service_orders WHERE ""Id"" = '$orderId' AND ""DiagnosticSnapshotJson"" IS NULL;"
            if ((Read-Sql $OsComposeFile 'gearflow_os' $notReceivedSql) -ne '1') { throw 'Snapshot foi gravado antes de recuperar a OS.' }
            Write-Host 'OK: DiagnosticFinished publicado com consumidor OS parado; snapshot ainda nao recebido.'
        } finally {
            Compose-Control $OsComposeFile 'start' 'os-service'
        }
        Wait-Ready $OsUrl
    } else {
        Request 'Post' "$operationUri/diagnostic/finish" @{ consumables = @() } $headers | Out-Host
    }
    if ($Scenario -in @('BrokerRecovery', 'OperationRecovery', 'OsRecovery')) {
        $receivedSql = "SELECT count(*) FROM service_orders WHERE ""Id"" = '$orderId' AND ""DiagnosticSnapshotJson"" IS NOT NULL;"
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if ((Read-Sql $OsComposeFile 'gearflow_os' $receivedSql) -eq '1') { break }
            Start-Sleep -Seconds 1
        }
        if ((Read-Sql $OsComposeFile 'gearflow_os' $receivedSql) -ne '1') { throw 'Diagnostico nao voltou a OS apos recuperacao.' }
        Write-Host 'OK: DiagnosticFinished voltou a OS e foi persistido apos recuperacao.'
    } else {
        Write-Host 'Diagnostico concluido. Confira o retorno na OS com o comando SQL indicado no roteiro.'
    }
}
Write-Host "OS_ID=$orderId"
Write-Host "CATALOG_JOB_ID=$($job.id)"
Write-Host 'O teste cria dados locais e nao simula aprovacao, pagamento ou execucao comercial.'
