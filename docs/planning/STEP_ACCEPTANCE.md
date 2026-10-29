# Critérios de qualidade dos passos 1 a 3

Atualizado em 28/10/2026. Cenários abaixo são **critérios a implementar/verificar**, não testes já aprovados. Baseline do passo 1 está em [evidência](evidence/step-1/RESULTS.md). Cada passo terá registro próprio, com comando, quantidade de testes, cobertura, limitações e próximo checkpoint.

## Passo 1 — validação de inventário e regressão

- Conferir que todo handler Workshop/Catalog de origem tem destino explícito na matriz.
- Conferir existência e hash dos arquivos fonte; todos os testes citados da suíte Operation devem existir.
- Revisar exceções de paridade (CHG-01..15) sem transformar proposta em requisito já aceito.
- Executar testes existentes em Release e coletar cobertura; preservar evidência do resultado.
- Conferir links da documentação e não alterar src/tests no passo de inventário.

Não criar testes de negócio artificiais só para aumentar quantidade neste passo documental. O teste significativo aqui é a regressão da suíte existente e a auditoria de completude das fontes. Testes novos serão adicionados com as implementações dos passos 2/3.

## Critérios de implementação

| ID | Cenário / resultado esperado | Camada / passo | Situação inicial |
|---|---|---|---|
| AT-01 | Receber e preservar OS/veículo/serviços/peças; não gerar nova OS nem perder peças por usar DTO incompleto | Domain 2 + Application 3 | Ausente no núcleo |
| AT-02 | Rejeitar OS/veículo/IDs vazios, zero serviços e quantidades inválidas sem mutação parcial; documentar novos guards versus legado | Domain 2 | Só OS vazia é coberta |
| AT-03 | Coleções/snapshot defensivos: modificar lista de entrada não altera dados aprovados/histórico | Domain 2 | Não comprova snapshot comercial |
| AT-04 | Diagnóstico inicia somente a partir de Received e operação ativa; transição inválida preserva estado/histórico | Domain 2 + Application 3 | Guard simples coberto; atividade ainda ausente |
| AT-05 | Finalização usa serviços/peças iniciais e consumíveis do diagnóstico; preserva quantidades e produz resultado comercial completo sem criar BudgetDB local | Domain 2 + Application 3 | Não implementado |
| AT-06 | Preço inexistente/consulta indisponível não vira zero nem altera estado; zero explicitamente cadastrado é permitido | Application 3 | Não implementado |
| AT-07 | Part inteiro, consumível decimal e identidade por linha; não perder linhas repetidas legítimas do mesmo catálogo; regra de total alinhada com Budget antes de integração | Domain 2 + Application 3 | Não implementado; cálculo comercial pendente |
| AT-08 | Veículo pode mudar em Received/InDiagnostic, bloqueia depois da conclusão do diagnóstico; comando/ordem atrasada não sobrescreve snapshot sem controle | Domain 2 + Application 3; concorrência real posterior | Não implementado |
| AT-09 | Diferenciar desativação pré-diagnóstico/conclusão de cancelamento compensatório; operação desativada não inicia; não desfazer reparo físico | Domain 2 + Application 3 | CancelDiagnostic parcial; IsActive ausente |
| AT-10 | Autorizar apenas snapshot válido; autorização não é reparo físico; esperar reserva externa quando necessária; comando financeiro não dispara efeito de estoque no Operation | Domain 2 + Application 3 | Bool atual inicia direto, ainda não atende |
| AT-11 | Progresso identifica linha aprovada, não catálogo; duas linhas do mesmo serviço têm progresso independente; desconhecida falha | Domain 2 + Application 3 | Teste atual não prova mapeamento BudgetJobId/LineId |
| AT-12 | Registrar data/autor por linha uma vez; retry de intenção não duplica efeito; distinguir conflito de domínio e replay idempotente | Domain 2 + Application 3; inbox real posterior | Só conflito por job repetido coberto |
| AT-13 | Conclusão exige execução e não adiciona bloqueio novo por todos os jobs; evento local não declara consumo/entrega global | Domain 2 + Application 3 | Guard simples existente |
| AT-14 | História/autor/timestamps/versão consistentes; falha não acrescenta transição de sucesso | Domain 2 + Application 3 | Histórico simples existente |
| AT-15 | Consultas locais retornam detalhe operacional, linhas e histórico; ausente é falha explícita; paginação normalizada e estável | Application 3 (DTO, sem HTTP) | Ausente |
| AT-16 | Fila prioriza trabalho conforme política explicitada; exclui inativas/terminais; preserva antiguidade e desempate determinístico | Domain 2 + Application 3 | Ordenação simples coberta; paginação/atividade faltam |
| AT-17 | Diagnóstico/reparo medidos nas respectivas janelas; tempo de fila separado no alvo; finalização->entrega permanece responsabilidade OS | Application 3; exportador posterior | Ausente |
| AT-18 | Validação/consulta/commit que falha não registra sucesso nem entrega evento; unidade de trabalho de teste deve usar cópia isolada para não mascarar mutação | Application 3; atomicidade de banco posterior | Fake atual só testa falha na criação, não prova rollback de alteração |
| AT-19 | Recebimento/autorização/cancelamento repetidos mantêm resultado consistente; mesmo ID com conteúdo divergente falha | Domain 2 + Application 3; persistência inbox posterior | DuplicateReceive hoje retorna conflito |
| AT-20 | Cancelamento e início disputam mesma operação; só um vence; cancelamento antes da criação impede mensagem atrasada de ressuscitar trabalho | Semântica 2/3; concorrência real em banco posterior | Não implementado |
| AT-21 | Limites: abertura/entrega no OS, aprovação/pagamento no Budget, saldo no Inventory; Operation só publica resultado local/aceita autorização | Revisão de dependências e testes 3; BDD real posterior | Arquitetura proposta, não integração comprovada |
| AT-22 | Catálogo: registrar destino de CRUD e guard de preço/nome; porta de preço permite testar núcleo sem assumir CRUD migrado | Inventário 1; suporte posterior conforme dono confirmado | Só inventariado |

## Pontos que não podem ser simulados como garantia pronta

Banco/transactions/outbox/inbox, concorrência entre processos, JWT/policies, RabbitMQ, serialização MassTransit, HTTP de preços e Saga/BDD completo terão verificações em incrementos próprios. Um fake que retém referência do agregado não prova rollback; interface Commit não implementa atomicidade.

A meta >=80% será mantida por serviço e novas camadas, mas 100% de linhas do núcleo pequeno não comprova requisitos que ainda não existem. Além de cobertura, exigir cenários de falha e invariantes acima.

## Saída esperada de cada próximo passo

**Passo 2:** novos testes de domínio significativos (dados iniciais, coleções defensivas, estados, linhas, atividade/cancelamento) aprovados e checklist AT atualizado; parar e avisar conclusão antes do passo 3.

**Passo 3:** testes de aplicação de diagnóstico/queries/autorização/progresso/conclusão e falhas usando portas e unidade de trabalho isolada; delimitar o que ainda depende de integração. Parar e relatar próximo incremento (banco/API/mensageria), sem declarar serviço completo.

## Situação após o passo 2

A tabela inicial é a fotografia anterior à implementação. Resultado atual por critério, sem presumir conclusão das camadas externas:

| ID | Situação no checkpoint 2 | Teste representativo |
|---|---|---|
| AT-01 | Domínio atendido; integração pendente | `IntakePreservesOrderVehicleRequestedItemsAndDistinctOccurrences` |
| AT-02 | Domínio atendido | `IntakeRejectsEmptyIdentities` |
| AT-03 | Domínio atendido | `IntakeCopiesCallerCollectionsAndExposesReadOnlyViews` |
| AT-04 | Domínio atendido; fronteira da aplicação pendente | `ActorIsRequiredAtIntakeAndMutationWithoutPartialChange` |
| AT-05 | Validação de snapshot no domínio atendida; obtenção de preços/caso de uso pendentes | `CompleteDiagnosticPreservesRequestedItemsAndFractionalConsumables` |
| AT-06 | Pendente no passo 3 | posterior |
| AT-07 | Quantidade/linhas no domínio atendidas; fórmula financeira depende de acordo | `FractionalConsumableAndExplicitZeroPriceAreValid` |
| AT-08 | Guard local atendido; propagação/concorrência externas pendentes | `VehicleChangesBeforeDiagnosticCompletionButNotAfter` |
| AT-09 | Domínio atendido; aplicação/persistência pendentes | `DeactivationIsDifferentFromCancellationAndBlocksFurtherWork` |
| AT-10 | Autorização local atendida; validações comerciais externas pendentes | `AuthorizationQueuesWorkWithoutClaimingPhysicalStartOrConsumingStock` |
| AT-11 | Domínio atendido; contrato externo de mapping pendente | `JobsAreExecutedByBudgetLineAndRepeatedCatalogJobsHaveIndependentProgress` |
| AT-12 | Guard/auditoria por linha atendidos; replay de intenção pendente | `JobsAreExecutedByBudgetLineAndRepeatedCatalogJobsHaveIndependentProgress` |
| AT-13 | Domínio atendido; entrega de evento externo pendente | `FinalizationPreservesLegacyRuleWithoutNewPendingJobGuard` |
| AT-14 | Domínio atendido | `VersionAndHistoryDescribeActualActionsInOrder` |
| AT-15 | Pendente no passo 3 | posterior |
| AT-16 | Fila local atendida; paginação/query pendentes | `ActionableQueueExcludesCommercialWaitInactiveFailedAndTerminalWork` |
| AT-17 | Timestamps representados; cálculo/query/exportação pendentes | posterior |
| AT-18 | Falhas de validação sem mutação atendidas; unidade de trabalho isolada/rollback pendentes | `SnapshotCannotOmitReplaceAddOrChangeRequestedItems` |
| AT-19 | Autorização/cancelamento locais idempotentes; recebimento/journal pendentes | `SemanticallyRepeatedAuthorizationDoesNotRegressWorkOrReplaceFirstAuthorization` |
| AT-20 | Interleavings locais atendidos; concorrência real/persistência posteriores | `CancellationAndPhysicalStartHaveMutuallyExclusiveSequentialOutcomes` |
| AT-21 | Fronteiras preservadas no domínio; integração/BDD posteriores | posterior |
| AT-22 | Sem mudança; catálogo continua suporte proposto | posterior |

[Evidência do passo 2](evidence/step-2/RESULTS.md) e [modelo implementado](STEP_2_MODEL.md).
