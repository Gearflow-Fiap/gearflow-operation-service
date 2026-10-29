# Próximos incrementos após o núcleo local

Baseline em 28/10/2026. Passos 1–3 concluíram domínio/aplicação. O usuário autorizou planejar e iniciar a sequência seguinte; este checkpoint começa pelo incremento 4, com qualidade e testes antes de avançar.

| Incremento | Escopo | Critério de conclusão | Estado |
|---|---|---|---|
| 4 — persistência | PostgreSQL exclusivo, EF/migrations, rehidratação, histórico, journal/inbox de aplicação, CAS e outbox persistente | Banco real comprova roundtrip em novos contextos, atomicidade, replay, concorrência, consultas paginadas e retenção sem limpeza perigosa | Concluído; ver evidência do incremento 4 |
| 5 — API e preços reais | Host ASP.NET Core, JWT staff, ProblemDetails, ETag/idempotência, OpenAPI, integração de preços autenticada | Testes HTTP/auth e preço, sem acesso a banco alheio; regras comerciais alinhadas | 5A/5B implementados; homologação entre serviços pendente |
| 6 — contratos e mensageria | Pacote tipado acordado, MassTransit/RabbitMQ, consumers/replies, entrega da outbox e inbox de transporte | Banco/broker reais, duplicatas, reinício, DLQ e compatibilidade com OS/Budget/Inventory | Planejado |
| 7 — integração e entrega | Saga/BDD, Docker/K8s, Sonar/CI/CD, métricas/traces e documentação final | Fluxo real com compensação, >=80%, deploy e evidências da fase 4 | Planejado |

A ordem de contratos/preço pode ser refinada com os outros responsáveis. Não escolher silenciosamente política financeira de consumíveis. Outbox persistente no incremento 4 não significa eventos já publicados; o dispatcher do broker pertence ao 6. Journal de intenção atua como inbox de aplicação; inbox MassTransit virá junto dos consumers.

O incremento 4 não modifica bancos existentes nem publica/deploya serviços. Testes usam database/container exclusivo e credenciais de desenvolvimento.


## Detalhamento do incremento 5

- 5A: host HTTP, JWT/policy staff explícita, ProblemDetails, consultas, comandos com If-Match e Idempotency-Key, OpenAPI e testes HTTP/PostgreSQL.
- 5B: persistir catálogo de mão de obra, implementar adapter Inventory com timeout/credencial, homologar per-unit-v1 com Budget e claims com o emissor. Adapter composto implementado: mão de obra PostgreSQL e GET Inventory. Testes 5B usam HTTP real com servidor substituto; homologação com Inventory completo/credencial real pendente.
- Padrão recomendado para o grupo: [RFC-003](../rfcs/rfc-003-padrao-fase-4.md). Três serviços + infra-broker ainda precisam do dono da OS/Saga; manter OS separado é a proposta.

## Retomada após 5B

[Catálogo e preços](../development/CATALOG_AND_PRICING.md) e [ADR-010](../architecture/adr-010-catalogo-e-precos.md). Próxima etapa: 6, homologar contratos públicos e implementar MassTransit/RabbitMQ, inbox/dispatcher da outbox. Antes de testar o fluxo distribuído, disponibilizar infra-broker e alinhar OS/coordenador, tokens e policy de serviço com o grupo. Código local não substitui homologação coletiva.
