# API HTTP do Operation

Status: host e endpoints operacionais implementados. HTTP usa PostgreSQL real. Catálogo próprio e adapter HTTP Inventory implementados no 5B; cotação retorna 503 quando falta configuração, item ou dependência válida. Consumers/outbox de broker implementados; a integração coletiva com os outros serviços continua em homologação.

Base: /api/operation. Autenticação JWT HS256 + policy Staff em todos os endpoints abaixo.

| Método/rota | Entrada | Resultado |
|---|---|---|
| GET /queue | page, pageSize, status opcionais | página da fila acionável |
| GET /service-orders | page, pageSize, status, includeInactive opcionais | página de operações |
| GET /service-orders/{id} | GUID OS | estado atual e ETag forte da versão; 404 ausente |
| GET /service-orders/{id}/history | page/pageSize | histórico paginado |
| GET /durations | nenhuma | médias e amostras por fase |
| POST /service-orders/{id}/diagnostic/start | sem body | CommandResult e ETag |
| POST /service-orders/{id}/diagnostic/finish | {consumables:[{lineId,consumableId,quantity}]} | CommandResult com preços do catálogo/Inventory; 503 se cotação indisponível |
| POST /service-orders/{id}/diagnostic/fail | {reason} | falha de diagnóstico explícita |
| POST /service-orders/{id}/execution/start | sem body | início físico após autorização persistida |
| POST /service-orders/{id}/execution/jobs/{budgetJobId}/done | sem body | executa linha aprovada pelo Budget |
| POST /service-orders/{id}/execution/finish | sem body | conclusão após todas as linhas |

Escritas exigem Idempotency-Key GUID não vazio e If-Match com um único ETag forte positivo, ex.: "3". Sem If-Match: 428. Formato inválido, wildcard ou ETag fraco: 400. Versão divergente: 412. Estado/intenção conflitante: 409. Replay exato retorna resultado/ETag originais mesmo com versão atual posterior; GET consulta estado atual. Ator vem do sub autenticado com prefixo staff:, nunca do body. Respostas de escrita e consulta individual usam Cache-Control: no-store.

Enums são strings; propriedades desconhecidas no body são rejeitadas. Finish não aceita preços ou substituição de serviços/peças originais. Consumables=[] é válido; null/ausente não é. LineId precisa permanecer estável ao repetir uma intenção.

Não há endpoints HTTP para receber OS, autorizar execução, reservar estoque, aprovar orçamento, pagar ou compensar cancelamento. Esses passos dependem dos adapters internos de integração. Sem broker, recebimento/autorização só são exercitados via aplicação pelos testes; não é um fluxo completo para demonstração ainda.

Erros seguem application/problem+json com code e traceId, sem stack trace ou detalhe de JWT. 400 validação; 401 token ausente/inválido; 403 falta policy staff/sub; 404 ausência; 409 conflito; 412 versão; 428 precondição; 503 cotação indisponível; 500 erro interno.

OpenAPI gerado: GET /openapi/v1.json (Staff fora de Development; público em Development para a UI). Scalar: GET /scalar/v1 apenas em Development. As APIs de negócio continuam exigindo Staff em todos os ambientes. Liveness público: GET /health/live, sem afirmar prontidão do banco/broker. Catálogo /api/catalog/jobs implementado: veja CATALOG_AND_PRICING.md.

## Configurar e executar

Definir por ambiente/user-secrets, sem versionar segredos:

- ConnectionStrings__Operation: conexão PostgreSQL exclusiva.
- Jwt__Secret: chave HS256 com pelo menos 32 bytes; Jwt__Issuer e Jwt__Audience obrigatórios.
- Authorization__StaffClaimType e Authorization__StaffClaimValue: claim confiável emitida pela identidade. Proposta do grupo: actor_type=staff. Tokens atuais do monólito ainda precisam dessa evolução.
- Pricing__ConsumableMode: LegacyLinePriceV1 ou PerUnitV1, somente após acordo. Ausente/inválida deixa precificação bloqueada. O adapter composto usa catálogo local e GET Inventory; configurar Inventory__BaseUrl e Inventory__BearerToken para peças/insumos. Consulte CATALOG_AND_PRICING.md.

Executar migrations conforme POSTGRESQL.md e depois `dotnet run --project src/Operation.Api`. Não há migration automática na inicialização. Não copiar security_stamp como política de revogação remota nem consultar banco Identity.

A RFC-003 apresenta o padrão recomendado para a revisão coletiva. A API exige configuração explícita em vez de tratar qualquer JWT como staff.

Motivos de falha/cancelamento/desativação têm limite de 2000 caracteres na aplicação, alinhado aos contratos de mensagens. Sem alteração de estado quando a validação falha.


## Contrato executável da documentação (M1)

Todas as rotas /api/** possuem resumo, descrição, tipo de sucesso e erros aplicáveis em ProblemDetails. O esquema inclui exemplos de entrada/erro, paginação e headers. ETag e Cache-Control estão documentados onde o runtime os devolve; criação de catálogo também informa Location.

Os testes EndpointContractTests descobrem as rotas reais no EndpointDataSource e verificam documentação/policy Staff, chamadas anônimas/cliente sem efeitos, conteúdo OpenAPI e exemplos executáveis. A criação de catálogo não ganha Idempotency-Key/If-Match por causa da documentação; atualização/desativação exigem apenas If-Match. Os comandos de operação exigem os dois headers.

Os IDs nos exemplos são ilustrativos. Para comandos distintos, gere um novo Idempotency-Key; reutilize apenas numa repetição exata da mesma intenção. O exemplo de conclusão de diagnóstico com consumables vazio serve para casos sem insumos; preços de peças continuam dependendo do Inventory quando aplicável.

Plano de melhoria: [M1–M5](../planning/CODE_QUALITY_PLAN.md). Para ver as novas descrições/exemplos na imagem local, reinicie o perfil Docker Compose com F5.
