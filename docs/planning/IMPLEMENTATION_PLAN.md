# Plano de implementação da fase 4

**Baseline:** 2026-10-24. **Escopo desta etapa:** planejar/documentar; sem publicar pacote, implantar ou mudar runtime dos serviços.

## Sequência ativa de execução

Em 28/10/2026, o usuário solicitou três passos locais: (1) paridade/escopo, (2) modelo operacional, (3) casos de uso. A conclusão e a evidência de cada um ficam no [acompanhamento](EXECUTION_TRACKER.md). Executar somente o passo 1 antes de retornar ao usuário. Esta sequência precede/complementa os marcos P0-P8 abaixo e não exige fingir que houve acordo externo para testar domínio local.

## Estado de partida

Operation tem Domain/Application e 19 testes locais. Inventory tem implementação de banco/broker/contratos inspecionada; não reexecutamos seus testes. Budget tem documentação; OS ainda não existe segundo o grupo. A nova arquitetura é alvo proposto, não descrição do núcleo já pronto.

## Sequência de incrementos

| Marco | Trabalho | Dependências | Critério de conclusão |
|---|---|---|---|
| P0 — contrato de referência | Revisar RFCs/ADRs, fechar identidade/snapshot, fluxo comercial e cancelamento | Responsáveis de todos os serviços | Versão da baseline e decisões aceitas com nomes/PRs |
| P1 — contrato executável | Records do pacote comum, fixtures, publicação e simulador da Saga | Owner/registry da RFC-003 | Cada consumidor usa versão fixa; roundtrip MassTransit validado |
| P2 — domínio alvo | Separar ReadyForExecution/InExecution; DiagnosticId/LineId/snapshot; CancelOperation/falha/tombstone; catálogo | P0/P1 | Guards, fluxos, corrida conceitual e totais testados; exemplos coerentes |
| P3 — persistência | PostgreSQL/EF, migrations, concorrência, unidade de trabalho isolada, inbox/outbox/journal | Infra local + P2 | Banco real comprova atomicidade, índices, tombstone e disputa start/cancel |
| P4 — API e preços | Endpoints staff, ETag/intenção HTTP, catálogo, adapter preço, ProblemDetails/OpenAPI | Claims/credencial e preços RFC-002 | API testada; cliente bloqueado; falha de preço não conclui parcialmente |
| P5 — mensageria | Consumers StartDiagnostic/StartExecution/CancelOperation, eventos tipados, retry/DLQ/traces | P1/P3/P4 | Simulador completa fluxo; duplicatas e reinício seguros com RabbitMQ real |
| P6 — integração/Saga | Integrar OS/Budget/Inventory; corrigir diagramas externos; BDD e compensação | Serviços/simuladores disponíveis | Fluxo integrado e falhas com evidência; simulador não conta como Saga final |
| P7 — entrega | Docker, manifests/CI/CD, Sonar, proteção main, telemetria e documentação final | Infra/registry/segredos | Deploy independente, >=80%, probes, rastreamento e checks obrigatórios |
| P8 — corte e demonstração | Rotas de gateway/frontend, dados de teste, desativar donos antigos, vídeo/PDF | P6/P7 | Um dono por escrita; sem dual-write; vídeo <=15 min e PDF com links/participantes |

P2/P3 podem ser desenvolvidos com fixtures locais e simulador, sem esperar OS completo. Não prometer integração enquanto tipos/semântica estiverem divergentes. Estimativas dependem da divisão de tarefas e prazo do grupo, ainda não informados.

## Mudanças necessárias no núcleo existente

| Atual | Alvo | Impacto |
|---|---|---|
| AwaitingApproval/WaitingParts locais | AwaitingExecutionAuthorization; espera comercial na Saga | Migrar enum/queries e testes; não serializar enum atual como wire |
| AcceptExecution(stockReserved bool) já inicia | StartExecution valida snapshot e fica ReadyForExecution | Remover bool externo como prova livre; início físico separado |
| Finalized local | ExecutionCompleted local | Evitar confundir conclusão com estado global OS |
| JobProgress.JobId opaco | Progresso por LineId comercial, com ItemId de catálogo | Novos snapshots e mapeamento de dados |
| CancelDiagnostic/RejectBudget locais | CancelOperation comandado pela Saga | Tombstone e resposta/rejeição idempotentes; Budget não cancela diretamente participante |
| CompleteDiagnostic sem itens | Snapshot de itens, nomes, preços, totais e DiagnosticId | Consulta autorizada de preço; sem fallback zero |
| OperationEvent string Type | Records tipados versionados do pacote | Manter envelope interno só se útil à outbox, nunca publicar como contrato |
| Interface Commit sem adapter | Adapter transacional real | Atomicidade comprovada em integração |
| Fila herdada inclui espera comercial | Fila acionável e filtros de espera | Mudança proposta de UX; validar com frontend |

## Donos de integração a combinar

Operation: núcleo, API, catálogo, banco e consumers locais. OS: Saga, timeouts e estado global. Budget: orçamento, pagamento, reconciliação e compensações financeiras. Inventory: reserva/liberação/consumo e preços. Infra: bancos, broker, cluster, secrets, registry e observabilidade compartilhada. Frontend: rotas, ETag, estados locais/globais e experiência de fila. Este plano não atribui nomes/prazos sem acordo do grupo.

## Rastreabilidade do enunciado

| Exigência | Entrega planejada / evidência |
|---|---|
| >=3 serviços independentes | OS/Operation/Budget/Inventory com repos, banco e deploy próprios |
| SQL + NoSQL na solução | PostgreSQL/SQL Server + MongoDB efetivamente implementado |
| Mensageria e sem banco alheio | Contratos tipados, APIs de consulta e credenciais isoladas |
| Saga com compensação | OS persistido; cancelamento confirmado e compensações idempotentes |
| Unitários e >=80% por serviço | CI com coverage abrangendo todas as novas camadas |
| Pelo menos um BDD completo | Cenário OS -> pagamento -> execução -> consumo e caminho de falha |
| Qualidade automatizada | Sonar ou equivalente como gate real, pendente de configuração |
| CI/CD Kubernetes e main protegida | Pipelines por repo, imagem versionada, migrations/rollout, checks do host |
| Monitoramento fase 3 | OTLP/New Relic, logs e métricas; trace fim a fim |
| Dockerfile/manifests/API docs | Artefatos reais a produzir em P4/P7, não apenas lista de pastas |
| Vídeo <=15 min | Fluxo completo, compensação, CI/deploy e trace |
| PDF final | Integrantes/IDs, links, diagrama, Saga e justificativas |

## Fora do incremento inicial

Reabertura de OS, múltiplas revisões comerciais, descontos/aprovação parcial, escalonamento inteligente de mecânicos, multi-tenancy e migração automática de históricos ainda não possuem requisitos fechados. Não são funcionalidades implementadas. Filiais precisam de decisão explícita de isolamento antes de declarar suporte nacional.

## Próxima ação concreta

Revisar RFC-001 com os responsáveis, especialmente pagamento antes de estoque, cancelamento com confirmação, contratos e catálogo. Depois implementar P1/P2 com simulador e testes; banco/API/mensageria seguem na ordem acima.
