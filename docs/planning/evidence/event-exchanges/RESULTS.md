# Exchanges dos eventos publicados — 30/10/2026

Implementação local dos 13 nomes operation.<fato> sem sufixo .v1, conforme solicitado. São exchanges fanout do MassTransit, não routing keys em uma exchange topic compartilhada. Nenhuma infraestrutura compartilhada, serviço externo ou fila de comando foi alterada.

## Implementação

OperationEventNames.All no pacote de contratos é o mapa canônico imutável e independente de MassTransit. OperationEventEntityNameFormatter delega nomes não mapeados ao formatador anterior. ConfigureOperationEventNames é aplicado ao bus de produção antes dos endpoints. O publicador/outbox existente continua usando os tipos reais e seus IDs; nenhum pipeline transacional foi refeito.

Mantidos os cinco comandos/filas, framework/Fault names, namespace/URN GearFlow.Contracts.Operation.V1, payloads, schemas, exemplos, migrations e política de ativação. Manifesto e export CLR agora incluem exchange nos eventos e null nos comandos. Validador de contratos também verifica nomes/duplicidade e topologia dos comandos.

## Validação

Build Release dos nove projetos: zero erros/avisos. Três testes focados aprovados, incluídos no total final: dois unitários para os 13 nomes e fallback de comandos/framework; um cenário real RabbitMQ publica todos os 13 eventos pelo publicador de produção e verifica exchange original recebido, body, MessageId, CorrelationId e causa.

Suíte completa com PostgreSQL 17 e RabbitMQ reais: **297 casos .NET distintos** (170 unitários + 127 integração), zero falhas/ignorados. **13 testes Python aprovados**. Reexecuções focadas não foram somadas. Cobertura >=80% aprovada por assembly: Contracts 100%; API 95,79%; Application 100%; Domain 98,93%; Infrastructure 99,23%.

18 contratos/schemas/exemplos validados e comparados com nova exportação CLR. Pacote NuGet preview e ZIP de integração gerados/verificados localmente em C:/Users/miran/Documents/Codex/2026-10-24/at/work/event-exchanges-20261030. Nenhuma publicação em registry. Payloads/schemas/exemplos, Messages.cs e migrations com diff vazio. git diff --check aprovado.

Relatórios completos: TestResults/event-exchanges; resumo em [validation.json](validation.json). O broker de teste foi iniciado pelo usuário, pois o agente não tem acesso ao pipe Docker; testes usam vhosts/bancos próprios e não purgam filas de ambiente compartilhado.

## Operação e limites

A mudança de nome físico não é transparente para assinantes configurados somente nos exchanges CLR antigos. Configurar/provisionar novos bindings duráveis dos consumidores antes de habilitar o dispatcher, preservar filas/mensagens antigas e coordenar a transição. Não há dual publish ou bridge automática. Não se alterou definitions.json do infra-broker.

[Mapa e instruções](../../../development/MESSAGING.md#exchanges-dos-eventos-publicados), [kit atualizado](../../../integration/README.md). A configuração padrão local continua com mensageria/dispatcher desligados; fornecer conexão/credenciais do broker e habilitar as flags para publicar no ambiente desejado. Os eventos não passaram a ser enviados pelo F5 sem essa configuração.

Imagem Docker não reconstruída pelo agente. Não houve commit, push, deploy ou homologação com OS/Budget reais. Pendências coletivas e de entrega permanecem no 7B.
