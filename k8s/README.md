# Operation no EKS existente

## Perfil de integraÃ§Ã£o OSâ€“Operation

`kubectl kustomize k8s/overlays/integration` renderiza o perfil com consumo e despacho
habilitados, broker `rabbitmq.messaging.svc.cluster.local`, vhost `gearflow` e label
de cliente RabbitMQ no pod. NÃ£o aplica recursos. Fornecer RabbitMq__Username e
RabbitMq__Password individuais no Secret gearflow-operation-secret e conferir
overrides desse Secret para host/vhost/flags. O namespace precisa da autorizaÃ§Ã£o
prevista na NetworkPolicy real (`gearflow.io/messaging-access=true`). O perfil nÃ£o
cria credenciais nem altera polÃ­ticas do broker. Preparar imagem e banco antes do deploy.

Manifests preparados e renderizados offline; nenhum recurso aplicado. Namespace existente: gearflow. A infraestrutura SQL Server da fase 3 nÃ£o atende o PostgreSQL do Operation.

PrÃ©-requisitos do grupo:

- PostgreSQL 17 exclusivo e conectividade a partir dos pods; usuÃ¡rio de aplicaÃ§Ã£o e usuÃ¡rio de migration com privilÃ©gios adequados.
- Secret gearflow-operation-secret: ConnectionStrings__Operation, Jwt__Secret, Jwt__Issuer, Jwt__Audience; Inventory__BaseUrl/BearerToken e RabbitMq__Host/Port/VirtualHost/Username/Password quando habilitados; OTLP headers quando habilitado.
- Secret gearflow-operation-migrations: ConnectionStrings__Operation do usuÃ¡rio de migration.
- imagePullSecret gearflow-operation-registry para GHCR privado; imagem aprovada por digest.
- Revisar claims Staff, polÃ­tica de consumÃ­veis, recursos, TLS, egress e topologia de broker. Flags de broker e exportaÃ§Ã£o OTLP comeÃ§am false.

```powershell
kubectl kustomize k8s/base
kubectl kustomize k8s/overlays/kong
python scripts/prepare_release.py ghcr.io/organizacao/repositorio@sha256:<digest-real>
kubectl kustomize k8s/overlays/release
```

Renderizar nÃ£o valida admissÃ£o nem aplica. Job de migration em migration-job.yaml deve usar o mesmo digest; esperar seu sucesso antes do rollout. Delivery workflow prepara essa sequÃªncia quando habilitado. O overlay release gerado Ã© ignorado pelo Git. Overlay Kong Ã© opcional e exige plugins gearflow-jwt e gearflow-rate-limit existentes; nÃ£o Ã© aplicado pelo workflow base. Revisar conflitos de rotas com o monÃ³lito na homologaÃ§Ã£o.

Liveness/startup nÃ£o dependem do banco. Readiness exige schema atualizado; sem secret/banco nÃ£o hÃ¡ serviÃ§o pronto. Base nÃ£o cria namespace, banco, broker, credenciais ou recursos AWS. ReversÃ£o de imagem nÃ£o reverte migration. NÃ£o usar placeholders como release em um cluster real.

## Academy

Perfil com os secrets GitHub existentes: [instruções](academy/README.md).
