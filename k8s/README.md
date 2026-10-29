# Operation no EKS existente

Manifests preparados e renderizados offline; nenhum recurso aplicado. Namespace existente: gearflow. A infraestrutura SQL Server da fase 3 não atende o PostgreSQL do Operation.

Pré-requisitos do grupo:

- PostgreSQL 17 exclusivo e conectividade a partir dos pods; usuário de aplicação e usuário de migration com privilégios adequados.
- Secret gearflow-operation-secret: ConnectionStrings__Operation, Jwt__Secret, Jwt__Issuer, Jwt__Audience; Inventory__BaseUrl/BearerToken e RabbitMq__Host/Port/VirtualHost/Username/Password quando habilitados; OTLP headers quando habilitado.
- Secret gearflow-operation-migrations: ConnectionStrings__Operation do usuário de migration.
- imagePullSecret gearflow-operation-registry para GHCR privado; imagem aprovada por digest.
- Revisar claims Staff, política de consumíveis, recursos, TLS, egress e topologia de broker. Flags de broker e exportação OTLP começam false.

```powershell
kubectl kustomize k8s/base
kubectl kustomize k8s/overlays/kong
python scripts/prepare_release.py ghcr.io/organizacao/repositorio@sha256:<digest-real>
kubectl kustomize k8s/overlays/release
```

Renderizar não valida admissão nem aplica. Job de migration em migration-job.yaml deve usar o mesmo digest; esperar seu sucesso antes do rollout. Delivery workflow prepara essa sequência quando habilitado. O overlay release gerado é ignorado pelo Git. Overlay Kong é opcional e exige plugins gearflow-jwt e gearflow-rate-limit existentes; não é aplicado pelo workflow base. Revisar conflitos de rotas com o monólito na homologação.

Liveness/startup não dependem do banco. Readiness exige schema atualizado; sem secret/banco não há serviço pronto. Base não cria namespace, banco, broker, credenciais ou recursos AWS. Reversão de imagem não reverte migration. Não usar placeholders como release em um cluster real.
