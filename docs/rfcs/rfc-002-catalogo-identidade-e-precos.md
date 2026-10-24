# RFC-002: Catálogo, clientes, identidade e preços

**Status:** Draft. **Date:** 2026-10-24. **Reviewers:** Operation, Inventory, OS, Budget, responsável pelo monólito/Lambda e frontend.

## Summary

Extrair Catalog para Operation; manter Identity/Customers temporariamente no monólito; congelar preços no diagnóstico e no orçamento. Não acessar bancos alheios.

## Motivation

Monólito possui Identity, Customers, Catalog, Inventory, Workshop e Notifications. A divisão em quatro serviços não diz sozinha onde ficam os contextos de suporte. SQL cross-context não é válido na fase distribuída.

## Proposal

### Catálogo e preços

- Operation oferece mão de obra (Job: nome, descrição, preço em centavos), no mesmo deploy mas com regras separadas do agregado operacional.
- Inventory continua dono de peças/insumos e seus preços. Rotas atuais GET /api/inventory/parts/{id} e /consumables/{id} já devolvem preço e nome; podem servir de fallback inicial com paralelismo limitado e timeout.
- Endpoint em lote é proposta pendente: POST /api/inventory/prices/query, body Items com ItemType/ItemId; resposta Items com nome, preço unitário em centavos e instante de leitura. Não está implementado nem altera os contratos atuais. Autenticação de serviço precisa de definição.
- Se qualquer item estiver ausente/inativo, falhar explicitamente; nunca usar GetValueOrDefault para gerar preço zero. Preço zero só é válido quando explicitamente cadastrado.
- DiagnosticFinished contém snapshot de preços e quantidades; Budget valida/calcula total com a mesma política. StartExecution devolve exatamente as linhas aprovadas. Sem substituições silenciosas, descontos ou revisões nesta v1.
- Quantity decimal: mão de obra 1 por linha, peças inteiras positivas, insumos positivos com até 3 casas. UnitPriceCents inteiro >=0; moeda BRL. LineTotalCents = arredondar Quantity*UnitPriceCents para inteiro com midpoint away from zero; soma dos totais de linha em aritmética checada. Limites de armazenamento devem impedir overflow e pagamento fora do limite do provedor.
- Atenção à migração: Budget atual do monólito soma PriceCents dos insumos sem multiplicar Quantity. A nova regra proposta é mudança explícita de comportamento; verificar dados/semântica do legado e cenários fracionários antes de adotar. Não corrigimos valores históricos automaticamente.

### Clientes e veículos

Identity/Customers permanecem no monólito durante transição. OS recebe IDs e valida existência por API quando abre a ordem; guarda só snapshot necessário ao processo. Operation recebe VehicleId, sem CPF, endereço ou e-mail. OS fornece nome/e-mail ao Budget quando necessário. Consulta detalhada de veículo pode usar API autorizada, sem replicar cadastro mutável inteiro em eventos.

### Autenticação e autorização

Inventory valida JWT por assinatura/issuer/audience/expiração. Monólito também usa security_stamp por leitura Identity; isso não pode ser copiado para outro banco. Reutilizar validação criptográfica e definir política de revogação por API/cache ou token curto, sem consulta direta ao banco Identity.

Proposta: distinguir staff de customer nas claims/policies; Operation exige staff e não apenas qualquer JWT. Cliente autenticado por CPF não pode iniciar reparos. O emissor ainda precisa fornecer claim confiável (p.ex. actor_type=staff); claim exata e migração de tokens devem ser acordadas, não presumidas existentes. JWT nunca vai no payload RabbitMQ. Integração de preços exige credencial de serviço restrita, a definir com infraestrutura; segredo não versionado.

Não criar RBAC amplo/novo provedor nesta etapa sem requisito; políticas mínimas por tipo de ator já precisam ser explícitas. Gateway controla roteamento/CORS/rate limit; APIs continuam validando acesso. Avaliar Lambda no repositório real antes de alterar seu contrato.

## Alternatives

Catalog separado: melhor autonomia se houver escala/owner próprio, mas cria serviço extra agora. Budget dono do catálogo: confunde cobrança com operação. Snapshot único sem consulta de preço: só é válido se vier de fonte confiável identificada; preço do navegador não basta.

## Risks & Open Questions

Confirmar dono do catálogo, regra de total legado/insumos, precisão e limites do Mercado Pago, claims emitidas, revogação de staff, autenticação service-to-service e segregação de filiais. Mudanças comerciais de orçamento requerem nova revisão e nova autorização; v1 não contempla múltiplos ciclos por OS.

## Decision

Pendente. Só implementar dependências externas depois de fixar contrato e política correspondentes.
