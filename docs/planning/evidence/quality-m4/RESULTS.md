# M4 — persistência e convenções

Concluído localmente em 29/10/2026. Baseline: f0a52e28587219c6ef6414298244607a170ff3dc. Sem commit, push ou deploy executado pelo agente.

## Implementação

Seis IEntityTypeConfiguration em Persistence/Configurations, aplicadas explicitamente por OperationDbContext. Schema e convenção snake_case permanecem no contexto. Quatro migrations e ModelSnapshot anteriores intactos; não foi criada migration.

Directory.Packages.props centraliza 24 versões diretas existentes. PackageReference preserva sua camada e PrivateAssets; overrides locais são proibidos. A versão própria de GearFlow.Contracts.Operation permanece inalterada. .editorconfig e AnalysisLevel=10.0 estabelecem convenções/analyzers; gate de whitespace incremental nos oito arquivos do M4, com IDE0055 warning nas seis configurações. .gitattributes fixa LF no escopo para checkouts Windows/Linux. Pasta virtual build da solução expõe as configurações centrais no Visual Studio.

Dockerfile copia Directory.Packages.props e .editorconfig junto de Directory.Build.props antes do restore. Pipeline CI inclui o mesmo gate incremental de whitespace. Debugger Compose, DLL publicada e migration one-shot preservados.

## Equivalência e testes

Antes da extração, uma ferramenta temporária fora do repositório referenciou Infrastructure e capturou o modelo de design completo, GenerateCreateScript e GetCreateScript do histórico EF, sem conexão de banco. Referências em tests/Operation.UnitTests/Infrastructure/Fixtures. ModelContractTests confirma equivalência após normalizar apenas quebras/espaços de fim de linha; verifica também HasPendingModelChanges=false e quatro migrations. Isso cobre annotations/concurrency que não aparecem apenas no DDL, além do histórico em public.__EFMigrationsHistory.

Comparação de project.assets.json antes/depois nos nove projetos confirmou todas as versões e SHA512 resolvidas, incluindo transitivas. [Hashes dos grafos por projeto](package-graphs.json). Nenhum upgrade de dependência nesta etapa.

- Restore da solução aprovado.
- Build Release dos nove projetos .NET: zero erros/avisos.
- Quatro testes focados de modelo aprovados, incluídos no total final, sem somar reexecuções.
- Suíte final: **294 testes .NET distintos** (168 unitários + 126 integração), zero falhas/ignorados.
- **13 testes Python aprovados**, incluindo override do debugger e preservação de credenciais locais.
- PostgreSQL 17 e RabbitMQ reais, bancos/vhosts exclusivos dos testes; regressão de HTTP/Scalar, idempotência/CAS/transação, inbox/outbox, migrations e OTLP aprovada.
- Gate >=80% por assembly aprovado: Contracts 100%; API 95,79%; Application 100%; Domain 98,93%; Infrastructure 99,23%.
- Gate de whitespace aprovado com dotnet format em modo pasta. O modo solução encontrou restrição de pipe do ambiente; o modo pasta evita o workspace MSBuild e verifica as mesmas regras de whitespace. Build continua responsável pelos analyzers semânticos.
- Restore da API e do migrador aprovado em diretório isolado contendo somente os arquivos COPY anteriores ao primeiro RUN do Dockerfile. Publish Release de ambos aprovado, com UseAppHost=false.
- dotnet sln list reconhece a solução atualizada; git diff --check aprovado e diff de Migrations vazio.

TRX e cobertura: TestResults/quality-m4, ignorados pelo Git. [Resumo estruturado](validation.json). [Guia](../../../development/BUILD_AND_PERSISTENCE.md).

## Limites e retomada

A imagem Docker não foi reconstruída pelo agente, que não tem acesso ao pipe do daemon. Restore isolado e publish não equivalem à execução da imagem; F5 pode reconstruí-la localmente. O CI configurado contém build/smoke Docker, mas não foi executado remotamente nesta etapa. OS/Saga, integração coletiva e entrega remota continuam pendentes no 7B.

Próxima etapa: **M5 — avaliar handlers**. Avaliar responsabilidades/dependências e registrar decisão; somente extrair handlers ou adotar MediatR se houver benefício verificável, preservando o pipeline transacional único.
