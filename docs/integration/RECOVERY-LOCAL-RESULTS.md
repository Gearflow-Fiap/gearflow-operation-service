# Recuperacao local OS–Operation

Execucao: 11/11/2026, Docker Compose, APIs HTTP e RabbitMQ reais.
Runner: `scripts/test-os-operation-local.ps1`.

| Cenario | OS criada | Resultado |
|---|---|---|
| BrokerRecovery | fe126adb-94e9-4850-9c09-e9ce58e3a7b4 | Aprovado |
| OperationRecovery | 2427f0d8-b7c3-4836-8f26-58841b86f420 | Aprovado |
| OsRecovery | dbf8a575-c561-4895-b4ee-aac8e9a60ebc | Aprovado |

BrokerRecovery: broker parado, OS criada por HTTP, um StartDiagnostic pendente na
outbox e nenhuma operacao no destino. Broker religado em finally; APIs reconectaram
sem reinicio manual. Uma operacao persistida com versao inicial 1 e um registro de
historico. Diagnostico iniciado, replay exato manteve versao 2, conclusao versao 3
e snapshot recebido pela OS.

OperationRecovery: consumidor Operation parado, OS publicada pelo produtor real;
outbox marcada e destino ainda sem operacao. Apos religar a API, mesmas verificacoes
de recebimento unico, replay e retorno do diagnostico passaram.

OsRecovery: OS parada apos o inicio do diagnostico. Operation concluiu e marcou o
evento da versao 3 como publicado, enquanto o snapshot ainda era null na OS. Apos
religar o consumidor OS, o snapshot foi recebido e persistido.

Consultas SQL foram somente de leitura para verificacao externa; os servicos nao
acessaram bancos alheios. Nenhum volume foi removido. Containers foram religados.
Essas evidencias comprovam os casos exercitados, nao entrega exactly-once, ausencia
de duplicatas em qualquer janela de crash, nem o ciclo comercial completo.

Pendencias: autorizacao via Billing, pagamento/Mercado Pago, execucao comercial,
consumo de estoque e compensacoes financeiras. Estes cenarios nao foram substituidos
por eventos sinteticos nem considerados aprovados pelos testes de recuperacao.
