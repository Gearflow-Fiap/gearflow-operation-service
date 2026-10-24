# Testes, contratos e qualidade

## Evidência atual

[Baseline de cobertura](../coverage/README.md): 19 testes unitários locais passaram em 2026-10-24. Domain/Application medidos: 101/101 linhas e 44/44 branches. Não houve nova execução de testes de negócio nesta etapa documental nem testes distribuídos. CI preparado ainda não foi verificado remotamente.

## Estratégia alvo

| Camada | Verificação necessária |
|---|---|
| Domain | Estados válidos/inválidos, snapshot, identidade da linha, progresso, falha de diagnóstico, início físico e cancelamento |
| Application | Preço ausente/zero explícito/indisponível, snapshot imutável, total fracionário, falha de commit sem evento |
| API | JWT staff/customer, ProblemDetails, ETag/If-Match, idempotência, paginação e OpenAPI |
| Banco PostgreSQL real | Migrations, índices únicos, concorrência start/cancel, tombstone, rollback estado/outbox e reinício |
| Broker RabbitMQ real | URN/envelope reais MassTransit, inbox/outbox, duplicatas, replay, erro permanente e DLQ |
| Contratos | Todos os exemplos contra a definição correta do schema; serialização/deserialização dos records reais por produtor/consumidor |
| Saga integrada / BDD | OS, Budget, Inventory e Operation no caminho feliz e compensação, sem acessar bancos alheios |

Não usar apenas mocks para comprovar atomicidade e concorrência. Na validação do planejamento, conferir JSON/links/semântica; isso não substitui roundtrip do pacote real.

## Cenários obrigatórios antes do corte

1. Diagnóstico -> aprovação/pagamento -> reserva -> autorização -> início físico -> conclusão -> consumo -> finalização.
2. OS somente com mão de obra: zero comandos de reserva/consumo, sem travar aguardando resposta inexistente.
3. Consumível fracionário: total correto por linha, soma checada e mesma quantia no Budget/pagamento.
4. Reservas de mesmo item em linhas distintas: agrupar, enviar uma linha Inventory e consumir uma vez.
5. Cancelamento vence início: reserva só liberada depois do fence; início atrasado rejeitado.
6. Início vence cancelamento: OperationCancellationRejected; sem liberação/refund automáticos.
7. CancelOperation antes de StartDiagnostic: tombstone, sem ressuscitar operação.
8. Mensagens/intenção HTTP duplicadas: sem repetir progresso, histórico, reserva ou cobrança.
9. Banco cai antes do commit; processo cai após commit e antes do publish: rollback no primeiro caso e entrega via outbox no segundo.
10. StockInsufficient e reposição; insuficiência antiga depois de reserva confirmada não regride Saga.
11. Pagamento confirmado durante compensação: reconciliar/refund; sem autorizar reparo.
12. Falha no ConsumeStock após reparo: retry/DLQ/RecoveryRequired, sem fingir cancelamento físico.
13. Preço ausente/indisponível: sem snapshot parcial ou zero inventado.
14. Cliente com JWT válido não pode executar ação de mecânico.
15. Falha explícita DiagnosticFailed não aceita um finish posterior sem novo processo aprovado.

## CI/CD e evidências

Gate >=80% de linhas por serviço, incluindo código de produção das camadas adicionadas; documentar exclusões legítimas sem ocultar comportamento. Manter teste de contrato e cenário BDD com resultados reproduzíveis. SonarQube/Cloud ou similar precisa de projeto/credenciais configurados e gate obrigatório; ainda pendente. Build/test/qualidade antes de imagem e deploy Kubernetes. Branch main protegida com PR/checks no host Git, não apenas arquivo YAML.

Evidências para entrega: relatório de cobertura, resultado BDD, CI com qualidade/deploy, screenshot/trace da Saga, caso de compensação e vídeo <=15 minutos. Dados de demonstração sem credenciais/dados pessoais reais.
