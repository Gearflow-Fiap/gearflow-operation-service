# Incremento 5B — catálogo e adapter de preços

Concluído localmente em 29/10/2026. Homologação com serviços completos e credenciais do grupo ainda pendente.

## Entrega

- IJobCatalog/JobDefinition e implementação PostgreSQL exclusiva do Operation.
- Migration AddLaborCatalog; catálogo versionado, manutenção Staff, desativação lógica, ETag/If-Match e OpenAPI.
- CompositePriceCatalog: mão de obra local + GET Inventory existentes, sem banco compartilhado.
- IInventoryCredential configurada externamente, sem encaminhar token do funcionário; redirects desativados; transporte HTTPS ou HTTP de desenvolvimento explícito.
- Validação de ID/nome/preço, limite de resposta, cancelamento, timeout por chamada e prazo total da cotação; nenhuma falha vira preço zero.
- Política de consumíveis explícita; ausência bloqueia fechamento. Proposta PerUnitV1 para novos dados, sem modificar Budget ou preços históricos.
- Snapshot e replay persistidos continuam protegidos; mudança no catálogo não muda diagnóstico fechado.

## Execução

Build/testes Release via:

```text
dotnet test GearFlow.Operation.slnx --no-restore --configuration Release -m:1 -nr:false --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/increment-5b-accepted --logger trx
python scripts/check_coverage.py TestResults/increment-5b-accepted
```

155 testes unitários + 43 de integração = **198 aprovados**, 0 falhas, 0 ignorados.

| Assembly | Linhas cobertas/instrumentadas | Cobertura |
|---|---|---|
| Operation.Api | 190/193 | 98,45% |
| Operation.Application | 312/312 | 100% |
| Operation.Domain | 370/374 | 98,93% |
| Operation.Infrastructure | 1405/1421 | 98,87% |

Gate >=80% por assembly aprovado. A cobertura de Infrastructure inclui migrations/modelos gerados. Exclusão única de arquivo gerado de biblioteca OpenApiXmlCommentSupport.generated.cs, conforme runsettings já utilizada no 5A. Nenhum código escrito do serviço excluído. TRX/Cobertura em TestResults local ignorado, artifacts na próxima execução CI. CI remota não executada neste trabalho.

## Cenários

CRUD e paginação reais; Staff; preço zero explícito e limites; conflito de versão; concorrência entre conexões; desativação; migrations up/down e upgrade preservando operação existente. HTTP loopback real testa composição de mão de obra/peças/insumos, credencial transportada, total per-unit, snapshot preservado após edição/desativação, replay sem recotar e 401/404/503 sem gravação parcial. Mão de obra sem Inventory funciona com configuração de política e catálogo local. Unitários cobrem respostas inválidas, preço divergente/ausente, erros de rede, cancelamento e timeout efetivo do HttpClient.

PostgreSQL 17.11 real em instância temporária isolada. O servidor HTTP usado como Inventory é substituto de teste com DTOs verificados no repositório; não executa regras, autenticação JWT ou banco do Inventory completo. Essa distinção é necessária para não confundir teste do adapter com homologação entre serviços.

## Limites e retomada

Configurar banco/JWT/Staff, migrations, Pricing:ConsumableMode, Inventory:BaseUrl e Inventory:BearerToken conforme docs/development/CATALOG_AND_PRICING.md. Sem emissão/renovação de credencial neste serviço; policy restrita no Inventory e emissor ainda dependem do grupo. Dados antigos do monólito não foram importados; novas OS devem referenciar IDs do catálogo novo. Importação de ordens antigas exige plano específico de preservação de referências.

Não foram alterados gearflow-app, Inventory, Budget ou infra-broker. Sem publicação/deploy/commit. Broker, consumers, inbox de transporte, dispatcher da outbox, Saga e BDD distribuído continuam pendentes.

Próximo incremento 6: revisar contratos com OS/Budget/Inventory, definir dono da OS/Saga (OS separado recomendado), disponibilizar infra-broker, implementar MassTransit/RabbitMQ e homologar serviços reais com suas credenciais. Não declarar fase 4 pronta antes dessa integração.
