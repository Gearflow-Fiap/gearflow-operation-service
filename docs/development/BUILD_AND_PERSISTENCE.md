# Persistência, pacotes e convenções de build

## Configurações EF

OperationDbContext mantém DbSets, schema operation e a convenção de colunas snake_case. Os mapeamentos estão em Persistence/Configurations, cada um implementando IEntityTypeConfiguration<T>:

| Configuração | Tabela |
|---|---|
| BrokerInboxConfiguration | broker_inbox |
| JobConfiguration | catalog_jobs |
| OperationConfiguration | operations |
| HistoryConfiguration | histories |
| ReceiptConfiguration | command_receipts |
| OutboxConfiguration | outbox_events |

O contexto aplica explicitamente as seis configurações, na ordem anterior. Isso mantém o conjunto de entidades explícito e evita depender da ordem de descoberta por reflection. A convenção de colunas continua sendo aplicada após todos os mapeamentos.

Permanecem os nomes de tabelas/colunas/índices/constraints, conversões de enum para string, limites, nullability, tipos jsonb, tokens de concorrência, chaves e relacionamentos com DeleteBehavior.Restrict. O histórico EF continua em public.__EFMigrationsHistory, configurado na factory. As quatro migrations e seu ModelSnapshot não foram alterados.

### Verificação do modelo

ModelContractTests usa três referências em Operation.UnitTests/Infrastructure/Fixtures, capturadas no código anterior ao M4 sem conexão de banco: modelo de design EF completo, SQL de criação das tabelas e SQL da tabela de histórico. Compara o resultado atual, normalizando apenas quebras de linha e whitespace no fim das linhas. O modelo inclui flags de concorrência e annotations que não aparecem no DDL. Outro teste verifica HasPendingModelChanges=false e a presença das quatro migrations existentes.

As referências são um contrato de regressão, não arquivos para atualizar automaticamente quando um teste falhar. Uma mudança real de modelo exige revisão da alteração e migration apropriada; depois revisar e atualizar as referências. Não alterar migrations antigas para acomodar reorganização de código.

## Versões NuGet

A pasta virtual build da solução expõe Directory.Build.props, Directory.Packages.props, .editorconfig e .gitattributes no Visual Studio.

Directory.Packages.props centraliza as **24 versões diretas existentes**, via Central Package Management. PackageReference fica no projeto/camada que utiliza o pacote, preservando PrivateAssets e demais metadados. As referências comuns de teste continuam em tests/Directory.Build.props, sem versões locais.

ManagePackageVersionsCentrally está ativo e VersionOverride desativado. NuGet rejeita referências com Version local, pacote sem versão central ou override proibido durante restore. Esta etapa não habilita pinning transitivo nem atualiza pacotes. A comparação antes/depois confirmou todas as versões e hashes SHA512 das dependências resolvidas nos nove projetos.

A versão do pacote próprio GearFlow.Contracts.Operation permanece no seu csproj; ela representa o contrato publicado e não é a versão de uma dependência. Atualizações futuras de dependências devem ser explícitas em Directory.Packages.props e passar pela regressão.

## Docker e Visual Studio

O Dockerfile copia Directory.Build.props, Directory.Packages.props e .editorconfig antes do primeiro restore. O contexto continua sendo a raiz do repositório. Alterar pacotes centrais invalida a camada de restore, e o publish recebe as mesmas convenções da execução local/CI.

Foi verificado o restore da API e do migrador numa pasta isolada contendo apenas os arquivos COPY anteriores ao primeiro RUN do Dockerfile, além do publish Release de ambos. Isso verifica a disponibilidade dos arquivos de build sem depender do daemon Docker; não substitui uma reconstrução da imagem. O perfil F5, caminho da DLL, override do debugger e migration one-shot permanecem iguais.

## Convenções incrementais

.editorconfig define UTF-8, newline final, LF, espaços e quatro posições em C# (duas em XML/JSON/YAML). Namespace file-scoped, braces e ordenação de usings são recomendações gerais. Directory.Build.props fixa AnalysisLevel=10.0 para csproj, mantém analyzers e habilita code style no build; warnings continuam erros.

IDE0055 (formatação) tem severidade warning apenas nas seis configurações extraídas. No código anterior permanece suggestion. O CI também verifica whitespace nos oito arquivos do escopo M4: DbContext, seis configurações e ModelContractTests. Não foi aplicada formatação em massa. .gitattributes fixa LF nesses arquivos/referências e nos novos arquivos de configuração necessários, evitando divergência do gate entre checkout Windows/Linux.

```powershell
dotnet restore GearFlow.Operation.CI.slnf
dotnet format whitespace . --folder --verify-no-changes --include src/Operation.Infrastructure/Persistence/OperationDbContext.cs src/Operation.Infrastructure/Persistence/Configurations tests/Operation.UnitTests/Infrastructure/ModelContractTests.cs
dotnet build GearFlow.Operation.CI.slnf -c Release --no-restore
dotnet test tests/Operation.UnitTests -c Release --filter FullyQualifiedName~ModelContractTests
```

Para formatar esse escopo, remover --verify-no-changes. O modo pasta não precisa do workspace MSBuild por pipe e é suficiente para o gate de whitespace; análise semântica continua no build. Ampliar regras/arquivos do gate em mudanças revisáveis, após corrigir a baseline correspondente. Não elevar todas as sugestões a erros de uma vez.

[Plano M1–M5](../planning/CODE_QUALITY_PLAN.md), [estrutura](PROJECT_STRUCTURE.md), [configuração](CONFIGURATION.md) e [testes](../../tests/README.md).
