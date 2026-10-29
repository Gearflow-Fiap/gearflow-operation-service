# Catálogo e consulta de preços — incremento 5B

## Configuração

O host registra PostgresJobCatalog + CompositePriceCatalog + InventoryPriceClient. O catálogo bloqueado do 5A foi removido. Para novos diagnósticos da fase 4, proposta local explícita:

```powershell
$env:Pricing__ConsumableMode='PerUnitV1'
$env:Inventory__BaseUrl='https://inventory.seu-ambiente/'
# Fornecer Inventory__BearerToken pelo ambiente/secret manager; não versionar o token.
```

BaseUrl deve apontar para a raiz da API (o adapter acrescenta api/inventory/...). HTTP loopback é aceito em desenvolvimento. Para endereço HTTP interno de Compose/K8s de demonstração, configurar Inventory__AllowInsecureHttp=true explicitamente; caso contrário, usar HTTPS. O broker não participa da consulta de preços.

Demais configurações de banco/JWT/staff estão em API_ENDPOINTS.md. Pricing__ConsumableMode ausente/inválido mantém fechamento bloqueado com 503. Escolha explícita entre PerUnitV1 e LegacyLinePriceV1; Budget precisa validar o pricingPolicyId do snapshot. Não inferir regra pelo nome do item ou quantidade.

Inventory__BearerToken é credencial de integração fornecida externamente, não token recebido do navegador. Implementação atual não emite nem renova tokens: configurar uma credencial aceita pelo Inventory e renovar antes da expiração (variáveis de ambiente exigem reiniciar processo para atualização). A proposta de actor_type=service/audience/scope restritos está na RFC-003 e precisa de suporte do emissor e Inventory. Não afirmar que essa autorização restrita já foi implementada nos outros repositórios.

## Rotas do catálogo

Staff autenticado, base /api/catalog/jobs:

| Método | Rota | Regra |
|---|---|---|
| GET | /?page=1&pageSize=20&includeInactive=false | página ordenada por nome/id |
| GET | /{id} | item, versão e ETag; inclui inativo |
| POST | / | {name,description,priceCents}; 201/Location/ETag |
| PUT | /{id} | mesmo body; If-Match forte obrigatório; 200 com nova versão |
| DELETE | /{id} | If-Match obrigatório; desativação lógica com nova versão |

Nome obrigatório até 128 caracteres; descrição até 2000; preço em centavos inteiro >=0. Mesmo JWT/policy Staff dos endpoints operacionais. PUT não reativa item desativado. If-Match ausente=428, inválido=400, antigo=412. Atualizações concorrentes só permitem uma gravação por versão. POST não promete replay idempotente: duas criações podem gerar dois IDs; não fazer retry automático após resposta ambígua. Journal de Idempotency-Key é dos comandos operacionais, não do CRUD do catálogo.

Executar nova migration AddLaborCatalog usando o guia PostgreSQL. Não altera dados operacionais existentes. Não importa catálogo do gearflow-app: novos pedidos devem usar IDs do catálogo novo. Migração de ordens/dados antigos exige plano separado para preservar referências; não trocar IDs existentes silenciosamente.

## Cotação e falhas

Mão de obra vem apenas do banco Operation. Peças usam GET /api/inventory/parts/{id} e priceCents; consumíveis usam GET /api/inventory/consumables/{id} e unitPriceCents. Nome, ID e valor são verificados. Item ausente/inativo ou resposta malformada falha explicitamente. Zero explícito é permitido; preço ausente nunca é convertido em zero.

Prazo de 5s por request e 10s para cotação completa; uma chamada em voo por cotação, sem retry automático e sem seguir redirect. Limite de 64 KiB por resposta. Cancelamento do request é propagado. Cotação ocorre antes da transação de commit; falha não gera novo estado, receipt ou outbox. Ausência de mão de obra ativa também bloqueia fechamento. Só mão de obra não exige credencial Inventory.

Após fechar, alterações/desativação no catálogo e falhas posteriores no Inventory não alteram o snapshot. Replay usa resultado persistido sem recotar. Não há reserva de estoque nesta consulta; verificar preço não promete disponibilidade/reserva.

## Validação e homologação

Testes combinam PostgreSQL real e servidor HTTP loopback substituto que reproduz DTOs encontrados no Inventory. Validam composição, credencial enviada, snapshot, replay e falhas sem gravação parcial. Isso não comprova deploy conjunto, compatibilidade do token real ou configuração do Inventory no ambiente do grupo. Próximo checkpoint de mensageria deve incluir homologação entre serviços reais e suas credenciais.
