# Observabilidade e operação planejadas

**Status:** Proposed. Reutilizar padrões OpenTelemetry, logs e New Relic documentados no gearflow-app; consultar infra-k8s para configurações reais. Não há host instrumentado no núcleo atual.

## Correlação

Traces HTTP e MassTransit preservam contexto W3C. Logs estruturados incluem service.name, TraceId/SpanId, CorrelationId, MessageId, causationId, operationVersion e código de erro. CorrelationId de OS não substitui TraceId e não vai como label de métrica de alta cardinalidade. Não registrar tokens, CPF, e-mail nem payload financeiro bruto.

## Sinais e operação

- Histograma de duração de diagnóstico/reparo por fase; medir início físico corretamente, separar tempo de fila.
- Tamanho/idade da fila, número de operações esperando autorização, Sagas sem progresso (no OS).
- Erros HTTP/dependência de preço, retries, conflitos de concorrência, cancelamentos rejeitados.
- Idade/backlog da outbox, filas `_error`, processamento e reprocessamento de mensagens.
- CPU/memória e probes do cluster pela infraestrutura existente.

Rotas alvo: /health/live sem dependências; /health/ready verifica banco e condições mínimas de consumo do broker (política a fixar); /metrics restrito à rede de observabilidade; OTLP para coletor configurado por ambiente. Não expor console RabbitMQ/banco publicamente para a demonstração.

## Runbook mínimo antes de produção/demo

| Alerta | Ação |
|---|---|
| Outbox parada | Verificar broker/conectividade e saúde do worker; não apagar mensagens para limpar alerta |
| Fila de erro | Inspecionar código/contrato sem dados sensíveis; corrigir causa; replay controlado mantendo identidade |
| RecoveryRequired | Consultar Saga, estado Operation, pagamento e reserva; não liberar/reembolsar sem reconciliar reparo físico |
| Preço indisponível | Restabelecer API/credencial de catálogo; repetir mesma intenção HTTP |
| Migration falhou | Interromper rollout; preservar dados e versão compatível anterior; investigar antes de reexecutar |

Banco/broker não devem ser recriados para resolver erro de negócio. Retenção de inbox, intenções HTTP, tombstones e históricos deve cobrir a janela máxima de replay. Até definir expiração segura, não expirar tombstones para permitir que comandos antigos reabram uma OS. Limpeza operacional é decisão de arquitetura com testes de replay.

Secrets/registry/namespace, recursos, HPA e dashboards dependem de infra-k8s/infra-database. Não afirmar que os manifests antigos do monólito já atendem múltiplos serviços.
