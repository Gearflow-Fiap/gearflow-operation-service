# ADR-014: Manter casos de uso explícitos e pipeline único

**Status:** Accepted localmente no M5. **Date:** 2026-10-29. **Escopo:** organização da aplicação do Operation. Mantém a estrutura existente; não representa aprovação coletiva de contratos, dono da OS/Saga ou entrega remota.

## Context

Após M1–M4, OperationService contém 11 comandos do mesmo agregado WorkOperation, distribuídos em cinco arquivos parciais por responsabilidade: recebimento, diagnóstico, execução, administração e pipeline. Seu construtor tem três dependências: IOperationUnitOfWork, DiagnosticPricer e TimeProvider. As cinco consultas já estão separadas em OperationQueries, que usa IOperationReadStore. O catálogo próprio tem IJobCatalog e não está concentrado nesse serviço de operação.

A API e o adapter de mensagens chamam os mesmos casos de uso. OperationCommandHandler em Infrastructure implementa validação de envelope, inbox, deduplicação de transporte e confirmações; ele não é um handler de aplicação nem coordenador da Saga. Há diferenças semânticas deliberadas: StartDiagnostic do contrato aciona Receive; StartExecution do contrato aciona AuthorizeExecution. Início físico é ação HTTP distinta.

O pipeline compartilhado gera fingerprint, procura receipt antes de conferir versão/consultar preços, opera sobre estado destacado e entrega estado/receipt/outbox a um único Commit com CAS. Há testes de replay histórico, conflito de intenção, falha de commit, cancelamento e repetição sem nova precificação.

## Decision

Manter OperationService como fachada explícita de comandos, OperationQueries para leitura e as partes por responsabilidade. Não extrair handlers individuais nem adicionar MediatR nesta etapa. Não introduzir dispatcher/mediator próprio ou transações em endpoints/consumers para reorganizar arquivos.

A separação atual já permite localizar o caso de uso e testar o domínio/aplicação sem HTTP, EF ou MassTransit. Nos onze comandos avaliados, a unidade de consistência e as regras compartilhadas são as mesmas. Criar onze wrappers apenas para encaminhar chamadas aumentaria os registros e pontos de navegação sem retirar uma responsabilidade concreta do fluxo.

Preservar as fronteiras: transportes validam sua entrada/identidade; aplicação coordena intenção e mudança; domínio protege invariantes; adapter PostgreSQL garante atomicidade/CAS; inbox do broker protege identidade da mensagem e recuperação de transporte. Esses mecanismos são complementares. O receipt da aplicação não substitui a inbox, e mover ambos para um behavior genérico exige demonstrar a equivalência de suas semânticas.

## Alternatives Considered

| Alternativa | Benefício possível | Custo/limite neste código | Decisão atual |
|---|---|---|---|
| Serviço explícito e arquivos por responsabilidade | Fluxo direto, um pipeline e reaproveitamento por HTTP/mensagens | Pricing é dependência do serviço completo, embora só CompleteDiagnostic consulte preços | Manter |
| Handlers por caso de uso, sem mediator | Isolar dependências quando comandos crescerem de forma independente | Exigiria uma abstração compartilhada de execução e mudança de todos os chamadores, ou wrappers sem ganho | Reavaliar quando houver responsabilidade independente |
| Handlers com MediatR | Dispatch e behaviors padronizados quando houver fluxos que precisem deles | Nova dependência, contratos internos/DI e risco de reordenar validação, replay e transação durante migração | Não adotar agora |

Esta escolha não afirma que MediatR ou handlers sejam inadequados em geral. O critério é um ganho demonstrável neste serviço; contagem de arquivos ou semelhança com o monólito não basta.

## Consequences

O pipeline e os contratos existentes permanecem iguais. Não há pacote, código de produção, migration ou teste novo para impor uma preferência de framework. A regressão de aplicação/arquitetura existente é reexecutada como evidência da avaliação.

Há um acoplamento conhecido: DiagnosticPricer é resolvido com OperationService, mesmo para comandos que não consultam preços. A ausência de configuração continua suportada; configuração inválida do adapter pode falhar ao resolver suas dependências, conforme a validação lazy do M2. Se esse acoplamento exigir isolamento operacional dos comandos sem preço, avaliar primeiro a separação de CompleteDiagnostic e seu caminho de resolução, com testes desse isolamento. Adicionar um mediator por si só não resolve isso.

Reabrir a decisão quando houver dependências exclusivas relevantes em vários comandos, coordenações de aplicação independentes, necessidade comprovada de behaviors compartilhados ou testes que exijam muitos doubles sem relação com o caso de uso. Analisar também se surgiu outro limite de domínio antes de apenas repartir classes.

Qualquer migração futura deve preservar action/fingerprint, ator, normalização do payload, replay antes de versão/consulta externa, CAS e um único commit de estado/receipt/outbox. Deve manter a distinção entre autorização comercial e início físico e passar pelos testes HTTP, PostgreSQL e RabbitMQ pertinentes. Uma comparação de handlers precisa demonstrar esse ganho e essa equivalência antes da adoção.

## Related

- [Mapa dos casos de uso e critérios de evolução](../development/APPLICATION_USE_CASES.md).
- [ADR-007 — intenções e casos de uso](adr-007-casos-de-uso-e-intencoes.md).
- [ADR-008 — persistência](adr-008-persistencia-postgresql.md).
- [ADR-011 — mensageria](adr-011-mensageria-operation.md).
- [Plano M1–M5](../planning/CODE_QUALITY_PLAN.md).
