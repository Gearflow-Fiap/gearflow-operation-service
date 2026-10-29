# Próximos incrementos após o núcleo local

Baseline em 28/10/2026. Passos 1–3 concluíram domínio/aplicação. O usuário autorizou planejar e iniciar a sequência seguinte; este checkpoint começa pelo incremento 4, com qualidade e testes antes de avançar.

| Incremento | Escopo | Critério de conclusão | Estado |
|---|---|---|---|
| 4 — persistência | PostgreSQL exclusivo, EF/migrations, rehidratação, histórico, journal/inbox de aplicação, CAS e outbox persistente | Banco real comprova roundtrip em novos contextos, atomicidade, replay, concorrência, consultas paginadas e retenção sem limpeza perigosa | Concluído; ver evidência do incremento 4 |
| 5 — API e preços reais | Host ASP.NET Core, JWT staff, ProblemDetails, ETag/idempotência, OpenAPI, integração de preços autenticada | Testes HTTP/auth e preço, sem acesso a banco alheio; regras comerciais alinhadas | Planejado |
| 6 — contratos e mensageria | Pacote tipado acordado, MassTransit/RabbitMQ, consumers/replies, entrega da outbox e inbox de transporte | Banco/broker reais, duplicatas, reinício, DLQ e compatibilidade com OS/Budget/Inventory | Planejado |
| 7 — integração e entrega | Saga/BDD, Docker/K8s, Sonar/CI/CD, métricas/traces e documentação final | Fluxo real com compensação, >=80%, deploy e evidências da fase 4 | Planejado |

A ordem de contratos/preço pode ser refinada com os outros responsáveis. Não escolher silenciosamente política financeira de consumíveis. Outbox persistente no incremento 4 não significa eventos já publicados; o dispatcher do broker pertence ao 6. Journal de intenção atua como inbox de aplicação; inbox MassTransit virá junto dos consumers.

O incremento 4 não modifica bancos existentes nem publica/deploya serviços. Testes usam database/container exclusivo e credenciais de desenvolvimento.

