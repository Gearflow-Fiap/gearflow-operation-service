# Acompanhamento da implementação

Atualizado em 29/10/2026. **Retomada: 7A e preparo 7B.1 concluídos localmente; próximo marco é decisão coletiva OS/Saga/contratos e homologação real do 7B.** Registros anteriores são históricos; os três passos solicitados diferem dos marcos P0-P8.

| Passo solicitado | Escopo | Situação | Evidência |
|---|---|---|---|
| 1 — Paridade e escopo | Inventário fase 3, donos novos, lacunas e critérios de teste | **Concluído em 28/10/2026** | [Matriz](STEP_1_PARITY.md), [manifesto](PARITY_MANIFEST.json), [testes](evidence/step-1/RESULTS.md) |
| 2 — Modelo operacional | Implementar dados/identidades/estados necessários ao Operation, com testes de domínio | **Concluído em 28/10/2026** | [Modelo](STEP_2_MODEL.md), [testes](evidence/step-2/RESULTS.md), [aceite](STEP_2_ACCEPTANCE.json) |
| 3 — Casos de uso | Implementar fluxo local completo com portas e testes de aplicação | **Concluído em 28/10/2026** | [Aplicação](STEP_3_APPLICATION.md), [testes](evidence/step-3/RESULTS.md), [aceite](STEP_3_ACCEPTANCE.json) |

## Regra de trabalho

- Executar um passo por vez, registrar alterações/testes/limites e avisar conclusão.
- Ao concluir passo 1, parar antes de implementar passo 2, conforme solicitado pelo usuário.
- Próximos passos recebem evidência própria; não reutilizar cobertura antiga como prova de funcionalidade nova.
- Não publicar pacote, implantar ou alterar runtime de outros repositórios nesta sequência inicial.

## Passo 1 — registro de conclusão

### Entregue

- 29 pontos do fluxo com fonte, regra observada, dono na divisão, lacuna atual e critérios de aceite.
- Todos os 23 handlers de casos de uso Workshop/Catalog inventariados; 60 referências de fonte com SHA-256.
- Escopo Operation definido: diagnóstico, fila, progresso dos reparos, histórico e conclusão local. OS, Budget e Inventory mantêm seus dados/efeitos.
- Necessidades de dados do passo 2 registradas: veículo, serviços/peças solicitados, consumíveis, identidade de linha aprovada, atividade e histórico.
- CHG-01..15 distinguem paridade, correção e proposta: peças ausentes do contrato anterior, cálculo de consumíveis, alteração de veículo, desativação, identidade BudgetJobId/LineId, fila e início físico.
- 22 critérios AT documentados para testes de implementação; não marcados como aprovados antes de existir código.
- README, índice, plano geral e avisos de contratos/API apontam para esta baseline de paridade.

### Validado

- `dotnet test` em Release: **19 aprovados, 0 falhas, 0 ignorados**.
- Cobertura Domain/Application atuais: **100% de linhas e branches**, 101/101 linhas e 44/44 branches. Gate >=80% existente aprovado.
- Manifesto conferido contra arquivos de origem; referências a testes e critérios válidas; todos os handlers do recorte têm destino.
- Código de src/tests, scripts e CI permaneceu idêntico ao início do passo, conferido por hash. Passo 2 não começou.
- Nenhum teste do monólito/banco/broker/API/Saga foi executado; cenários legados foram inspecionados como referência.

## Passo 2 — registro de conclusão

### Implementado

- Dados iniciais completos (OS/veículo/código opcional/serviços/peças), identidade estável de diagnóstico e consumíveis decimais.
- Snapshot imutável e autoritativo de itens/valores, com política identificada e overflow verificado; sem mudar implicitamente cálculo financeiro de consumíveis.
- Mapeamento explícito BudgetJobId/DiagnosticLineId/CatalogJobId, com linhas repetidas de catálogo executadas independentemente.
- Estados locais: Received, InDiagnostic, AwaitingExecutionAuthorization, ReadyForExecution, InExecution, ExecutionCompleted, DiagnosticFailed e Canceled.
- Autorização separada do reparo físico; cancelamento/desativação, tombstone, histórico/versão e datas UTC sem regressão.
- Ajuste mínimo das assinaturas/fila do núcleo Application para compilar com o modelo; regressão preservada com os novos dados. Não são casos de uso completos do passo 3.
- [ADR-006](../architecture/adr-006-modelo-local-e-precificacao.md) registra escolha local, sem atribuir aprovação coletiva dos contratos.

### Validado

- **72 testes aprovados em Release**, 0 falhas/ignorados. Testes anteriores foram reorganizados/evoluídos; não são 19 antigos mais 72 novos.
- **342/342 linhas (100%) e 150/153 branches (98,03%)** instrumentados; gate >=80% de linhas por assembly aprovado.
- Formatação whitespace C# em modo folder verificada e diff conferido.
- Resultado, teste a teste, cobertura e limites em [evidence/step-2](evidence/step-2/RESULTS.md).
- [Critérios AT atualizados](STEP_ACCEPTANCE.md): separar o que o domínio já prova do que depende de aplicação/persistência/integração.

### Limites e parada

Não foram implementados consultas/paginação, fonte de preço por portas, journal, unidade de trabalho isolada, banco, API, broker, Saga ou BDD. O esqueleto Application ainda retorna conflito no recebimento repetido e não cria tombstone ao cancelar operação ausente. O modelo não autentica fonte de preço nem autoriza pagamento/reserva: essas fronteiras dependem de implementação/revisão posteriores. Nenhum arquivo de runtime dos outros repositórios foi alterado.

**Passo 2 concluído. Parar e informar ao usuário antes de iniciar o passo 3.**

## Passo 3 — registro de conclusão

### Implementado

- Casos de uso locais de recebimento, diagnóstico, falha explícita, alteração de veículo, desativação, autorização, início físico, execução por linha, conclusão e cancelamento pré-criação/tombstone.
- Precificação via IPriceCatalog com duas políticas explícitas (sem default), resposta completa validada e ausência/indisponibilidade sem preço zero inventado.
- CommandContext, fingerprint canônico e receipts para replay, reuso divergente de chave e checagem de versão. Commit de estado/receipt/eventos com contrato de CAS e atomicidade.
- WorkOperation.Copy e InitialVehicleId para trabalhar com estado destacado e não reverter atualização posterior num recebimento repetido.
- OperationQueries/DTOs congelados, paginação/filtros, histórico e durações/amostras por fase.
- Eventos pendentes internos com enum de tipo, causa, versão e projeção congelada; não são contratos públicos do broker.
- [ADR-007](../architecture/adr-007-casos-de-uso-e-intencoes.md), [aceite por critério](STEP_3_ACCEPTANCE.json) e documentação dos [casos de uso](STEP_3_APPLICATION.md).

### Validado

- **120 testes aprovados em Release**, 0 falhas, 0 ignorados.
- **632/632 linhas (100%) e 278/283 branches (98,23%)** instrumentados; gate >=80% por assembly aprovado.
- Falhas de cotação e commit após mutação de cópia não tornam estado/receipts/eventos visíveis parcialmente no double de teste.
- Replay devolve resultado original antes de preço/precondição; protocolo de commits simultâneos modelado com CAS no storage isolado.
- Consultas, filas, DTOs e cálculo de métricas validados com dados locais. Formatação/diff/links/referências conferidos.
- Resultado individual e cobertura em [evidence/step-3](evidence/step-3/RESULTS.md).

### Limites

Storage e catálogo concretos desta etapa existem somente em tests. Não há PostgreSQL/EF, HTTP/JWT, client de preço real, MassTransit/RabbitMQ, Saga/BDD real, exporter de telemetria ou deploy. Política de consumíveis e contratos OS/Budget ainda exigem revisão coletiva. Teste de CAS em memória não comprova isolamento/constraints do banco. Nenhum runtime dos outros repositórios foi alterado.

**Passo 3 concluído. Encerrar este checkpoint e informar o próximo incremento, sem iniciar banco/API/mensageria automaticamente.**

## Próximo incremento — somente após retorno ao usuário

Implementar persistência exclusiva PostgreSQL/EF: migrations, mapeamento/rehidratação do agregado, receipt persistente, índices únicos, concorrência otimista/CAS e outbox/inbox; executar testes contra banco real para falhas/restart e disputas de criação/atualização/intenção. Definir retenção e migração de fingerprint/journals/tombstones. Depois host HTTP/JWT/clients de preço, contratos publicados e broker/Saga/BDD, CI/CD e observabilidade.

Os três passos solicitados terminaram o núcleo local de domínio/aplicação. Não declarar serviço completo enquanto os adapters/integrações e critérios de entrega da fase 4 não estiverem validados.

## Sequência posterior autorizada

Usuário autorizou iniciar próximos incrementos. O [plano](NEXT_INCREMENTS.md) começa pelo incremento 4 de persistência e testes reais. Estado atual: em execução; próximos adapters ainda não concluídos.

## Incremento 4 — concluído

Persistência exclusiva PostgreSQL/EF implementada, com migrations, reidratação validada, consultas SQL, histórico, receipt e outbox transacionais. Conflitos por intenção/versão e rollback comprovados contra banco real. 130 testes unitários + 14 de integração aprovados; cobertura por assembly >=98%. [Evidências e limites](evidence/increment-4/RESULTS.md), [ADR-008](../architecture/adr-008-persistencia-postgresql.md), [guia local](../development/POSTGRESQL.md).

O texto de limites do passo 3 registra o estado histórico daquele checkpoint. Estado atual inclui PostgreSQL, mas ainda não API ou broker. Próximo incremento: 5, host HTTP/JWT e adapter de preços, com testes HTTP e validação dos contratos comerciais. Antes de fechar o adapter, confirmar com Inventory a política de consumíveis e autenticação/contrato de cotação. Contratos públicos de eventos devem ser revisados coletivamente no incremento 6.


## Incremento 5A — concluído

Host HTTP/JWT, policy staff explícita, ProblemDetails, OpenAPI e comandos com ETag/idempotência. **166 aprovados** (131 unitários + 35 integração), sem falhas/ignorados; API 97,97%, demais assemblies >=98,61%. [Evidências](evidence/increment-5a/RESULTS.md), [ADR-009](../architecture/adr-009-api-http.md), [rotas/configuração](../development/API_ENDPOINTS.md), [proposta ao grupo](../rfcs/rfc-003-padrao-fase-4.md).

Retomada: 5B, catálogo de mão de obra e preços reais. Host ainda retorna 503 na cotação por falta de adapter; não declarar incremento 5 completo nem fluxo distribuído pronto. Preservar regra de checkpoint/testes por entrega.

## Incremento 5B — concluído localmente

Catálogo PostgreSQL, CRUD Staff com versão/desativação e adapter de cotação HTTP Inventory. **198 aprovados** (155 unitários + 43 integração), sem falhas/ignorados; todos os assemblies >=98,45%. [Evidências](evidence/increment-5b/RESULTS.md), [ADR-010](../architecture/adr-010-catalogo-e-precos.md), [configuração](../development/CATALOG_AND_PRICING.md).

Retomar no incremento 6: contratos públicos/broker e homologação com serviços reais. O Inventory dos testes 5B é servidor substituto; token real, policy do emissor e política financeira comum ainda exigem alinhamento. Preservar snapshots e histórico; não importar dados do legado trocando IDs silenciosamente.

## Incremento 6 — participante concluído localmente

Consumers RabbitMQ/MassTransit, inbox recuperável, dispatcher da outbox, contratos tipados/preview NuGet e schemas/exemplos gerados. **215 testes distintos aprovados** (159 unitários + 56 integração), sem falhas/ignorados; cobertura por assembly >=97,60%. Reexecução complementar do fluxo com autorização repetida também aprovada. [Evidências](evidence/increment-6/RESULTS.md), [ADR-011](../architecture/adr-011-mensageria-operation.md), [operação](../development/MESSAGING.md), [contratos ativos](../contracts/README.md).

Retomar no 7: homologar topologia/envelope com infra-broker e serviços, confirmar owner OS/Saga e contratos/credenciais, executar BDD distribuído e preparar observabilidade/deploy. Pacote preview foi gerado localmente; nada publicado em registry e nenhum repositório externo alterado. Saga completa/CI remota/deploy ainda não validados.

## Incremento 7A — concluído localmente

Docker/Compose, migration CLI, probes, OpenTelemetry/logs/backlog, manifests Kustomize e workflows configuráveis. **225 testes distintos aprovados** (161 unitários + 64 integração), zero falhas/ignorados; cobertura mínima 93,52%. Duas regressões suplementares aprovadas. Imagem final com migrate exit 0/API healthy, smoke HTTP aprovado e 18 contratos preservados. [Evidências](evidence/increment-7a/RESULTS.md), [guia](../development/DELIVERY_AND_OBSERVABILITY.md), [ADR-012](../architecture/adr-012-observabilidade.md), [ADR-013](../architecture/adr-013-entrega-operation.md).

Reexecução Docker identificou histórico EF ambíguo quando usuário e schema se chamam operation. Corrigido para public.__EFMigrationsHistory e validado com reaplicação; dados preservados. Infraestrutura fase 3 inspecionada sem alterações: PostgreSQL próprio ainda precisa ser provisionado.

Retomar no 7B: alinhar OS/Saga/contratos/topologia/credenciais com o grupo, provisionar banco/secrets/rede, configurar CI/Sonar/GHCR/environment/OIDC, homologar deploy/telemetria e executar Saga/BDD distribuídos. Nenhuma publicação/deploy foi executada. 7A não encerra a fase 4.

## Incremento 7B.1 — kit para revisão do grupo concluído

Guia do coordenador, matriz de oito achados com SHA-256 de 14 fontes, seis decisões pendentes e onze cenários conjuntos não executados. ZIP reproduzível contém NuGet preview e os 18 schemas/exemplos; exclui configurações privadas por lista explícita. Oito testes Python aprovados, export CLR/contratos e pacote/ZIP reais verificados. [Guia](../integration/README.md), [evidências](evidence/increment-7b1/RESULTS.md).

Usuário informou não saber quem assume OS/Saga. Documentos Budget propõem OS separado, mas não há confirmação coletiva nem runtime disponível. Não transferimos silenciosamente essa responsabilidade ao Operation. Próximo marco: responsáveis revisam D-01..D-06 e alinham envelope/topologia, estoque, preços e identidade; depois executar H-01..H-11 no ambiente conjunto. 7B global, cloud e Saga completa permanecem pendentes.

## Organização e experiência local — 29/10/2026

Atendidas as três frentes solicitadas: suporte Visual Studio Docker Compose; código agrupado por responsabilidades/features, sem novo mediator; testes separados por camada e tipo de integração, com fixtures próprias. Solução CLI filtrada contém todos os nove projetos .NET e dispensa SDK Docker da IDE. Credenciais locais inicializadas com preservação de valores existentes.

**228 testes .NET + 12 Python aprovados**, sem falhas/ignorados, cobertura mínima 93,52% e 18 contratos preservados. Projeto/perfil Compose reconhecidos na IDE; smoke HTTP do ambiente iniciado passou. Usuário confirmou F5 abrindo readiness; smoke HTTP final repetido pelo agente. Breakpoint específico não foi confirmado. [Evidências](evidence/project-organization/RESULTS.md), [Visual Studio](../development/VISUAL_STUDIO.md), [estrutura](../development/PROJECT_STRUCTURE.md), [testes](../../tests/README.md).

Retomada de negócio permanece 7B: dono OS/Saga e contratos/topologia/credenciais coletivos, integração real e entrega remota. Não confundir reorganização local com conclusão dessa homologação.

## Scalar — concluído localmente

Scalar em /scalar/v1 e esquema público apenas em Development; APIs de negócio continuam Staff. Fora de Development, UI 404 e esquema protegido. Visual Studio abre Scalar após reconstruir com F5; helper New-LocalStaffToken.ps1 gera token temporário local sem embutir credenciais na página.

231 testes .NET distintos aprovados (164 unitários + 67 integração), incluindo três reexecuções após recuperar broker inicialmente indisponível; 13 Python aprovados. Cobertura API 93,64%, demais assemblies >=98,93%. [Evidências](evidence/scalar/RESULTS.md) e [uso](../development/VISUAL_STUDIO.md). Sem deploy remoto; imagem local precisa de reconstrução para exibir a nova UI.

## Evolução de código M1–M5 — M1 concluído

Plano registrado em [CODE_QUALITY_PLAN](CODE_QUALITY_PLAN.md), separado dos passos iniciais/incrementos 7B. M1 documenta as 16 rotas no Scalar/OpenAPI e acrescenta testes descobrindo rotas reais para documentação, policy Staff, acesso anônimo/cliente, respostas/headers e exemplo executável.

236 testes .NET distintos (164 unitários + 72 integração) e 13 Python aprovados, sem falhas/ignorados; API 95,31% e demais assemblies >=98,93%. Cinco testes focados adicionais são reexecuções dos casos já incluídos no total. [Evidências](evidence/quality-m1/RESULTS.md).

Próximo passo dessa sequência: M2 — DI por camada e opções tipadas. Demais pendências de integração coletiva/OS/Saga continuam no 7B. Reiniciar F5 para ver as novas descrições no Scalar.

## Evolução de código — M2 concluído

Composição explícita API/Application/Infrastructure e opções tipadas para autenticação, conexão, Inventory, preços, mensageria e telemetria. Lifetimes preservados; credencial Inventory observa reload. Ausência da conexão é detectada na inicialização, mas disponibilidade continua no readiness. Flags opcionais e ausência de default financeiro preservadas.

273 testes .NET distintos (164 unitários + 109 integração) e 13 Python aprovados, zero falhas/ignorados; API 95,12%, demais assemblies >=98,93%. [Evidências](evidence/quality-m2/RESULTS.md), [guia](../development/CONFIGURATION.md). F5/Compose não alterados; reconstruir para executar o código novo.

Próximo passo: M3 — validação HTTP, ator e ETag reutilizáveis. Integração coletiva/OS/Saga e entrega remota continuam no 7B.

## Evolução de código — M3 concluído

Helpers de ator autenticado, ETag e contexto de comando HTTP; shape/conversão de requests organizados e regras preservadas na aplicação/domínio. Catálogo permanece sem Idempotency-Key obrigatório, e replay/transação permanecem no pipeline existente. [Guia](../development/HTTP_INPUT.md).

290 testes .NET distintos (164 unitários + 126 no projeto de integração) e 13 Python aprovados; zero falhas/ignorados. API 95,79%; demais assemblies >=98,93%. Três cenários aprovados antes da refatoração e reexecutados depois; 17 casos novos incluídos no total final. [Evidências](evidence/quality-m3/RESULTS.md).

Próximo passo: M4 — configurações EF por entidade, versões NuGet centralizadas e convenções incrementais. Migrations, handlers e configuração F5/Compose não alterados neste checkpoint; integração coletiva/OS/Saga e entrega remota continuam no 7B.

## Evolução de código — M4 concluído

Mapeamentos EF separados em seis configurações; 24 versões diretas em Directory.Packages.props, preservando dependências resolvidas dos nove projetos. .editorconfig/analyzers e gate de whitespace incremental; pasta build na solução e Dockerfile atualizado para restore central. Modelo, DDL e histórico EF idênticos à referência anterior; quatro migrations/snapshot intactos.

294 testes .NET distintos (168 unitários + 126 integração) e 13 Python aprovados, sem falhas/ignorados. API 95,79%; demais assemblies >=98,93%. Restore isolado do contexto Docker e publish API/migrador aprovados; imagem/F5 não reconstruídos pelo agente. [Evidências](evidence/quality-m4/RESULTS.md), [guia](../development/BUILD_AND_PERSISTENCE.md).

Próximo passo: M5 — avaliar handlers e registrar decisão, sem MediatR automático ou duplicação do pipeline transacional. Pendências coletivas/OS/Saga e entrega remota seguem no 7B.
