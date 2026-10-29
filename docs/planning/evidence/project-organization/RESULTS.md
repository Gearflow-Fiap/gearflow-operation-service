# Organização e execução local — 29/10/2026

## Escopo solicitado

1. Projeto docker-compose.dcproj com Microsoft.Docker.Sdk e perfil Docker Compose na solução Visual Studio, usando o ambiente local já existente.
2. Pastas de produção organizadas por responsabilidade, mantendo as camadas e os namespaces públicos. Casos de uso OperationService separados por feature com um único pipeline de idempotência/versão/commit.
3. Projeto Operation.Tests renomeado para Operation.UnitTests; cenários separados por camada e fixtures em Support. Integração separada em Api/Persistence/Messaging/Delivery/Support. Dependências comuns centralizadas em tests/Directory.Build.props.

MediatR avaliado e não adicionado: a separação de comandos/consultas e o pipeline atômico existentes permitem organizar os arquivos sem acrescentar um dispatcher de encaminhamento. Contratos/namespace/URN públicos permanecem iguais.

## Execução e depuração

Compose usa PostgreSQL, migration explícita e API. Arquivo .env.local é criado somente se estiver ausente, com credenciais aleatórias que não são impressas; valores existentes são preservados. Testes cobrem geração, reexecução e falha sem sobrescrever configuração incompleta.

IDE usa Regular Mode com build Debug e overlay compose.visualstudio.yml; API habilita diagnósticos e filesystem gravável apenas em desenvolvimento. CLI/CI mantêm imagem Release e restrições de runtime. Dockerfile fica em src/Operation.Api junto ao projeto; contexto de build continua na raiz. Compose e workflow de entrega apontam ao novo caminho.

Solução completa GearFlow.Operation.slnx inclui o .dcproj. Filtro GearFlow.Operation.CI.slnf contém todos os nove projetos .NET e permite build/test sem o SDK Docker da IDE. Não remove testes do gate.

## Validação

- Restore/auditoria NuGet e build Release aprovados, zero avisos/erros.
- **228 testes .NET aprovados**, 164 unitários + 64 integração, zero falhas/ignorados. Três testes de arquitetura novos verificam dependências das camadas.
- **12 testes Python aprovados**, incluindo três novos para inicialização de credenciais locais; nenhum ignorado na execução local.
- PostgreSQL 17 e RabbitMQ 4.1 reais, dados/vhosts exclusivos de testes. Servidor PostgreSQL nativo encerrado de forma controlada após o teste.
- Os 18 contratos foram exportados do CLR e comparados aos schemas/exemplos versionados: compatibilidade aprovada.
- Configuração Compose base + overlay validada pelo CLI Docker sem imprimir configuração interpolada. MSBuild instalado do Visual Studio avaliou propriedades do .dcproj.
- Visual Studio inspecionado: docker-compose carregado e perfil Docker Compose observado após confirmar Single startup project. O ambiente iniciado via IDE respondeu ao smoke HTTP (200 saúde, 401 anônimo, 403 cliente, 201 criação, 200 consulta/desativação). F5/readiness final confirmados pelo usuário; breakpoint específico não foi declarado como executado.
- Metadados de caminhos, separação base/dev, filtro completo e YAML dos workflows verificados. Nenhum deploy remoto realizado.

| Assembly | Cobertura |
|---|---:|
| Contracts | 26/26 — 100,00% |
| API | 303/324 — 93,52% |
| Application | 345/345 — 100,00% |
| Domain | 370/374 — 98,93% |
| Infrastructure | 2512/2532 — 99,21% |

Relatórios TRX/Cobertura/OpenCover em TestResults/reorganization (ignorados). Check >=80% por assembly aprovado. As oito validações Python do 7B.1 estão incluídas no total doze, não são contadas duas vezes.

## Diagnóstico da depuração

Após associar o Dockerfile ao projeto API, a IDE passou a anexar o debugger, mas seu arquivo gerado apontava /app/Operation.Api.dll. O usuário confirmou exit 155 e ausência desse arquivo; a imagem publica em /app/api/Operation.Api.dll. Propriedades equivalentes foram registradas no csproj, mas o fluxo Compose exige override final: docker-compose.vs.release.yml aplica o caminho correto depois do .g.yml. O overlay inicial era anterior ao arquivo gerado e seus labels eram sobrescritos. A combinação com o .g.yml real foi validada via Docker Compose config; teste novo reproduz o erro e comprova a correção da ordem. [Diagnóstico sanitizado](debugger-path-diagnosis.json). Após regenerar, o usuário confirmou F5 abrindo /health/ready com status ready; o agente repetiu o smoke HTTP na imagem final com sucesso. Breakpoint específico não foi confirmado e não é declarado como teste executado.

## Retomada

[Guia Visual Studio](../../../development/VISUAL_STUDIO.md), [estrutura](../../../development/PROJECT_STRUCTURE.md) e [mapa de testes](../../../../tests/README.md). F5/readiness confirmados pelo usuário; validar breakpoint específico se necessário no trabalho diário. Pendências de OS/Saga, integração coletiva, infraestrutura e deploy do incremento 7B continuam no acompanhamento; organização local não encerra a fase 4.

Outros repositórios foram apenas lidos. Não houve commit, push, publicação ou alteração de recursos compartilhados. Cache antigo bin/obj de Operation.Tests foi preservado em artifacts/legacy-unit-test-build (ignorado), sem apagar dados.
