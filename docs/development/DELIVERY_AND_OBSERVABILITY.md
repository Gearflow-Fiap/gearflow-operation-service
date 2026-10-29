# Execução, entrega e observabilidade

## Ambiente local

Docker Desktop com engine Linux. `.env.local` é privado e ignorado; `.env.local.example` lista as chaves. Gerar senha de PostgreSQL e segredo JWT forte (mínimo 32 caracteres), sem reutilizar credenciais de produção.

```powershell
docker compose --env-file .env.local -f compose.local.yml up --build -d api
docker compose --env-file .env.local -f compose.local.yml ps --all
pwsh -NoProfile -File scripts/Test-LocalDelivery.ps1
```

O smoke autentica localmente, verifica 401/403 e cria/consulta/desativa seu próprio item de catálogo. A API está em http://127.0.0.1:5085. Inventário, broker e OTLP ficam desligados/não configurados; concluir diagnóstico ainda requer preços reais. `/health/live` retorna 200 enquanto o processo responde; `/health/ready` exige PostgreSQL e migrations. Readiness com broker habilitado inclui os checks do transporte. Não executar `down --volumes` se quiser preservar os dados locais.

## Migrations

```powershell
$env:OPERATION_CONNECTION_STRING='<conexão do banco exclusivo>'
dotnet run --project tools/Operation.Migrate -c Release -- --check
dotnet run --project tools/Operation.Migrate -c Release -- --apply
```

Check: 0 atualizado, 2 pendente; apply: 0 sucesso; erro: 1; entrada ausente/inválida: 64. Também aceita ConnectionStrings__Operation. Não passar conexão no argumento. Ferramenta exige configuração explícita, não usa banco padrão e não imprime credenciais. A quarta migration adiciona TraceParent/TraceState opcionais à inbox. Backups/revisão de permissões/compatibilidade pertencem à operação do ambiente.

## Telemetria

| Chave | Uso |
|---|---|
| Telemetry__ExportEnabled | false padrão; true para exportar |
| Otlp__Endpoint | endpoint base OTLP; fallback OTEL_EXPORTER_OTLP_ENDPOINT |
| OTEL_EXPORTER_OTLP_PROTOCOL | grpc padrão ou http/protobuf |
| OTEL_EXPORTER_OTLP_HEADERS | autenticação do coletor, em secret |
| Telemetry__AllowInsecureHttp | autoriza HTTP interno fora de loopback explicitamente |
| Telemetry__BacklogEnabled | true padrão; coleta SQL a cada 15s |

HTTPS obrigatório fora de loopback, salvo autorização explícita para coletor interno. HTTP/protobuf acrescenta /v1/traces, /v1/metrics e /v1/logs ao endpoint base; não fornecer endpoint de um sinal específico. A infraestrutura existente usa New Relic OTLP grpc em https://otlp.nr-data.net:4317, com header api-key; configurar a chave em secret e validar a região/conta do grupo. Não há endpoint público /metrics.

Métricas próprias: gearflow.operation.commands, command.duration, outbox.deliveries, outbox.pending, inbox.incomplete, outbox.oldest e backlog.sample_success (prefixo gearflow.operation em todas). Confirmar nomes exportados no backend antes de construir dashboards. Correlacionar logs/traces pelo traceId; OS/intenção são atributos de spans, não labels das métricas próprias. Alertas sugeridos para homologação: readiness indisponível, erro de comando, backlog/idade crescendo, sample_success=0, DLQ e tempo de cotação. Limiares exigem carga/SLO acordados.

## CI e entrega remota pendentes

Sonar: configurar SONAR_PROJECT_KEY, SONAR_HOST_URL, opcional SONAR_ORGANIZATION como variables e SONAR_TOKEN como secret. Sem projeto a análise remota é explicitamente pulada; gate local >=80% por assembly permanece obrigatório. Relatórios incluem Cobertura e OpenCover. Nenhuma execução remota ou resultado Sonar foi comprovado localmente.

Publicação: DELIVERY_ENABLED=true autoriza workflow a publicar GHCR após CI de main. Deploy: DEPLOY_ENABLED=true e environment operation-homologation com revisores/proteções configurados, AWS_ROLE_ARN, AWS_REGION e EKS_CLUSTER_NAME. Manter desligado até revisar PostgreSQL, permissões e contratos/topologia. Consulte [Kubernetes](../../k8s/README.md). O workflow não habilita automaticamente proteções do GitHub.

[ADR-012](../architecture/adr-012-observabilidade.md) e [ADR-013](../architecture/adr-013-entrega-operation.md). Homologação de Saga/BDD depende de OS/Budget/Inventory e não é comprovada por estes testes locais.
