# ADR-010 — Catálogo próprio e cotação HTTP

Status: implementado no incremento 5B do Operation; homologação e credencial do ambiente entre responsáveis ainda necessárias.

## Catálogo

Operation é dono do catálogo de mão de obra. Tabela operation.catalog_jobs no seu PostgreSQL, com preço inteiro em centavos, nome/descrição limitados, estado ativo, versão otimista e metadados de alteração. Nova migration acrescenta tabela/índice sem alterar snapshots de operações existentes. Não há leitura do banco do monólito nem importação automática dos seus dados.

CRUD autenticado por Staff: criação retorna 201/Location; atualização e desativação exigem If-Match; desativação lógica não apaga snapshots. Catálogo tem ciclo de vida independente dos comandos operacionais: POST de criação não oferece journal de idempotência; não repetir automaticamente após resposta ambígua sem consultar cadastro. Não prometer replay idempotente onde não existe. Manutenção por Staff é baseline local da fase 4; permissões mais específicas dependem do acordo de identidade.

## Fonte de preços

CompositePriceCatalog lê mão de obra ativa no banco próprio e consulta InventoryPriceClient para peças/insumos. Usa contratos GET já presentes no repositório Inventory: /api/inventory/parts/{id} com priceCents; /consumables/{id} com unitPriceCents. Não inventa endpoint batch, não compartilha EF e não consulta banco remoto. isActive=false é rejeitado quando a API incluir esse campo; no contrato atual a ausência é aceita e item inexistente deve retornar 404.

Resposta confere ID, nome e preço inteiro não negativo; preço zero explícito é permitido. Item ausente/inativo, JSON inválido, autenticação recusada, redirecionamento ou indisponibilidade nunca viram preço zero. Resposta limitada a 64 KiB, timeout HTTP de 5s, prazo total da cotação de 10s, uma consulta em voo por cotação e cancelamento propagado. Não faz retry HTTP automático. Load do catálogo e rede ocorrem antes da transação curta de commit operacional; concorrência final continua protegida pelo CAS.

Credential é obtida de IInventoryCredential (implementação atual: configuração do ambiente), sem encaminhar JWT do funcionário. HttpClient não segue redirects. HTTPS é padrão fora de loopback; HTTP interno de demonstração exige Inventory:AllowInsecureHttp=true explícito. Não há emissão/renovação automática de JWT neste serviço. A credencial deve ser válida no Inventory; policy restrita de serviço e emissor ainda dependem dos responsáveis por identidade/Inventory. Token do teste não é credencial de produção.

## Política financeira

Configuração obrigatória Pricing:ConsumableMode. Recomendação local para novos dados: PerUnitV1, correspondente a per-unit-away-from-zero-v1. A alternativa LegacyLinePriceV1 permanece explícita para compatibilidade, sem recalcular histórico. Ausente/inválida retorna 503 no fechamento. Não foi alterado Budget para fingir acordo coletivo.

Snapshot resultante congela moeda BRL, linhas, preços e política; pricedAt indica término da leitura, não transação distribuída entre catálogos. Mudança posterior do catálogo não altera diagnóstico. Falha de preço mantém diagnóstico aberto; usuário pode repetir intenção sem receipt/evento parcial. Replay após sucesso não consulta preços novamente.

## Verificação e limites

Unitários verificam contratos, credencial, dados inválidos, ausência/erro, cancelamento e timeout real do HttpClient. Integração usa PostgreSQL e HTTP loopback real com servidor substituto que reproduz DTOs do Inventory: não é execução do serviço Inventory completo. Migrations up/down e upgrade com operação já existente são exercitados. Homologação com Inventory/Budget reais, política do emissor, broker e Saga permanecem próximos trabalhos.
