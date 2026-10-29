# Arquitetura de referência da fase 4

**Status:** Proposed. **Data:** 2026-10-24.

## Evidências e limites

Inspeção de código: gearflow-app, núcleo Operation e Inventory (contratos, consumers, EF/outbox, autenticação). Budget: README e fluxo-saga, sem código funcional. OS ainda não implementado segundo o responsável. Infra-k8s, infra-database, frontend e lambda não foram inspecionados nesta baseline. Resultados de testes declarados no README Inventory não foram reexecutados nesta análise.

## Contextos e propriedade

| Contexto | Dono | Banco de referência | Integração |
|---|---|---|---|
| OS e coordenação | OS Service | SQL Server planejado no Budget | API + Saga persistida |
| Diagnóstico, fila e reparos | Operation | PostgreSQL proposto | API staff + comandos/eventos |
| Catálogo de mão de obra | Operation, proposta | Mesmo banco próprio Operation | CRUD + snapshots |
| Orçamento, aprovação, cobrança | Budget/Billing | MongoDB planejado | Comandos/eventos + Mercado Pago |
| Peças, insumos e reservas | Inventory | PostgreSQL implementado | API de catálogo/preço + comandos/eventos |
| Identity e Customers | Monólito durante transição, proposta | Banco existente | API pela borda; não compartilhar tabelas |
| Notificações | Contexto responsável pelo aviso | Banco do participante | Budget: orçamento/pagamento; Inventory: estoque; OS: veículo pronto |

A diversidade SQL/NoSQL atende ao planejamento da solução quando MongoDB for efetivamente implementado. Não é necessário adicionar NoSQL ao Operation para repetir o requisito. Banco próprio significa credenciais, schema/banco e migrations sob controle do serviço; não presumir acesso cruzado mesmo numa instância compartilhada. A organização física definitiva depende da infraestrutura.

Não criar agora um serviço separado de catálogo nem um orquestrador em outro repositório. Um bounded context não exige um deploy exclusivo se não houver necessidade clara.

## Fluxos e interfaces

- Frontend usa gateway para HTTP; cada API valida JWT e autorização. Não chamar o broker do navegador.
- Saga envia comandos direcionados; participantes publicam eventos. Broker não toma decisões de negócio.
- REST interno só para consultas necessárias (por exemplo, preços), com autenticação de serviço, timeout e falha explícita. Não usar credencial do usuário como segredo de integração.
- Ninguém acessa banco de outro serviço. Dados necessários chegam em snapshots ou consultas autorizadas.
- Cada serviço possui deploy independente; um conjunto de manifests de dev compartilhado pode orquestrar a demonstração sem criar build acoplado entre repositórios.

## Estado global versus local

OS é a visão para o cliente e dona do histórico global. Operation guarda histórico técnico e diagnóstico; Budget guarda estado financeiro; Inventory guarda reserva. Estados locais não são sincronizados por escrita direta nem precisam ter enums idênticos.

Alvo Operation: Received -> InDiagnostic -> AwaitingExecutionAuthorization -> ReadyForExecution -> InExecution -> ExecutionCompleted. DiagnosticFailed aguarda cancelamento e bloqueia conclusão posterior. Canceled é terminal pré-início físico. Espera por pagamento/estoque fica na Saga. O domínio do passo 2 já usa os estados técnicos alvo; os adapters e contratos wire ainda não foram implementados. [Modelo local](../planning/STEP_2_MODEL.md).

## Filiais

O enunciado contextualiza expansão nacional, mas não detalha segregação. Não presumimos multi-tenancy, algoritmo de distribuição ou RBAC completo. Antes de dados reais, decidir BranchId, escopo de acesso, estoque por filial e compatibilidade dos contratos. Os contratos v1 desta baseline são para uma operação lógica/filial; adicionar filial com impacto de autorização exige nova revisão, não somente um campo opcional ignorado.

## Migração incremental

Identity/Customers ficam temporariamente no gearflow-app. Extrair Catalog e Workshop por responsabilidade. Rotas migradas devem ter um único dono; gateway muda rota por rota. Na transição, marcar endpoints de escrita antigos como descontinuados/desabilitados quando o novo assumir; não fazer dual-write entre monólito e serviço.

Histórico legado pode ser exportado/importado com mapeamento explícito de IDs e estados, antes do corte de tráfego. Não foi implementada migração de dados. Não converter OS já executada em uma nova Saga que cobraria ou consumiria estoque outra vez.

## Critérios transversais

Cobertura >=80% por serviço, um fluxo BDD completo, Sonar ou equivalente, CI/CD independente, Kubernetes, healthchecks, OpenTelemetry e logs correlacionados. Schema/contrato versionado não substitui testes integrados. [Plano e evidências](../planning/IMPLEMENTATION_PLAN.md).
