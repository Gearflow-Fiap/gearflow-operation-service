# M5 — avaliação de handlers

Concluído localmente em 29/10/2026. Baseline: 1fd3fb709a1b8b501a04eb661536db94817645d0.

## Resultado da avaliação

[ADR-014](../../../architecture/adr-014-casos-de-uso-e-handlers.md) registra a decisão de manter OperationService explícito e seu pipeline único, sem novos handlers/MediatR. Onze comandos do mesmo agregado, três dependências e cinco consultas já separadas não demonstraram benefício suficiente para introduzir dispatch/behaviors ou onze wrappers.

A decisão compara alternativas e explicita o acoplamento conhecido do pricer resolvido no serviço completo. Define gatilhos de reavaliação baseados em dependências/coordenações independentes e isolamento operacional. Uma extração futura precisa preservar fingerprint, ator, replay antes de versão/preço, CAS e commit atômico de estado/receipt/outbox.

[Mapa dos casos de uso](../../../development/APPLICATION_USE_CASES.md) documenta método/arquivo/entrada/responsabilidade e o limite entre aplicação e inbox. StartDiagnostic recebido por mensagem chama Receive; StartExecution recebido por mensagem chama AuthorizeExecution. Essas entradas não são o início físico HTTP. [Inventário obtido do código](use-case-inventory.json).

## Validação executada nesta etapa

- Build Release dos nove projetos .NET: zero erros/avisos.
- **168 testes unitários existentes aprovados**, zero falhas/ignorados, incluindo aplicação, pricing, arquitetura e equivalência do modelo EF. Nenhum teste novo criado para impor uma escolha de framework.
- Diff de src, testes, pacotes, configuração de build, solução e CI vazio. Etapa exclusivamente documental.
- Links locais dos documentos do M5 verificados e git diff --check aprovado. Resumo em [validation.json](validation.json); relatório TRX em TestResults/quality-m5, ignorado pelo Git.

A última regressão completa e cobertura são as do M4: 294 testes .NET + 13 Python, cobertura mínima 95,79%. São [referência anterior](../quality-m4/RESULTS.md), não reexecução completa do M5. Não houve mudança de produção que justificasse repetir PostgreSQL/RabbitMQ/Docker nesta avaliação documental. Os testes unitários acima foram reexecutados no M5; nenhuma contagem anterior foi somada a eles.

## Conclusão e retomada

A sequência local M1–M5 está concluída. O plano aceitava manter a estrutura atual caso uma separação não apresentasse ganho. As melhorias M1–M4 permanecem e esta decisão orienta evolução futura, sem congelar a arquitetura.

Nenhum commit, push, deploy ou reconstrução de imagem realizado pelo agente. Fase 4 completa continua dependendo do 7B: definição do dono OS/Saga, alinhamento de contratos/topologia/credenciais, homologação com serviços reais, cenários distribuídos e entrega/qualidade remotas.
