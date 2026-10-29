# Incremento 5A — API operacional

Concluído em 29/10/2026. Incremento 5 completo ainda depende do 5B (catálogo/Inventory real).

## Implementado

Operation.Api organizado em src no slnx. JWT HS256 real com validação de assinatura/algoritmo/emissor/audiência/expiração, policy Staff configurada explicitamente e sub GUID único. Claims antigas não são automaticamente promovidas para staff. Consultas e comandos reutilizam aplicação e PostgreSQL.

GET fila/lista/estado/histórico/durações; POST diagnóstico start/finish/fail e execução start/job/finish. If-Match forte e Idempotency-Key obrigatórios; replay histórico preservado; ator vem do token; budgetJobId identifica linha executada. JSON rejeita propriedades livres/preços do cliente. ProblemDetails e OpenAPI com segurança Bearer e headers de comando. Liveness público, sem afirmar readiness. Sem endpoints públicos para comandos da Saga.

Política financeira ausente bloqueia precificação explicitamente; catálogo do host continua bloqueado até adapter real, sem cotação falsa. RFC-003 apresenta a proposta ao grupo, sem declarar aprovação coletiva.

## Validação

Build/testes Release; 131 unitários + 35 integração (14 persistência + 21 HTTP), todos aprovados, 0 falhas e 0 ignorados.

```text
dotnet test GearFlow.Operation.slnx --no-build --configuration Release -m:1 -nr:false --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/increment-5a-gate --logger trx
python scripts/check_coverage.py TestResults/increment-5a-gate
```

| Assembly | Cobertas/instrumentadas | Linhas |
|---|---|---|
| Operation.Api | 145/148 | 97,97% |
| Operation.Application | 302/302 | 100% |
| Operation.Domain | 370/374 | 98,93% |
| Operation.Infrastructure | 920/933 | 98,61% |

Gate >=80% por assembly aprovado. Exclusão restrita ao arquivo OpenApiXmlCommentSupport.generated.cs, gerado pela biblioteca Microsoft.AspNetCore.OpenApi; nenhum código escrito do serviço excluído. Migrations continuam instrumentadas. CI atualizada para a mesma runsettings; sem execução remota neste checkpoint. TRX/Cobertura brutos no TestResults local ignorado e artifacts da futura execução CI.

PostgreSQL 17.11 real, instância temporária independente do Docker. WebApplicationFactory executa pipeline HTTP com JWT assinado de teste (não fake authentication). Catálogo de sucesso é fake explícito. Testes cobrem 401/403, audiência/assinatura/expiração, ETag/precondições, replay após avanço do estado, conflito de intenção, execução por budgetJobId, payload inválido/preço livre, falha de diagnóstico, 503 sem mutação e OpenAPI. Fixture serializada fecha pools entre bancos de cenários para evitar esgotar conexões ociosas.

## Limites e retomada

Não há integração HTTP real com Inventory, catálogo de mão de obra persistente, broker/consumer/dispatcher, Saga distribuída ou deploy. Token emitido atualmente pelo monólito ainda não inclui actor_type; proposta requer alinhamento com emissor. Identidade e clientes ainda são dependências de transição, não extraídos neste trabalho. Gearflow-app, Inventory, Budget e infra-broker não foram alterados.

Próximo: 5B. Confirmar a RFC-003 com grupo, definir dono da OS/Saga (recomendado OS separado), política per-unit-v1/unidade dos insumos e credencial de serviço. Implementar catálogo próprio e adapter Inventory com testes de contrato/HTTP; depois iniciar mensageria.
