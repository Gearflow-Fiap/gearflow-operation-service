> Registro da análise inicial. Para a baseline de planejamento posterior, contratos e evolução dos estados, consultar [docs/INDEX](INDEX.md). As propostas novas distinguem autorização de início físico e substituem pontos ainda abertos deste registro.

# GearFlow - análise da fase 4 e plano do Operation Service

## Base da análise

PDF SOAT - Fase 4 - Tech challenge, páginas 2 a 6; código local do gearflow-app; README e docs/fluxo-saga.md do gearflow-budget-service; código e ADRs do gearflow-inventory-service. O PDF define os critérios acadêmicos; os diagramas dos repositórios são propostas do grupo e precisam ser conciliados com o código. Não foram tratados como autorização para publicar, implantar ou alterar outros serviços.

## Requisitos obrigatórios do grupo

- Pelo menos 3 microsserviços independentes, cada qual com repositório, infraestrutura e banco próprios.
- Na solução, usar pelo menos um banco SQL e um NoSQL. A leitura adotada é diversidade entre serviços; o PDF não exige dois bancos em cada serviço.
- Comunicação assíncrona por mensageria; REST síncrono quando necessário. Proibido acessar banco alheio.
- Saga para transações críticas, com compensações e tratamento de falhas; escolha justificada no README.
- Unitários em todos os serviços e cobertura mínima de 80% por serviço; pelo menos um fluxo completo com BDD.
- CI/CD independente: build, testes, qualidade com SonarQube ou similar e deploy automático em Kubernetes.
- Main protegida, PR obrigatório e checagens automáticas.
- Reutilizar monitoramento e observabilidade da fase 3.
- Código, Dockerfile, manifests, pipelines, evidência de cobertura, documentação e Swagger ou Postman.
- Vídeo de até 15 minutos: fluxo completo entre serviços, Saga com falha/compensação, deploy automático com testes e rastreamento distribuído.
- PDF final: participantes e identificação, links dos repositórios e vídeo, diagrama geral, estratégia da Saga e justificativa das tecnologias e fronteiras.
- Pagamentos devem integrar com Mercado Pago (responsabilidade do Budget/Billing).

OS, Billing e Execução são exemplos sugeridos pelo PDF. A divisão concreta pode incluir Inventory separado, como já ocorre no grupo. Múltiplas filiais são contexto do desafio, mas não há especificação explícita de tenant, filial, escala de funcionários ou algoritmo de prioridade novo; confirmar necessidade antes de adicionar esses modelos.

## O que existia e para onde vai

| Origem no gearflow-app | Destino/ação |
|---|---|
| Workshop.Domain/ServiceOrder: StartDiagnostic e FinalizeDiagnostic | Operation gerencia estado operacional e histórico local |
| FinalizeDiagnosticHandler: preços, criação do Budget e e-mail | Operation fornece resultado do diagnóstico; Budget gera orçamento e envia e-mail; origem dos preços ainda precisa ser alinhada |
| ApproveBudgetHandler: aprova + reserva + inicia execução | Budget registra aprovação; OS/Saga coordena pagamento e ReserveStock; Operation só começa por comando autorizado |
| ExecuteJobHandler + BudgetJob.IsExecuted | Operation passa a manter o progresso local dos itens aprovados, sem escrever no banco do Budget |
| ResumeExecutionHandler + PartsReplenishedHandler | Saga reage a StockReplenished e solicita nova reserva; só então autoriza retomar Operation |
| FinalizeServiceOrderHandler | Operation termina execução e informa ExecutionFinished; Saga comanda ConsumeStock; Inventory confirma StockConsumed; OS atualiza estado global |
| DeliverServiceOrder e abertura/consulta global da OS | OS Service |
| SqlInventoryReservation / SqlPricingReader / SqlCustomerContactReader | Não portar o acesso SQL entre contextos; substituir por contratos/snapshots ou API do serviço responsável |
| ServiceOrderRepository.GetPagedByPriorityAsync | Fila operacional preserva prioridade por status e antiguidade; exclui terminais |
| ObservabilityExtensions e BusinessMetrics | Reutilizar padrões OpenTelemetry, logs com correlação, healthchecks e métricas no futuro host |

Prioridade do legado: InExecution, AwaitingApproval, AwaitingPartsOrConsumables, InDiagnostic, Received. A fila nova exclui Canceled e Finalized. Delivered fica no OS Service. O progresso não exige concluir todos os jobs para finalizar porque o legado não tinha esse bloqueio; revisar com o grupo se quiserem alterar a regra.

## Evidências novas dos serviços clonados

Inventory implementa .NET 10, PostgreSQL próprio, RabbitMQ/MassTransit 8.5, transação local, inbox/outbox e contratos GearFlow.Contracts. Essa análise inspecionou os arquivos; não reexecutou os testes do Inventory nem validou a infraestrutura.

Budget contém apenas documentação: .NET 8 e MongoDB são planejados, não implementação verificada. Os diagramas chamam Operation de gearflow-execution-service e ainda colocam reserva/baixa de estoque dentro dele. Isso diverge do Inventory separado e deve ser corrigido no desenho compartilhado.

O ADR-0003 do Inventory propõe: ExecutionFinished -> OS/Saga -> ConsumeStock -> Inventory -> StockConsumed -> OS Finalized. Está marcado Proposed, portanto não é uma decisão final do grupo. O núcleo novo permite esse fluxo sem baixar estoque por conta própria. Não desfazer fisicamente uma execução já iniciada; falhas depois desse ponto exigem recuperação/retry e intervenção definida pelo grupo.

## Primeiro incremento implementado

- Domain e Application independentes em .NET 10, acompanhando o monólito e o Inventory.
- Estado operacional, histórico com autor/data, snapshot de IDs de serviços e progresso por item.
- Diagnóstico, autorização de execução após aprovação/reserva externa, espera por estoque, retomada, conclusão e rejeição.
- CancelDiagnostic idempotente antes da execução, sem tentar desfazer trabalho físico.
- Fila por status e antiguidade, sem operações canceladas/finalizadas.
- Porta de unidade de trabalho exige commit atômico de agregado e evento pendente; nenhuma implementação de persistência foi simulada como produção.
- CorrelationId igual ao ServiceOrderId, conforme os documentos do grupo.
- Testes de fluxo local, guards, fila, rejeição, compensação local e falhas no commit.

OperationEvent é um registro INTERNO provisório para a porta, não um contrato RabbitMQ. Nomes com .v1 não representam contratos publicados. Os payloads de diagnóstico (itens, quantidades, preços e identificadores), snapshots aprovados e identidade dos jobs ainda precisam virar contratos tipados compartilhados. Não publicar esse envelope genérico como se fosse suficiente para o Billing.

## Próximos incrementos, em ordem

1. Alinhar os contratos com o responsável pelo OS: StartDiagnostic cria entrada na fila; API inicia o trabalho; DiagnosticStarted e DiagnosticFinished devolvem resultado. StartExecution só após pagamento/reserva conforme decisão da Saga. Definir CancelDiagnostic, cancelamento antes do início físico, ExecutionFinished e falhas.
2. Definir se os IDs de progresso identificam linha do orçamento ou serviço de catálogo; hoje são IDs opacos únicos. Confirmar quem fornece preços de mão de obra (Catalog ainda no monólito) e peças/insumos (endpoint em lote pendente no Inventory).
3. Implementar Infrastructure com banco exclusivo (PostgreSQL é a proposta coerente com os diagramas), EF, migrations, concorrência otimista, inbox/outbox e deduplicação por MessageId e regra de negócio. Não referenciar projetos de outros repositórios por caminho local.
4. Adicionar API autenticada para fila, detalhes/histórico, iniciar/finalizar diagnóstico, executar job e finalizar execução; ProblemDetails, OpenAPI e healthchecks reais.
5. Adicionar MassTransit/RabbitMQ, consumers e contratos tipados compatíveis com GearFlow.Contracts, retry, DLQ e rastreamento. Compensações precisam resistir a duplicatas, mensagens atrasadas e concorrência, inclusive cancelamento antes da criação (tombstone).
6. Testes de banco/broker reais; BDD integrado com os outros serviços e simulador enquanto OS não existir. O fluxo unitário local NÃO atende sozinho ao BDD completo do PDF.
7. CI de build/test/coverage com gate >=80%, Sonar, Dockerfile, manifests K8s, deploy independente e configuração de branch protection. A cobertura atual só mede o núcleo existente; terá de incluir as novas camadas.
8. Reutilizar observabilidade da fase 3: OTLP/New Relic, Prometheus, logs com TraceId e CorrelationId, erros de integração, duração de diagnóstico/execução e alertas de DLQ.
9. Consolidar README, diagrama compartilhado, evidências de cobertura e roteiro do vídeo/PDF de entrega.

## Decisões pendentes

- Confirmar Saga orquestrada no OS e RabbitMQ/MassTransit como escolha coletiva (há documentação nessa direção).
- Banco próprio do Operation; MongoDB do Billing atende NoSQL somente quando estiver implementado.
- Momento do pagamento: documentação Budget propõe pagar antes de reservar/iniciar execução, diferente do fluxo antigo sem pagamento.
- Finalização global após StockConsumed, proposta do Inventory; política para falha após execução física.
- Aprovação/rejeição de orçamento versus comando de compensação: participantes obedecem a Saga e não orquestram outros serviços.
- IDs e versões do pacote GearFlow.Contracts; ownership, publicação e compatibilidade dos contratos.
- Escopo de filiais e autorização por filial; regras de prioridade e atribuição de mecânico, caso requeridas pelo grupo.
- Infraestrutura: consultar gearflow-infra-k8s e gearflow-infra-database antes de definir manifests/deploy/observabilidade.

## Repositórios adicionais

Já examinados: app, operation, budget e inventory. O OS ainda não está implementado, conforme informado. Precisaremos de infra-k8s e infra-database para deploy e observabilidade; lambda para autenticação se o contrato não estiver claro; frontend ao ajustar rotas/DTOs. gearflow-legado só é necessário se houver regra ausente ou divergente no monólito atual. Não é necessário clonar todos agora.

## Validação deste incremento

19 testes passaram, sem falhas ou ignorados. Cobertura do núcleo: 101/101 linhas e 44/44 branches (100%). Relatório preservado em docs/coverage/initial-core.cobertura.xml. Workflow de CI preparado com build/test/gate de 80% por assembly; execução remota, Sonar e deploy ainda pendentes.
