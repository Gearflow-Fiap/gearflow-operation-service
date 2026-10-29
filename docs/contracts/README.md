# Contratos executáveis Operation — incremento 6

Baseline local implementada em 29/10/2026; **preview sob revisão coletiva**, sem pacote publicado em registry. Fonte canônica local: src/Operation.Contracts/Messages.cs. Pacote separado GearFlow.Contracts.Operation 1.0.0-preview.1 (.NET 10), sem copiar contratos Inventory. Namespace/URN continuam GearFlow.Contracts.Operation.V1.<Tipo>. Assembly não define o URN.

Para compartilhar os contratos sem copiar código nem configurações privadas, gerar o [kit de integração 7B.1](../integration/README.md). O ZIP reúne NuGet, schemas/exemplos, decisões e matriz de compatibilidade; não representa aprovação coletiva ou publicação em registry.

- [Manifesto ativo](runtime-v1/manifest.json): 5 comandos e 13 eventos/respostas.
- Schemas Draft 2020-12 e exemplos do serializador real em runtime-v1/schemas e runtime-v1/examples.
- [Operação e topologia](../development/MESSAGING.md).
- [ADR-011](../architecture/adr-011-mensageria-operation.md).

## Comandos

| Tipo | Fila (Send) | Regra |
|---|---|---|
| StartDiagnostic | operation-start-diagnostic | Receber OS em Received; RequestedJobs não vazio e RequestedParts preservados, com LineId próprio |
| StartExecution | operation-start-execution | Validar snapshot completo/política e mapear BudgetJobId -> DiagnosticLineId; autorizar fila |
| CancelOperation | operation-cancel-operation | Fence pré-execução; tombstone se recebimento ainda não ocorreu |
| UpdateOperationVehicle | operation-update-vehicle | Atualização permitida pelas regras locais, sem substituir pedido original |
| DeactivateOperation | operation-deactivate | Desativação explícita, sem remover histórico |

StartDiagnostic não inicia diagnóstico físico e StartExecution não inicia reparo. Ações humanas seguem HTTP/JWT/If-Match. CorrelationId é ServiceOrderId. MessageId é intenção, não ID do item. Campos do envelope MessageId/CorrelationId precisam corresponder aos do corpo; occurredOn deve estar em UTC (Z ou +00:00). Ator dos comandos é system:os no adapter, com origem autorizada por credenciais/ACL, não por campo livre no body.

## Eventos e respostas

Publicados por tipo MassTransit: DiagnosticQueued, VehicleUpdated, DiagnosticStarted, DiagnosticFinished, DiagnosticFailed, ExecutionAuthorized, ExecutionStarted, JobExecuted, ExecutionFinished, OperationDeactivated, OperationCanceled, OperationCommandRejected e OperationCancellationRejected. Cada interessado deve ter fila própria.

Eventos carregam identidade, causação e versão operacional; nenhum enum global de status ou agregado/histórico inteiro. DiagnosticFinished carrega Snapshot com política e valores imutáveis. JobExecuted identifica BudgetJobId, DiagnosticLineId e CatalogJobId separadamente; ExecutionFinished inclui ExecutedBudgetJobIds. Conclusão exige todos os serviços executados no domínio atual.

OperationVersion é positiva para fatos; rejeições podem ter versão null antes de existir operação. Codes iniciais: StateConflict e CancellationNotAllowed. A rejeição genérica não é promessa de retry automático; OS deve reconciliar etapa. CancellationNotAllowed impede liberar estoque/reembolsar automaticamente após início físico. Contrato malformado, metadata divergente ou ID reaproveitado vão para fila _error, sem resposta de negócio inventada.

## Serialização e compatibilidade

JSON camelCase, GUIDs não vazios, enums comerciais string, quantidades decimais e dinheiro em centavos inteiros. Datas UTC aceitam Z e +00:00. Limite 200 linhas; serviços/peças preservam linhas distintas; máximo conjunto e correspondência de snapshot/mapeamentos são validações semânticas além do schema. Campos adicionais opcionais são tolerados; enums desconhecidos não são. Snapshot deve corresponder exatamente ao aceito no Operation, inclusive política, linhas e valores.

Envelope nativo application/vnd.masstransit+json; não usar wrapper próprio type/payload ou eventType/data. Publish mantém MessageId estável do evento, CorrelationId=OS e InitiatorId=CausationId. Respostas a novo comando semanticamente repetido têm novo ID e causação do novo comando, sem incrementar versão.

## Gerar e verificar

```powershell
dotnet run --project tools/Operation.ContractArtifacts --configuration Release -- docs/contracts/runtime-v1
python -m pip install -r scripts/contract_requirements.txt
python scripts/validate_contracts.py
dotnet pack src/Operation.Contracts --configuration Release --output artifacts/contracts
```

O gerador exporta schemas a partir da metadata CLR e exemplos determinísticos. Testes verificam URN MassTransit, roundtrip, campos extras e enum inválido; Python valida schemas/fixtures e casos negativos. CI regenera em pasta temporária e compara JSON com os arquivos versionados. Exemplos são casos independentes, não um log cronológico.

## Histórico e homologação

A pasta v1 contém a proposta de 24/10, anterior à paridade e ao código. **Não usar aqueles schemas/exemplos para o runtime atual.** Esta baseline substitui requestedJobIds por linhas completas, adiciona peças/política e preserva identidade BudgetJobId; não aceita conclusão parcial. As incompatibilidades foram corrigidas antes da publicação do preview, mas ainda devem ser revisadas com o grupo.

Infra-broker foi lido: ele propõe gearflow.events com topic/routing key e envelope próprio; Inventory já usa MassTransit. Testes Operation usam vhosts isolados e topologia MassTransit, sem modificar o infra-broker. A escolha coletiva de envelope/topologia/owner do pacote/ACL ainda precisa ser formalizada antes de conectar serviços no ambiente compartilhado.
