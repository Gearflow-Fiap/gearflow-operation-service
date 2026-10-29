# Mensageria Operation — incremento 6

## Ativação

Aplicar migrations (incluindo AddBrokerInbox) antes de iniciar consumers/dispatcher. Configurar por ambiente/secret manager, sem versionar credenciais:

```powershell
$env:Messaging__Enabled='true'
$env:Messaging__DispatchEnabled='true'
$env:RabbitMq__Host='localhost'
$env:RabbitMq__Port='5672'
$env:RabbitMq__VirtualHost='gearflow'
# Fornecer RabbitMq__Username e RabbitMq__Password pelo ambiente.
```

Messaging__Enabled ausente/false mantém API sem conexão ao broker. DispatchEnabled é separado e fica desligado por padrão: só ativar depois de migrations, compatibilidade e assinaturas duráveis provisionadas. Endpoint HTTP/JWT e configuração de preço continuam necessários para ações humanas. Em container, localhost aponta ao próprio container; usar nome de serviço/rede configurada.

O runtime 8.5.11 segue MassTransit/RabbitMQ como Inventory, com filas de comandos descritas no [manifesto](../contracts/runtime-v1/manifest.json). Mensagens publicadas são persistentes e usam publisher confirms + Mandatory. Sem rota para alguma fila, o registro permanece pendente; isso não comprova que TODOS os interessados necessários assinaram. Provisionar todos os tipos produzidos e validar roteamento antes da liberação.

O worker usa transação por registro, limite de 5s por Publish, lotes até 100 e intervalo de 2s. Falha interrompe lote, registra erro e mantém pendências. Réplicas usam SKIP LOCKED. Ordenação global ou por OS entre réplicas não é prometida; consumidores devem considerar identidade, causa e versão. Mensagem interna inválida exige investigação e pode bloquear entrega do lote; não descartar/alterar silenciosamente eventos já identificados.

## Uso pelos produtores

Usar pacote GearFlow.Contracts.Operation 1.0.0-preview.1 e namespace GearFlow.Contracts.Operation.V1. Referência NuGet com versão fixa/local feed; não copiar records ou entidades do domínio. Não foi publicado registry nem criado repositório de contratos comum. Assembly separada não muda o URN, que usa namespace e tipo.

```csharp
var endpoint = await bus.GetSendEndpoint(new Uri("queue:operation-start-diagnostic"));
await endpoint.Send(command, context =>
{
    context.MessageId = command.MessageId;
    context.CorrelationId = command.CorrelationId;
});
```

Comando/body e transporte devem ter IDs iguais; CorrelationId é OS. OccurredOn UTC, payload imutável, novo MessageId para nova intenção. StartDiagnostic recebe serviços/peças com LineId; StartExecution contém snapshot completo/política e mapeamento de linhas Budget; ações físicas são HTTP. Não usar PaymentConfirmed isoladamente para autorizar reparo: Operation aguarda o comando do coordenador após pré-condições.

## Recuperação e filas de erro

- Inbox registra hash/identidade antes do efeito. Journal de aplicação garante receipt+estado+evento atômicos; conclusão da inbox é outra transação recuperável. Não prometer uma transação única que englobe broker/duas etapas.
- Redelivery após crash usa receipt e não repete histórico. Inbox concluída apenas confirma consumo. Novo comando semanticamente repetido recebe resposta própria sem nova versão.
- Contrato/metadata inválidos e reuso divergente de MessageId vão diretamente para <fila>_error. Falhas técnicas têm tentativas de 100/500/1000 ms e depois _error. Mensagens não reconhecidas podem ir para <fila>_skipped, conforme MassTransit.
- Estado/snapshot incompatível é resposta durável OperationCommandRejected, não erro genérico de broker. Cancelamento após início físico retorna OperationCancellationRejected; coordenador não deve liberar estoque/estornar com base apenas em timeout.
- Depois de Publish confirmado, commit perdido pode causar duplicata com MESMO ID/corpo. Reprocessar mantendo identidade/payload em falha transitória. Corrigir contrato com nova intenção; não reutilizar ID já registrado com conteúdo novo.
- Sem retenção automática de inbox, receipts ou tombstones. Definir retenção/replay com o grupo antes de limpeza em produção.

Monitorar filas _error/_skipped, idade/quantidade da outbox e inbox incompleta. Ao reprocessar, investigar causa, validar estado global e preservar IDs; nenhuma ferramenta de purge/retry automático foi adicionada nesta etapa.

## Infra-broker e homologação

Repositório lido: C:\WS\gearflow-infra-broker. Ele propõe gearflow.events topic, routing key os.order-created.v1 e wrapper eventType/data. Operation/Inventory usam envelope nativo e exchanges por tipo MassTransit. Esses formatos não são intercambiáveis por copiar JSON para fila. Esta etapa não alterou definitions.json nem o infra-broker.

Recomendação ao grupo: usar MassTransit/envelope/URN em todos os serviços e deixar aplicações declararem sua topologia inicialmente; infra mantém vhosts, usuários, permissões e operação. Se o grupo optar por topic/routing keys próprias, definir EntityName/bindings/serializer de forma coordenada e testar compatibilidade; não ativar dois masters conflitantes.

Ainda precisam ser acordados: owner da OS/Saga (OS separado recomendado), filas de todos os assinantes, ACL por serviço, política financeira comum e publicação canônica do pacote. A Saga financeira não foi implementada no Operation. Demonstração local do participante não equivale a fluxo completo com os quatro serviços.

Para cutover, revisar dados/outbox anteriores antes de habilitar worker: ele entrega TODAS as pendências existentes. Não apontar testes/replay para vhost compartilhado. Dados antigos do gearflow-app precisam de plano separado de migração de IDs/estado; migrations não importam pedidos antigos.

## Testes locais e CI

Broker exclusivo de testes:

```powershell
docker compose -f compose.messaging-tests.yml up -d --wait
# PostgreSQL 17 via OPERATION_TEST_POSTGRES ou Testcontainers, conforme POSTGRESQL.md.
dotnet test GearFlow.Operation.CI.slnf --configuration Release --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/current
python scripts/check_coverage.py TestResults/current
```

Defaults dos testes: AMQP 127.0.0.1:5674, management http://127.0.0.1:15674 e credenciais de desenvolvimento do compose. Alternativas: OPERATION_TEST_RABBITMQ=amqp://usuario:senha@host:porta/ e OPERATION_TEST_RABBITMQ_MANAGEMENT=http(s)://host:porta/. Usuário de teste precisa gerenciar vhosts; não usar credencial produtiva. Fixture cria/apaga SOMENTE operation_it_<guid>; filas reais não são purgadas. CI usa serviços PostgreSQL/RabbitMQ próprios e executa a mesma suíte. Broker fica rodando para o usuário após os testes; não reiniciamos o broker inteiro.

Teste de reconexão desconecta consumidores e verifica fila durável; não simula queda do processo RabbitMQ nem perda de disco. Testes de crash são falhas injetadas nos pontos de commit; não confundir com teste de caos de processo. Sem ensaio de HA/TLS/cluster nesta etapa.
