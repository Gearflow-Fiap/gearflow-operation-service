# ADR-013 — Entrega isolada do Operation

Data: 29/10/2026. Estado: fundações locais implementadas; deploy pendente.

## Contexto

A infraestrutura clonada é da fase 3: EKS/Kong no namespace gearflow e SQL Server Express para o monólito. O Operation usa PostgreSQL próprio. Não reutilizamos a conexão ou os secrets do monólito.

## Decisão

Uma imagem .NET 10 multi-stage contém API e ferramenta de migration em diretórios separados. Executa sem root; API usa filesystem somente leitura e /tmp temporário. Migrations são job explícito antes do rollout. O Compose local usa PostgreSQL 17 sem porta publicada, credenciais ignoradas pelo Git e API apenas em loopback:5085.

Kustomize base contém somente Deployment/Service/ConfigMap próprios no namespace existente. O overlay Kong opcional registra /api/operation e /api/catalog/jobs; não assume o prefixo amplo /api do monólito. A migration tem job e credencial próprios. O cluster precisa prover PostgreSQL, secrets, pull credential e políticas/rede antes do deploy. Recursos de CPU/memória são valores iniciais, sem ensaio de carga.

CI prepara testes, contratos, cobertura, imagem/smoke e renderização offline. Sonar é habilitado quando o projeto/backend/token são configurados. O workflow de entrega exige CI bem-sucedida no push main deste repositório e DELIVERY_ENABLED=true; deploy exige DEPLOY_ENABLED=true, environment operation-homologation e AWS OIDC configurados. Imagens são referenciadas por digest. Não criamos recursos AWS nem ativamos esses controles remotamente.

## Consequências

Migration falha bloqueia rollout. Readiness recusa schema pendente. Rollback de imagem não desfaz schema; mudanças futuras precisam compatibilidade de leitura e estratégia expand/contract. Apenas migrations aditivas não dispensam revisão. O environment precisa de proteção configurada no GitHub; declarar o nome no YAML não cria revisores ou branch protection.

Validação local inclui imagem iniciada pelo usuário, migration exit 0, API saudável e smoke HTTP. Kustomize é renderizado offline; schema/admission, pull, OIDC, rollout e Sonar remotos permanecem pendentes. Nenhum apply ou publicação foi executado neste checkpoint.
