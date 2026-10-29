# ADR-011 — Mensageria do participante Operation

Status: implementada e em validação local no incremento 6; homologação coletiva pendente.

## Decisão

MassTransit.RabbitMQ 8.5.11, alinhado ao Inventory verificado no repositório, e contratos próprios sem dependência de domínio/EF. Pacote local GearFlow.Contracts.Operation 1.0.0-preview.1, namespace GearFlow.Contracts.Operation.V1. Não há cópia dos tipos Inventory nem publicação em registry. Proposta anterior v1 é histórica; runtime-v1 é gerado do código.

Cinco consumidores Send aplicam apenas ações locais da aplicação. Envelope/body são conferidos. Mensagem inválida ou MessageId divergente é permanente e vai para _error sem retry. Falha técnica tem três retentativas curtas (100/500/1000 ms), depois _error. Rejeição de estado/snapshot é resposta de negócio durável; não é falha transitória. Operation não consome PaymentConfirmed para iniciar reparo e não executa Saga financeira.

## Inbox e recuperação

broker_inbox registra MessageId, hash semântico serializado, comando e correlação ANTES do efeito. Payload inclui ordem dos arrays/data de origem; redelivery deve manter payload imutável. Lock consultivo por intenção serializa processamento. Registro inicial curto preserva hash mesmo em crash.

O journal de aplicação já grava receipt + estado + outbox na mesma transação. Consumidor usa esse journal e depois conclui a inbox/resposta em outra transação local. Não chamamos isso de transação única inbox/agregado: é um protocolo de recuperação em duas fases locais. Se cair após commit do agregado, retry encontra o receipt e conclui a inbox sem repetir efeito. Se cair antes, comando pendente é reexecutado. Se a inbox já concluiu, reentrega apenas confirma processamento. Não há limpeza automática de inbox/receipts/tombstones.

Mudança de estado já tem evento na outbox operacional; não criar outra resposta duplicada. Novo comando semanticamente repetido não gera versão, mas grava confirmação própria na inbox. Rejeições válidas também guardam resposta com ID estável. Ao contrário de erro de desserialização/metadata, elas podem ser publicadas com correlação segura.

## Dispatcher

Processa outbox operacional e respostas pendentes da inbox alternadamente. Seleção FOR UPDATE SKIP LOCKED permite réplicas sem marcar o mesmo registro ao mesmo tempo; não promete ordem global ou ordenação por OS entre réplicas. Cada destinatário deve usar identidade, causação e versão.

Publish tem prazo de 5s e publisher confirms; Mandatory impede marcar como entregue publicação sem fila de destino. Marcação só depois do Publish, na transação do registro. Falha entre Publish e commit pode reenviar a MESMA mensagem/ID: entrega ao menos uma vez, não exactly-once. Falha mantém pendência; worker registra erro e tenta novamente em ciclos de 2s. Mensagem interna corrompida não é descartada automaticamente: requer investigação, e pode bloquear o lote; observabilidade/reconciliação ficam para incremento 7.

Ativação explícita separada: Messaging:Enabled liga consumidores; Messaging:DispatchEnabled liga worker. Provisionar assinaturas duráveis do OS/consumidores antes de habilitar entrega; Mandatory verifica alguma rota, não que todos os consumidores necessários estejam inscritos.

## Topologia e infraestrutura

A infra clonada propõe exchange topic gearflow.events/envelope próprio, enquanto Inventory usa envelope/topologia MassTransit. Não adaptar silenciosamente um ao outro. Baseline Operation usa exchanges de tipos e filas fixas MassTransit em vhost isolado de teste. Não alteramos o infra-broker. É necessária revisão conjunta sobre topologia, ACL, owner do pacote e filas dos assinantes antes do ambiente compartilhado. Testes locais não provam Saga completa.

Cada teste cria/remova apenas seu vhost operation_it_<guid> e seu banco. Não reiniciar o broker compartilhado nem apagar filas existentes. TLS/usuários por serviço/permissões restritas e métricas precisam ser definidos no ambiente de entrega.

Referências: documentação oficial [mensagens](https://masstransit.massient.com/concepts/messages), [tratamento de falhas](https://masstransit.massient.com/concepts/exceptions), [outbox](https://masstransit.massient.com/concepts/outbox). API/configuração 8.5.11 também conferida no código Inventory e XML do pacote instalado; documentação online atual pode descrever versões posteriores.
