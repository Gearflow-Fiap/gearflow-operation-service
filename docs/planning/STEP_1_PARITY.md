# Passo 1 — matriz de paridade e escopo do Operation

**Data:** 28/10/2026. **Natureza:** escopo local para a extração; não é implementação completa nem aprovação coletiva de contratos. **Fonte:** código e testes locais da fase 3. [Manifesto com caminhos e SHA-256](PARITY_MANIFEST.json) registra os arquivos efetivamente comparados. Nenhum teste do monólito foi executado nesta etapa; seus cenários foram lidos como referência.

## Delimitação do serviço

**Operation escreve:** estado técnico de diagnóstico/reparo, dados operacionais recebidos da OS, itens diagnosticados, snapshot das linhas aprovadas, progresso e histórico local. **OS escreve:** abertura, alterações cadastrais da ordem, desativação/estado global/entrega e coordenação se mantida a Saga no OS. **Budget escreve:** orçamento, aprovação/rejeição e pagamentos. **Inventory escreve:** estoque/reserva/liberação/consumo. Identity/Customers permanecem suporte externo durante transição.

Não consumir os nove eventos financeiros para disparar efeitos de Saga dentro do Operation se o OS já os coordena. Consumo para projeção local é opcional e precisa de necessidade explícita, não pré-requisito para modelar o trabalho técnico.

## Inventário completo do recorte

Todas as pastas de casos de uso Workshop e Catalog têm destino abaixo. PAR-25..29 cobrem pontos externos importantes ao fluxo. Estado parcial do núcleo não é marcado como paridade concluída apenas por possuir método com nome parecido.

| ID | Caso da fase 3 | Dono na divisão proposta | Responsabilidade Operation |
|---|---|---|---|
| PAR-01 | Abrir OS | OS | Receber snapshot operacional, sem criar outra OS |
| PAR-02 | Iniciar diagnóstico | Operation | Preservar início apenas a partir de Received |
| PAR-03 | Concluir diagnóstico e gerar orçamento | Operation + Budget | Operation conclui diagnóstico e fornece resultado; Budget gera orçamento |
| PAR-04 | Aprovar orçamento por OS | Budget + coordenação + Inventory | Somente aceitar autorização operacional após verificações externas |
| PAR-05 | Aprovar orçamento por BudgetId | Budget + coordenação + Inventory | Mesmo limite de responsabilidade de PAR-04 |
| PAR-06 | Rejeitar orçamento por OS | Budget + coordenação + OS | Receber cancelamento coordenado, sem escrever no Budget |
| PAR-07 | Rejeitar orçamento por BudgetId | Budget + coordenação + OS | Não duplicar decisão do cliente |
| PAR-08 | Executar item de serviço | Operation | Manter progresso próprio da linha aprovada |
| PAR-09 | Retomar execução após falta de peças | Coordenação + Inventory + Operation | Retomar somente depois de confirmação externa de reserva |
| PAR-10 | Finalizar OS e consumir estoque | Operation + coordenação + Inventory + OS | Concluir execução local e comunicar; não consumir diretamente |
| PAR-11 | Entregar veículo | OS | Fora da escrita Operation |
| PAR-12 | Desativar OS | OS + Operation | Propagar desativação/inativação local antes do corte |
| PAR-13 | Atualizar veículo | OS + Operation | Aplicar atualização coordenada enquanto diagnóstico não foi concluído |
| PAR-14 | Listar OS por prioridade | OS (global) + Operation (fila local) | Preservar dados/ordem de referência e documentar evolução da fila |
| PAR-15 | Consultar OS e histórico | OS (global) + Operation (local) | Consultar detalhe operacional sem duplicar estado global |
| PAR-16 | Consultar OS com orçamento | OS/composição de consulta + Budget | Fornecer sua parte operacional; não consultar tabela Budget |
| PAR-17 | Consultar orçamento | Budget | Fora do Operation |
| PAR-18 | Médias de duração | Operation (diagnóstico/reparos) + OS (global/entrega) | Manter duração técnica e separar tempo de fila no alvo |
| PAR-19 | Reposição automática | Inventory + coordenação + Operation | Receber autorização de retomada; não assinar reposição para reservar por conta própria |
| PAR-20 | Criar mão de obra | Operation proposto (contexto Catalog) | Destino candidato, revisão do grupo pendente; separar das regras WorkOperation |
| PAR-21 | Atualizar mão de obra | Operation proposto (contexto Catalog) | Destino candidato, revisão do grupo pendente; separar das regras WorkOperation |
| PAR-22 | Excluir mão de obra | Operation proposto (contexto Catalog) | Destino candidato, revisão do grupo pendente; separar das regras WorkOperation |
| PAR-23 | Listar mão de obra | Operation proposto (contexto Catalog) | Destino candidato, revisão do grupo pendente; separar das regras WorkOperation |
| PAR-24 | Consultar mão de obra | Operation proposto (contexto Catalog) | Destino candidato, revisão do grupo pendente; separar das regras WorkOperation |
| PAR-25 | Notificar orçamento | Budget | Não copiar envio financeiro/estoque para Operation |
| PAR-26 | Notificar aprovação | Budget/OS | Não copiar envio financeiro/estoque para Operation |
| PAR-27 | Notificar falta de estoque | Inventory/coordenação | Não copiar envio financeiro/estoque para Operation |
| PAR-28 | Notificar estoque mínimo | Inventory | Não copiar envio financeiro/estoque para Operation |
| PAR-29 | Consulta pública de status | OS | Fora da API de mecânico |

## Regras, lacunas e testes por caso

### PAR-01 — Abrir OS

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/CreateServiceOrder/CreateServiceOrderHandler.cs`.
- Regra observada: VehicleId, JobIds (ao menos um), Parts/quantidade; OSCode pertence à OS.
- Lacuna atual: Receive atual só guarda ServiceOrderId; faltam dados iniciais.
- Teste atual relacionado: `EmptyOrderIsRejected`. Relação parcial, não prova de paridade.
- Aceite futuro: AT-01, AT-02; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-02 — Iniciar diagnóstico

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/StartDiagnostic/StartDiagnosticHandler.cs`.
- Regra observada: Registra autor/data/histórico e recusa estado inválido.
- Lacuna atual: Guard existe; entrada ainda sem veículo/itens.
- Teste atual relacionado: `InvalidTransitions_DoNotChangeStateOrHistory`, `CompleteFlow_WithStockDelay_RecordsLocalHistory`. Relação parcial, não prova de paridade.
- Aceite futuro: AT-04, AT-14; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-03 — Concluir diagnóstico e gerar orçamento

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/FinalizeDiagnostic/FinalizeDiagnosticHandler.cs`.
- Regra observada: Serviços/peças vêm da OS; consumíveis entram no diagnóstico; preços consultados no servidor.
- Lacuna atual: CompleteDiagnostic atual só muda estado, sem itens/preços.
- Teste atual relacionado: `ApplicationCommitsStateAndPendingEventTogether`. Relação parcial, não prova de paridade.
- Aceite futuro: AT-05, AT-06, AT-07, AT-18; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-04 — Aprovar orçamento por OS

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/ApproveBudget/ApproveBudgetHandler.cs`.
- Regra observada: Legado aprova, reserva e entra em execução ou espera por peças.
- Lacuna atual: Bool stockReserved não é um contrato de autorização completo.
- Teste atual relacionado: `ApprovalWithStockAndRejectionEmitDifferentEvents`. Relação parcial, não prova de paridade.
- Aceite futuro: AT-10, AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-05 — Aprovar orçamento por BudgetId

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/ApproveBudgetById/ApproveBudgetByIdHandler.cs`.
- Regra observada: Resolve OS pelo orçamento; não muda o significado da aprovação.
- Lacuna atual: Não implementar segunda aprovação no Operation.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-06 — Rejeitar orçamento por OS

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/RejectBudget/RejectBudgetHandler.cs`.
- Regra observada: Rejeição cancela OS em AwaitingApproval.
- Lacuna atual: RejectBudget local é provisório; substituir fronteira por cancelamento.
- Teste atual relacionado: `Rejection_CancelsOnlyWhileAwaitingApproval`. Relação parcial, não prova de paridade.
- Aceite futuro: AT-09, AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-07 — Rejeitar orçamento por BudgetId

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/RejectBudgetById/RejectBudgetByIdHandler.cs`.
- Regra observada: Resolve OS a partir do BudgetId e cancela.
- Lacuna atual: Rejeição não deve virar consumer financeiro com efeitos duplicados.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-08 — Executar item de serviço

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/ExecuteJob/ExecuteJobHandler.cs`.
- Regra observada: Exige InExecution e BudgetJobId; desconhecido falha, repetido retorna conflito.
- Lacuna atual: JobProgress.JobId é ambíguo; distinguir linha de catálogo.
- Teste atual relacionado: `ExecutionRejectsUnknownAndRepeatedJobs`. Relação parcial, não prova de paridade.
- Aceite futuro: AT-11, AT-12; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-09 — Retomar execução após falta de peças

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/ResumeExecution/ResumeExecutionHandler.cs`.
- Regra observada: Reavalia reserva antes de avançar; falta de estoque mantém espera.
- Lacuna atual: ResumeExecution atual não consulta adapter nem prova autorização. 
- Teste atual relacionado: `CompleteFlow_WithStockDelay_RecordsLocalHistory`. Relação parcial, não prova de paridade.
- Aceite futuro: AT-10, AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-10 — Finalizar OS e consumir estoque

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/FinalizeServiceOrder/FinalizeServiceOrderHandler.cs`.
- Regra observada: Exige InExecution; não exige todos os jobs marcados; consume reserva antes do sucesso global.
- Lacuna atual: Núcleo usa Finalized local e não integra consumo.
- Teste atual relacionado: `FinalizationPreservesLegacyGuardWithoutNewJobRule`. Relação parcial, não prova de paridade.
- Aceite futuro: AT-13, AT-18, AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-11 — Entregar veículo

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/DeliverServiceOrder/DeliverServiceOrderHandler.cs`.
- Regra observada: Apenas Finalized -> Delivered; mede intervalo até entrega.
- Lacuna atual: Não adicionar Deliver no Operation.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-12 — Desativar OS

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/DeactivateServiceOrder/DeactivateServiceOrderHandler.cs`.
- Regra observada: Soft-delete só Received/InDiagnostic; não confundir com rejeição de orçamento.
- Lacuna atual: Não há IsActive nem tratamento de desativação no núcleo.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-09, AT-16; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-13 — Atualizar veículo

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/UpdateServiceOrderVehicle/UpdateServiceOrderVehicleHandler.cs`.
- Regra observada: Código permite Received/InDiagnostic e bloqueia a partir de AwaitingApproval (mesmo antes da aprovação efetiva).
- Lacuna atual: Não há VehicleId nem versionamento de snapshot.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-08; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-14 — Listar OS por prioridade

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/GetServiceOrders/GetServiceOrdersHandler.cs`.
- Regra observada: Paginação normaliza page>=1 e pageSize 1..100; prioridade definida no repository.
- Lacuna atual: ExecutionQueue é em memória, sem IsActive/paginação.
- Teste atual relacionado: `QueueUsesLegacyPriorityAndExcludesTerminalOperations`, `QueueUsesOldestFirstWithinStatus`. Relação parcial, não prova de paridade.
- Aceite futuro: AT-15, AT-16; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-15 — Consultar OS e histórico

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/GetServiceOrderById/GetServiceOrderByIdHandler.cs`.
- Regra observada: 404 quando não existe; histórico ordenado por data no DTO.
- Lacuna atual: Sem caso de uso de consulta local completo.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-14, AT-15; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-16 — Consultar OS com orçamento

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/GetServiceOrderDetails/GetServiceOrderDetailsHandler.cs`.
- Regra observada: Legado junta OS e orçamento via repositories do mesmo módulo.
- Lacuna atual: Composição distribuída ainda não definida.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-17 — Consultar orçamento

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/GetBudgetById/GetBudgetByIdHandler.cs`.
- Regra observada: Retorna itens, preços, aprovação e IDs de linha.
- Lacuna atual: A autorização precisa trazer snapshot suficiente para não ler BudgetDB.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-11, AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-18 — Médias de duração

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/UseCases/GetExecutionAverage/GetExecutionAverageHandler.cs`.
- Regra observada: Diagnóstico: InDiagnostic->AwaitingApproval; execução: InExecution->Finalized; finalização: Finalized->Delivered.
- Lacuna atual: Há timestamps, mas não consulta/métrica equivalente nem regra de amostra. 
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-17; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-19 — Reposição automática

- Fonte: `gearflow-app/src/Workshop/Workshop.Application/EventHandlers/PartsReplenishedHandler.cs`.
- Regra observada: Filtra OS cujo orçamento referencia item reposto e tenta reservar novamente.
- Lacuna atual: Reavaliar OS em espera fica fora do núcleo Operation.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-20 — Criar mão de obra

- Fonte: `gearflow-app/src/Catalog/Catalog.Application/UseCases/CreateJob/CreateJobHandler.cs`.
- Regra observada: Nome obrigatório, preço >=0 e descrição vazia quando nula.
- Lacuna atual: CRUD catálogo não implementado; caso de uso operacional deve usar porta de consulta de preços.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-22; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-21 — Atualizar mão de obra

- Fonte: `gearflow-app/src/Catalog/Catalog.Application/UseCases/UpdateJob/UpdateJobHandler.cs`.
- Regra observada: Valida nome/preço e preserva ID.
- Lacuna atual: CRUD catálogo não implementado; caso de uso operacional deve usar porta de consulta de preços.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-22; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-22 — Excluir mão de obra

- Fonte: `gearflow-app/src/Catalog/Catalog.Application/UseCases/DeleteJob/DeleteJobHandler.cs`.
- Regra observada: Legado remove fisicamente o Job; não há IsActive.
- Lacuna atual: CRUD catálogo não implementado; caso de uso operacional deve usar porta de consulta de preços.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-22; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-23 — Listar mão de obra

- Fonte: `gearflow-app/src/Catalog/Catalog.Application/UseCases/GetJobs/GetJobsHandler.cs`.
- Regra observada: Consulta catálogo pelo repository.
- Lacuna atual: CRUD catálogo não implementado; caso de uso operacional deve usar porta de consulta de preços.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-22; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-24 — Consultar mão de obra

- Fonte: `gearflow-app/src/Catalog/Catalog.Application/UseCases/GetJobById/GetJobByIdHandler.cs`.
- Regra observada: Consulta por ID, com falha quando ausente.
- Lacuna atual: CRUD catálogo não implementado; caso de uso operacional deve usar porta de consulta de preços.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-22; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-25 — Notificar orçamento

- Fonte: `gearflow-app/src/Notifications/Notifications.Application/EventHandlers/BudgetGeneratedEmailHandler.cs`.
- Regra observada: Evento in-process aciona notificação no contexto Notifications.
- Lacuna atual: Dono exato de aviso interno deve ser consolidado; sem efeito no núcleo.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-26 — Notificar aprovação

- Fonte: `gearflow-app/src/Notifications/Notifications.Application/EventHandlers/BudgetApprovedNotificationHandler.cs`.
- Regra observada: Evento in-process aciona notificação no contexto Notifications.
- Lacuna atual: Dono exato de aviso interno deve ser consolidado; sem efeito no núcleo.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-27 — Notificar falta de estoque

- Fonte: `gearflow-app/src/Notifications/Notifications.Application/EventHandlers/StockMissingNotificationHandler.cs`.
- Regra observada: Evento in-process aciona notificação no contexto Notifications.
- Lacuna atual: Dono exato de aviso interno deve ser consolidado; sem efeito no núcleo.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-28 — Notificar estoque mínimo

- Fonte: `gearflow-app/src/Notifications/Notifications.Application/EventHandlers/LowStockAlertHandler.cs`.
- Regra observada: Evento in-process aciona notificação no contexto Notifications.
- Lacuna atual: Dono exato de aviso interno deve ser consolidado; sem efeito no núcleo.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

### PAR-29 — Consulta pública de status

- Fonte: `gearflow-app/src/Host/GearFlow.Api/Endpoints/Externals/Public/ExternalStatusEndpoints.cs`.
- Regra observada: Consulta OS por id; TODO do legado: clientId não é verificado como dono.
- Lacuna atual: Não copiar lacuna de autorização; Operation permanece API operacional.
- Teste atual relacionado: não há teste Operation específico deste comportamento. Relação parcial, não prova de paridade.
- Aceite futuro: AT-21; consultar [plano de testes](STEP_ACCEPTANCE.md).

## Dados que o modelo do passo 2 precisa representar

| Dado | Origem | Uso / restrição |
|---|---|---|
| ServiceOrderId e código legível | OS | ID comum obrigatório; OSCode é referência de exibição, sem nova sequência gerada no Operation |
| VehicleId | Abertura/atualização OS | Não perder veículo; aceitar alteração coordenada apenas antes da conclusão do diagnóstico |
| RequestedJobs | OS | Pelo menos um serviço na abertura, como CreateServiceOrderHandler; preservar identidade do catálogo |
| RequestedParts | OS | PartId e quantidade inteira; não depender de ServiceOrderDto atual, que omite essa coleção |
| DiagnosticConsumables | Ação de diagnóstico | ConsumableId e quantidade decimal; separar dados informados de preços consultados |
| Diagnostic result | Operation | Snapshot imutável quando concluído; itens e quantidades preservados, fonte/instante de preço rastreáveis |
| BudgetId e approved lines | Autorização coordenada | Referenciar linhas aprovadas; definir relação BudgetJobId/LineId sem confundir com Catalog.JobId |
| Executed jobs | Operation | Data e autor por linha; sem escrever no banco Budget |
| History/CreatedAt/Version | Operation | Auditoria e ordenação; versão será usada também na concorrência futura |
| Active/canceled/failure | OS/Operation | Desativação não deve permitir novo trabalho; cancelar precisa distinguir reparo já iniciado |

Essa tabela fixa necessidades do núcleo local, não publica mensagens novas em nome dos outros integrantes. Wire format/casing/namespace e revisão do contrato permanecem em revisão.

## Preservação e mudanças explícitas

| ID | Assunto | Como tratar |
|---|---|---|
| CHG-01 | Aprovação/reserva/pagamento antes agrupados | Separação obrigatória de responsabilidades; ordem comercial e posição da Saga ainda propostas |
| CHG-02 | Autorizar fila versus início físico | Melhoria proposta: ReadyForExecution separado de InExecution; não afirmar que já existia no legado |
| CHG-03 | Conclusão local versus global | Usar resultado local distinto; confirmação Inventory/estado OS fora do Operation |
| CHG-04 | Snapshot de entrada incompleto | Corrigir proposta StartDiagnostic antes da integração: incluir peças e regra de serviços não vazios; atualização de veículo requer fluxo próprio |
| CHG-05 | IDs de execução | Legado usa BudgetJobId. LineId estável de ponta a ponta é proposta, não equivalência automática; mapeamento explícito e teste |
| CHG-06 | Preço ausente | GetValueOrDefault atual pode virar zero; no alvo falhar sem conclusão parcial. Zero cadastrado explicitamente continua válido |
| CHG-07 | Total de consumíveis | Budget legado soma PriceCents sem multiplicar Quantity; não mudar regra financeira silenciosamente. Acordar cálculo/arredondamento com Budget antes de produzir valores interoperáveis |
| CHG-08 | Fila | Legado exclui Finalized/Delivered, mas não Canceled; núcleo atual também exclui Canceled. Registrar essa correção. Fila acionável com ReadyForExecution é proposta de UX, não paridade literal |
| CHG-09 | Idempotência de job | Legado retorna conflito ao executar mesma linha duas vezes. Retry HTTP/mensagem deve devolver resultado estável na camada de intenção, sem executar duas vezes; não chamar conflito do domínio de exactly-once |
| CHG-10 | Catálogo | Dono Operation é proposta. Delete legado é físico; desativação proposta anteriormente é mudança de comportamento, ainda não implementada |
| CHG-11 | Itens livres no fim do diagnóstico | Legado usa serviços/peças já solicitados e recebe só consumíveis. API proposta com items livres é extensão; baseline dos passos 2/3 preserva pedido inicial, sem substituição silenciosa |
| CHG-12 | Troca de veículo | Código bloqueia ao entrar em AwaitingApproval, não somente após aprovação efetiva; comentário legado é impreciso. Preservar guard observado |
| CHG-13 | IsActive | Transition do legado não testa IsActive; não reproduzir permissão de trabalhar em operação desativada. Documentar validação de estado inativo como correção |
| CHG-14 | Cancelamento pré-criação/falha | Tombstone/DiagnosticFailed e disputa com início são necessidades distribuídas novas, sem prova nos testes atuais |
| CHG-15 | Validação de dados iniciais | Handler legado só exige serviços não vazios; construtores aceitam IDs/quantidades sem guard. Validar GUIDs/quantidades no alvo é reforço explícito de qualidade, não teste de uma regra já implementada no monólito |

## Ajustes a aplicar depois do checkpoint

- Passo 2: completar o modelo, identidades e invariantes; nenhum adapter externo necessário para testar esses dados.
- Passo 3: casos de uso e portas com preços/snapshots/queries; testar falhas sem banco real. Proposta de catálogo pode ser consumida por porta; migrar CRUD é incremento de suporte posterior se o grupo confirmar o dono.
- Posterior: banco/EF, API real, integração de preços autenticada, pacote comum e broker. Esses recursos não serão declarados prontos ao fechar os passos 2/3.
- Antes de interoperar: validar contratos reais do Budget, snapshot OS, dono do catálogo, cálculo de consumíveis, nomes/namespace e políticas de autorização.
- Não há bloqueio para iniciar domínio local após o checkpoint: representar dados e regras não exige fingir que os contratos externos já foram aceitos.

## Condição de conclusão do passo 1

Concluir inventário com fontes, delimitação e lacunas explícitas; mapear critérios de teste e executar a suíte existente do Operation com cobertura. Conclusão significa planejamento de paridade concluído; nenhum item funcional acima é declarado implementado por este documento. Resultados e ponto de parada ficam no [acompanhamento](EXECUTION_TRACKER.md).
