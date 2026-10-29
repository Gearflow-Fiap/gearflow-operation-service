# M1 — Scalar e contrato HTTP

Concluído localmente em 29/10/2026. Plano: [CODE_QUALITY_PLAN](../../CODE_QUALITY_PLAN.md). Os demais passos M2–M5 ainda não foram executados.

## Entrega

- 16 endpoints de negócio (11 operação e 5 catálogo) com resumo, descrição e tipos de sucesso; problemas HTTP documentados sem alterar handlers, rotas ou políticas de negócio.
- Distinção explícita entre recebimento/autorização via mensageria e início físico via HTTP; BudgetJobId versus IDs de catálogo/diagnóstico; snapshots e centavos BRL.
- OpenAPI com Bearer, paginação, exemplos de corpos/erros/headers, If-Match, Idempotency-Key e respostas ETag/Cache-Control/Location onde aplicáveis.
- Precondições do catálogo preservadas: criação sem If-Match/intenção; atualização/desativação com If-Match; DELETE retorna 200 com DTO. Cotação indisponível continua 503 sem conclusão parcial.
- Testes EndpointContractTests descobrem as rotas reais por EndpointDataSource: documentação/policy Staff, ausência de AllowAnonymous nas APIs de negócio, chamadas anônimas/cliente em cada método, respostas tipadas/problemas e headers apropriados.
- Um exemplo de cadastro extraído do próprio esquema foi enviado ao host real de teste, com verificação de 201, Location, ETag, leitura subsequente e 428 sem versão.

## Verificação

Build Release: zero avisos/erros. Cinco testes novos passaram numa execução focada e novamente na suíte completa (não somados em duplicidade). **236 testes .NET distintos aprovados: 164 unitários + 72 integração; 13 testes Python aprovados**, zero falhas/ignorados. PostgreSQL 17 e RabbitMQ reais; servidor PostgreSQL nativo encerrado após a execução. Relatórios TRX/Cobertura/OpenCover em TestResults/quality-m1 (ignorados).

| Assembly | Linhas | Cobertura |
|---|---:|---:|
| Contracts | 26/26 | 100,00% |
| API | 427/448 | 95,31% |
| Application | 345/345 | 100,00% |
| Domain | 370/374 | 98,93% |
| Infrastructure | 2511/2532 | 99,17% |

Gate >=80% por assembly aprovado. Metadata/schema foram verificados pelo host ASP.NET real de teste; não é evidência de deploy nem inspeção visual de uma imagem reconstruída. Reiniciar F5 para atualizar a documentação no Scalar local.

Não foram alterados contratos CLR/URN de mensageria, migrations, regras financeiras ou pipeline atômico. Não houve commit/push/publicação/deploy pelo agente. Próximo checkpoint: M2, composição de dependências e configuração tipada com comportamento atual preservado.
