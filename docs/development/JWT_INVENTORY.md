# JWT e consulta ao Inventory

## Autenticação HTTP

As rotas de negócio e o OpenAPI fora de Development exigem JWT HS256 válido:
assinatura, issuer, audience e expiração são verificados. Não exigem role/claim
Staff nem formato GUID no sub. Qualquer identidade aceita pelo emissor configurado
pode acessar essas rotas, inclusive tokens com actor_type=customer.

O sub precisa ser único, não vazio e ter até 194 caracteres, para identificar
ações no histórico. O prefixo histórico staff: permanece apenas no identificador
gravado, para preservar fingerprints e replays de idempotência existentes; ele
não representa uma permissão. Sub ausente/ambíguo/inválido continua rejeitado.
If-Match e Idempotency-Key continuam obrigatórios nas mesmas escritas.

## Kubernetes / Academy

O overlay k8s/overlays/integration, herdado pelos perfis Academy, configura:

```yaml
Inventory__BaseUrl: http://gearflow-inventory.gearflow.svc.cluster.local:8080/
Inventory__AllowInsecureHttp: "true"
```

O deploy executa scripts/configure_inventory.py depois de configure_academy.py.
Ele adiciona Inventory__BearerToken ao Secret gearflow-operation-secret sem
alterar as outras chaves. A identidade do token é gearflow-operation-service;
o token do usuário não é encaminhado. Nenhuma chave/token é versionada ou impressa.

GitHub Actions, environment academy:

| Configuração | Origem / padrão |
|---|---|
| INVENTORY_JWT_SIGNING_KEY | Secret com a chave aceita pelo Inventory; na ausência usa JWT_SIGNING_KEY, somente se os serviços compartilham a chave |
| INVENTORY_JWT_ISSUER | Variable; GearFlow.Api |
| INVENTORY_JWT_AUDIENCE | Variable; GearFlow.Client |
| INVENTORY_TOKEN_TTL_MINUTES | Variable; 10080 (7 dias), limite 43200 (30 dias) |

O token expira. Renovar antes de 7 dias (ou do TTL configurado), reexecutando
o workflow de deploy ou o script com as variáveis acima e reiniciando o Operation:

```powershell
python scripts/configure_inventory.py
kubectl -n gearflow rollout restart deployment/gearflow-operation
kubectl -n gearflow rollout status deployment/gearflow-operation
```

O script exige a chave em variável de ambiente; não passar a chave na linha de
comando nem colar em arquivos versionados. Alterar Secret via envFrom exige novos
pods. A renovação não é automática entre deploys; agendar a rotação operacional
antes da expiração. O script funciona com kubectl no PATH em Windows e Linux.

Após implantação, repetir diagnostic/finish da OS pendente com um ETag atual e
nova Idempotency-Key, incluindo um insumo real. Esperar geração do orçamento,
aprovação, reserva, pagamento, autorização da execução e consumo do estoque.
Readiness saudável isoladamente não comprova acesso HTTP Operation → Inventory.

Esta decisão substitui as exigências Staff/GUID da ADR-009 e dos documentos
históricos que descrevem a política anterior. A implantação continua separada
da edição do repositório.
