# Integração Operation — revisão 7B.1

Este kit é um preview para revisão entre os responsáveis. Não confirma uma Saga integrada, um acordo do grupo ou execução remota. O dono da OS/Saga ainda não foi confirmado pelo grupo. Operation permanece participante de diagnóstico e execução física.

## O que compartilhar

O ZIP contém este guia, matriz de compatibilidade, decisões pendentes, cenários de homologação, 18 schemas/exemplos gerados e o NuGet GearFlow.Contracts.Operation 1.0.0-preview.1. Não contém configurações privadas, bancos, tokens, código do monólito ou outros repositórios. bundle.json registra revisão/árvore alterada e SHA-256 dos arquivos. Checksums detectam corrupção; não autenticam o remetente.

## Como o coordenador usa o Operation

1. Adicionar a pasta packages do ZIP como feed NuGet local e referenciar GearFlow.Contracts.Operation na versão exata do manifesto. Requer .NET 10 e MassTransit 8.5.11 para reproduzir a baseline testada. Não copiar records para outro namespace: isso muda a identidade da mensagem.
2. Obter vhost/credencial e acordar envelope/topologia. O runtime usa application/vnd.masstransit+json e exchanges de eventos operation.<fato> sem sufixo de versão. O wrapper eventType/data do infra-broker não é compatível automaticamente.
3. Provisionar assinaturas duráveis dos eventos ANTES de habilitar o dispatcher do Operation. Cada serviço interessado precisa de fila própria. Confirmação de Publish não prova que todos os interessados assinaram.
4. Enviar StartDiagnostic à fila operation-start-diagnostic. Body e transporte devem ter MessageId/CorrelationId iguais; CorrelationId é ServiceOrderId e MessageId identifica a intenção. Usar OccurredOn UTC. O comando recebe o pedido e não inicia trabalho físico.
5. Consumir DiagnosticFinished preservando Snapshot completo e imutável. Não recalcular preços no Budget. Cada linha mantém DiagnosticLineId/LineId; Budget deve devolver seu BudgetJobId associado à linha de mão de obra aprovada.
6. O coordenador só envia StartExecution depois de comprovar orçamento aprovado, pagamento e reserva aplicável. Body inclui o snapshot aceito e o mapeamento de linhas. Operation valida o pedido e autoriza a fila; o mecânico inicia fisicamente via HTTP.
7. Ao consumir ExecutionFinished, coordenar ConsumeStock no Inventory quando houver reserva; só finalizar a OS global depois da confirmação aplicável. Operation não baixa saldo nem publica eventos de pagamento.

Exemplo de envio com o contrato tipado (a configuração do bus vem do ambiente):

```csharp
using GearFlow.Contracts.Operation.V1;
using MassTransit;

// command é StartDiagnostic desserializado/construído conforme schema/exemplo.
var endpoint = await bus.GetSendEndpoint(new Uri("queue:operation-start-diagnostic"));
await endpoint.Send(command, context =>
{
    context.MessageId = command.MessageId;
    context.CorrelationId = command.CorrelationId;
});
```

Os JSONs de examples são corpos independentes de mensagens, não envelopes RabbitMQ nem uma sequência correlacionada. Não publicar o corpo cru pelo painel do RabbitMQ como se fosse uma mensagem MassTransit. Manifesto informa URN e fila de cada comando; eventos não têm fila de destino única.

## Exchanges dos eventos publicados

Os 13 eventos usam exchanges próprios `operation.<fato>`, sem sufixo `.v1`, do tipo fanout durável do MassTransit. São nomes de exchanges, não routing keys de uma exchange topic compartilhada. O envelope, URN `GearFlow.Contracts.Operation.V1:<Tipo>`, propriedades e IDs permanecem iguais. Não remover V1 do namespace para adequar o nome no broker.

| Contrato | Exchange RabbitMQ |
|---|---|
| `DiagnosticFailed` | `operation.diagnostic-failed` |
| `DiagnosticFinished` | `operation.diagnostic-finished` |
| `DiagnosticQueued` | `operation.diagnostic-queued` |
| `DiagnosticStarted` | `operation.diagnostic-started` |
| `ExecutionAuthorized` | `operation.execution-authorized` |
| `ExecutionFinished` | `operation.execution-finished` |
| `ExecutionStarted` | `operation.execution-started` |
| `JobExecuted` | `operation.job-executed` |
| `OperationCanceled` | `operation.canceled` |
| `OperationCancellationRejected` | `operation.cancellation-rejected` |
| `OperationCommandRejected` | `operation.command-rejected` |
| `OperationDeactivated` | `operation.deactivated` |
| `VehicleUpdated` | `operation.vehicle-updated` |

Fonte canônica dos nomes: `GearFlow.Contracts.Operation.V1.OperationEventNames.All`, disponível no pacote puro de contratos, sem dependência MassTransit. O manifesto runtime-v1 registra `exchange` nos eventos e null nos comandos. O Operation aplica o mapeamento com ConfigureOperationEventNames; comandos e tipos do framework continuam com a formatação anterior.

### Configuração do consumidor

Receber o novo pacote/manifesto não renomeia a topologia automaticamente. O consumidor deve configurar o mesmo nome antes de registrar seus endpoints. Exemplo para DiagnosticFinished:

```csharp
using GearFlow.Contracts.Operation.V1;
using MassTransit;

// Dentro de UsingRabbitMq, antes de ConfigureEndpoints/ReceiveEndpoint:
cfg.Message<DiagnosticFinished>(message =>
    message.SetEntityName(OperationEventNames.All[typeof(DiagnosticFinished)]));
// O endpoint usa o consumer tipado DiagnosticFinished normalmente.
```

Repetir para cada tipo consumido, usando o mapa compartilhado. Cada serviço interessado mantém sua própria fila durável; réplicas do mesmo consumidor compartilham essa fila. Fazer bind apenas pelo nome do exchange não substitui o contrato/envelope tipado. Não usar operação `operation.completed` como alias automático de ExecutionFinished.

### Transição da topologia

Esta alteração troca os exchanges anteriores baseados no nome CLR pelos nomes acima. Assinantes configurados somente para os nomes antigos não recebem os eventos novos. Antes de habilitar o dispatcher no ambiente compartilhado:

1. Acordar nomes/envelope e permissões com os responsáveis e infra-broker.
2. Configurar consumidores e provisionar filas/bindings duráveis para os novos exchanges.
3. Validar publicação/recepção com MessageId e CorrelationId, então habilitar o produtor/dispatcher.
4. Preservar filas e mensagens antigas para drenagem/reconciliação; não fazer purge ou excluir exchanges automaticamente.

Não há dual publish, bridge automática nem alteração de definitions.json no infra-broker. Pendências de outbox mantêm IDs/contratos; após a atualização sua projeção utiliza os novos exchanges. Reentrega pode duplicar mensagem e exige a deduplicação existente. Publisher confirm/Mandatory não comprova assinatura de todos os participantes necessários.

## Eventos Budget e fronteiras

Budget publica BudgetGenerated, BudgetGenerationFailed, BudgetApproved, BudgetRejected, BudgetCanceled, PaymentRequested, PaymentConfirmed, PaymentFailed e PaymentRefunded. Esses nove nomes foram comunicados pelo grupo e constam na documentação clonada, mas ainda faltam contratos CLR/schemas executáveis do Budget.

O coordenador da OS deve reagir a esses fatos e decidir o próximo comando. Operation não consome PaymentConfirmed diretamente: pagamento sozinho não comprova todas as pré-condições do reparo. Se o grupo escolher outro dono para a Saga, atualizar essa decisão e os contratos antes de implementar consumers adicionais no Operation. A recomendação local segue OS separado; ainda não é decisão coletiva.

## HTTP e preços

API Operation exige JWT/Staff (claim configurável), comandos humanos com Idempotency-Key GUID e If-Match forte. Consultar a operação obtém a versão/ETag. Anônimo 401, sem permissão 403, versão antiga 412, precondição ausente 428, intenção conflitante 409. Não encaminhar o token humano para Inventory.

Operation consulta GET /api/inventory/parts/{id} e /api/inventory/consumables/{id}, usando token próprio do ambiente. Campos atuais: id/name/priceCents e id/name/unitPriceCents. Os DTOs clonados não expõem isActive: o adapter trata esse campo quando existir, sem exigir que ele esteja presente. A inspeção confirma estrutura de código, não autenticação nem chamada entre serviços reais.

Regra recomendada: BRL, centavos inteiros, per-unit-away-from-zero-v1, arredondamento por linha. Unidades de insumos e aceitação dessa política pelo grupo continuam pendentes. Não inventar preço ou usar zero quando a cotação falhar.

## Falhas e compensações

- Reentrega mantém MessageId e conteúdo; nova intenção recebe novo MessageId. Reuso divergente é erro de protocolo.
- Cancelar antes de executar: coordenador envia CancelOperation e espera OperationCanceled antes de liberar estoque/reembolsar. Timeout não comprova cancelamento.
- OperationCancellationRejected depois do início físico exige reconciliação; não estornar/liberar automaticamente.
- Fila _error/_skipped requer investigação, preservando IDs. Não há purge/retry automático no kit.
- OS só com mão de obra: pular ReserveStock/ConsumeStock. Com estoque, agrupar linhas por tipo/id antes da reserva, preservando separadamente a identidade de linhas do diagnóstico.

## Decisões e aceitação

Revisar decisions.json e compatibility.json com cada responsável; registrar decisão, dono e revisão dos contratos antes de marcar aprovado. scenarios.json descreve os cenários do fluxo conjunto e a evidência esperada. Eles continuam não executados entre serviços reais. Testes locais do Operation não devem ser registrados como Saga completa.

Para gerar o kit no repositório Operation:

```powershell
dotnet build GearFlow.Operation.CI.slnf -c Release
dotnet run --project tools/Operation.ContractArtifacts -c Release --no-build -- artifacts/handoff-clr
dotnet pack src/Operation.Contracts -c Release --no-build --output artifacts/handoff-package
python -m pip install -r scripts/contract_requirements.txt
python scripts/build_integration_handoff.py --package artifacts/handoff-package/GearFlow.Contracts.Operation.1.0.0-preview.1.nupkg --generated artifacts/handoff-clr --output artifacts/operation-integration-preview.zip
python scripts/build_integration_handoff.py --verify artifacts/operation-integration-preview.zip
```

O gerador recusa sobrescrever ZIP existente; escolher novo nome para nova entrega. O NuGet deve conter os mesmos contratos da fonte e o export CLR deve corresponder aos arquivos versionados. O kit é gerado localmente/como artifact da CI; não publica pacote em registry nem modifica o broker.
