# Incremento 6 — mensageria do participante Operation

Concluído localmente em 29/10/2026. Sem homologação coletiva presumida, sem deploy ou publicação em registry.

## Implementado

- Projeto Operation.Contracts (assembly/pacote GearFlow.Contracts.Operation 1.0.0-preview.1), sem EF/domínio/host.
- 5 comandos/13 eventos e respostas, namespace GearFlow.Contracts.Operation.V1; schemas Draft 2020-12 e exemplos gerados da metadata CLR.
- Consumers MassTransit.RabbitMQ 8.5.11 alinhados à linha do Inventory: recebimento, autorização, cancelamento, veículo e desativação.
- Validação de IDs/body/envelope, snapshot/política e mapeamentos de linhas. Contrato inválido ou ID divergente é permanente; falha técnica tem retry limitado e _error.
- Migration AddBrokerInbox. Registro de hash antes do efeito e protocolo recuperável com journal transacional da aplicação; respostas de no-op/rejeição persistidas.
- Dispatcher com SKIP LOCKED, publisher confirms/Mandatory, identidade estável e marcação após publicação. Worker no host com flags explícitas.
- Motivos limitados a 2000 caracteres na aplicação; confirmação de cancelamento/desativação preserva motivo auditado original. Autorização semanticamente repetida preserva AuthorizationMessageId inicial.
- CI com PostgreSQL/RabbitMQ e validação/compare de contratos/pack. Documentação/ADRs/tracker atualizados.

## Testes e comandos

Suíte Release: **159 unitários + 56 de integração = 215 testes distintos aprovados**, 0 falhas, 0 ignorados.

```text
dotnet test GearFlow.Operation.slnx --no-restore --configuration Release -m:1 -nr:false --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/increment-6-accepted --logger trx
```

Houve uma reexecução suplementar do cenário RabbitCommandsAndPublishedEventsRunFullOperation com a autorização semanticamente repetida, também aprovada (não contada como novo caso). Relatórios em TestResults/increment-6-accepted/supplemental. Código de produção idêntico entre a suíte e a reexecução; cobertura consolidada une os hits.

```text
python scripts/check_coverage.py TestResults/increment-6-accepted
```

| Assembly | Linhas cobertas/instrumentadas | Cobertura |
|---|---|---|
| GearFlow.Contracts.Operation | 26/26 | 100% |
| Operation.Api | 203/208 | 97,60% |
| Operation.Application | 312/312 | 100% |
| Operation.Domain | 370/374 | 98,93% |
| Operation.Infrastructure | 2072/2092 | 99,04% |

Gate >=80% aprovado. Infrastructure inclui migrations/modelos gerados; exclusão restrita ao arquivo OpenAPI gerado de biblioteca já documentado na runsettings. TRX/Cobertura brutos no TestResults local ignorado; CI remota não executada neste trabalho.

## Evidência real e seus limites

PostgreSQL 17.11 real em instância temporária. RabbitMQ 4.1-management real iniciado pelo usuário em container exclusivo, AMQP 5674/management 15674. Cada teste cria/remova apenas seu vhost/banco operation_it_<guid>. Broker inteiro não foi reiniciado ou apagado pelo agente.

Testado: comandos e eventos no broker; correlação/causação e URN; fluxo local de recebimento a execução final; duplicata exata/semântica; _error com contrato inválido/ID reutilizado; rejeição de cancelamento após reparo; veículo/desativação; fila durável após desconectar/reconectar consumidores; host API iniciando consumers e worker; Publish sem rota permanece pendente; dispatchers paralelos; crash injetado após commit da operação e após Publish, antes da marcação de entrega; tombstone; snapshot divergente; confirmação com motivo/autorização originais.

A Saga/coordenador foi representado por produtor/observador de teste. Budget/OS/Inventory completos não rodaram em conjunto. Catálogo de sucesso nos cenários de broker é fake explícito, separado dos testes HTTP do 5B. Reconexão de consumidor não equivale a queda do processo RabbitMQ; falhas injetadas em commit não equivalem a ensaio de caos/HA/perda de disco.

## Contratos e pacote

Gerador: tools/Operation.ContractArtifacts. Validado com python scripts/validate_contracts.py, incluindo 18 exemplos, schemas, IDs/datas/versões inválidos e campos opcionais extras. Exportação nova em diretório temporário comparada semanticamente aos JSONs versionados; sem drift. Unitários verificam URN real MassTransit, roundtrip, projeção de todos os eventos internos e enum numérico/desconhecido.

Preview nupkg contém DLL net10.0, README e 37 arquivos JSON (18 schemas, 18 exemplos, manifesto). Não foi publicado feed nem enviado mensagem ao grupo.

Artefato: GearFlow.Contracts.Operation.1.0.0-preview.1.nupkg
SHA256: 64947731F6C59A52CE9CEAE42C0C6F9248FF27119A7D8E00ECA5DB8A30E6C7BF

## Homologação pendente e retomada

Infra-broker foi inspecionado em C:\WS\gearflow-infra-broker: propõe gearflow.events topic/envelope eventType/data, diferente do envelope/topologia MassTransit usados no Inventory e neste Operation. Não foi modificado. Antes de conectar ambiente compartilhado, decidir padrão único ou adaptação explícita, filas de assinantes, permissões por serviço e owner/release dos contratos. Confirmar dono da OS/Saga e política financeira comum. Tokens/claims reais ainda dependem do emissor.

Operation é participante, não orquestrador financeiro. Eventos Budget de orçamento/pagamento não foram inventados/consumidos automaticamente; coordenador decide autorização após estoque/comercial. Mensageria desligada por padrão, dispatcher separado; revisar backlog/subscrições antes do cutover.

Não foram alterados gearflow-app, Inventory, Budget ou infra-broker. Não houve commit, deploy, purge de filas compartilhadas ou mudança de credenciais de usuário. Próximo incremento: 7, homologação/BDD com os serviços reais, observabilidade e entrega.
