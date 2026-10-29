# Organização do código

```text
docker-compose.dcproj                 # Startup Project no Visual Studio
GearFlow.Operation.slnx                # Solução completa, incluindo Compose
GearFlow.Operation.CI.slnf             # Todos os projetos .NET, sem SDK Docker/IDE
src/
  Operation.Domain/
    Common/ Diagnostics/ Execution/ Operations/ Orders/
  Operation.Application/
    DependencyInjection.cs
    Abstractions/Persistence/
    Catalog/ Diagnostics/ Observability/
    Operations/Commands/               # Casos de uso e pipeline atômico
    Operations/Queries/                # Consultas e projeções
    Operations/Queue/                  # Fila e durações
  Operation.Infrastructure/
    Configuration/ Messaging/ Migrations/ Persistence/ Pricing/
    Persistence/Configurations/         # Um IEntityTypeConfiguration por entidade
    DependencyInjection.cs
  Operation.Api/
    Authentication/ Composition/ Configuration/ Errors/ Health/ HostedServices/ Http/
    Endpoints/Catalog/ Endpoints/Operations/ Observability/ OpenApi/
    Program.cs Dockerfile
  Operation.Contracts/                 # Wire público versionado
tests/
  Directory.Build.props                # Configuração comum dos projetos de teste
  Operation.UnitTests/
    Architecture/ Application/ Contracts/ Domain/ Infrastructure/ Support/
  Operation.IntegrationTests/
    Api/ Delivery/ Messaging/ Persistence/ Support/
  python/
tools/
  Operation.ContractArtifacts/ Operation.Migrate/
docs/ k8s/ scripts/
```

Program.cs é o composition root. Endpoints traduzem HTTP; Application coordena casos de uso; Domain preserva invariantes; Infrastructure implementa persistência, integração HTTP e mensageria. Contratos públicos permanecem separados e com o mesmo namespace/URN. A reorganização física mantém namespaces públicos de produção para evitar alterações desnecessárias nos consumers e adapters.

OperationService possui partes por recebimento, diagnóstico, execução e administração. O arquivo Pipeline mantém uma única implementação de replay/idempotência, controle de versão, telemetria, receipt e commit de estado/outbox. Não duplicar esse fluxo dentro de endpoints/consumers. As partes são uma classe de aplicação; não criam transações independentes por arquivo.

MediatR foi avaliado e não adicionado neste checkpoint. A API já separa comandos e consultas, e o pipeline atual é explícito e testado. Adicionar um mediator apenas para encaminhar chamadas não melhora as fronteiras atuais. Se o número de handlers ou comportamentos justificar, migrar depois com testes de equivalência do pipeline, sem alterar contratos de integração.

[Execução Visual Studio](VISUAL_STUDIO.md) e [mapa dos testes](../../tests/README.md).

[Composição DI e configuração tipada](CONFIGURATION.md): responsabilidades, lifetimes e condições de ativação das integrações.

[Entrada HTTP](HTTP_INPUT.md): validação de requests, ator autenticado e ETag compartilhado, preservando as regras distintas de catálogo e comandos.

[Persistência e convenções de build](BUILD_AND_PERSISTENCE.md): configurações EF, versões NuGet centrais, referências do modelo e gate de formatação incremental.
