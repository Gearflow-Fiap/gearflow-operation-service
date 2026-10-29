# Plano de evolução da organização e qualidade

Iniciado em 29/10/2026, após Scalar/F5 funcionando. Esta sequência M1–M5 é distinta dos passos de implementação iniciais e dos incrementos de integração 7B.

Baseline: 231 casos .NET distintos e 13 testes Python aprovados no checkpoint Scalar; contratos de integração preview, fluxos de idempotência/concorrência e migrations já existentes. Evidência nova por passo, sem reutilizar contagens antigas como prova de mudanças novas.

| Passo | Escopo | Aceite | Estado |
|---|---|---|---|
| M1 — Scalar e contrato HTTP | Resumos/descrições, respostas tipadas/erros, headers e exemplos; testes de autorização/documentação descobrindo as rotas | Toda rota de negócio documentada e Staff; OpenAPI coerente com respostas reais; falhas de autenticação sem efeitos; build/testes/gate aprovados | Concluído localmente; evidências M1 |
| M2 — composição e configuração | Registro DI por camada e opções tipadas para JWT, Inventory, RabbitMQ, preços e telemetria | Program enxuto; lifetimes e adapters preservados; configuração inválida rejeitada no ponto correto, sem imprimir segredos; dependências opcionais não viram obrigatórias | Concluído localmente; evidências M2 |
| M3 — entrada HTTP | Validação de requests, leitura do ator e parser de ETag reutilizáveis | Mesmos códigos HTTP, precondições e limites; ator confiável na API e explícito nos comandos/consumers; domínio continua protegendo suas invariantes | Concluído localmente; evidências M3 |
| M4 — persistência e convenções | IEntityTypeConfiguration por entidade, versões NuGet centralizadas e regras de estilo/analisadores | Modelo EF equivalente, sem migration artificial; build Docker lê novos arquivos de configuração; versões preservadas e gate de estilo adotado incrementalmente | Planejado |
| M5 — avaliar handlers | Avaliar casos de uso individuais e dependências após os passos anteriores | Registrar decisão e justificativa; somente extrair handlers se houver ganho, com equivalência do pipeline transacional; MediatR não é requisito | Planejado |

## Regras para todos os passos

- Uma entrega verificável por vez; ao concluir, registrar código, testes, cobertura e limites, comunicar o checkpoint e indicar a próxima etapa.
- Preservar rotas, corpos, status HTTP, namespaces/URN e contratos de integração. Qualquer mudança de comportamento identificada deve ser tratada explicitamente, não escondida numa reorganização.
- Preservar replay antes da consulta externa/controle de versão, um único commit de estado/receipt/outbox e CAS. Não duplicar esses mecanismos em handlers ou filtros.
- Não mudar a política financeira, atribuir Saga ao Operation, consultar banco alheio nem copiar adapters SQL cross-BC do monólito.
- Não alterar migrations já existentes para reorganizar código. Comparar o modelo antes/depois de M4.
- Executar testes apropriados à mudança e manter cobertura >=80% por assembly. Testes de contrato devem descobrir rotas novas e falhar se perderem proteção/documentação.
- Não fazer commit, push, publicação ou deploy como parte deste plano local. Infraestrutura/integração coletiva continuam no acompanhamento 7B.

## M1 — detalhamento

1. Inventariar rotas de operação e catálogo pelo host ASP.NET, conferindo seus resultados e exceções reais.
2. Documentar intenção, entrada e resultado de cada rota; distinguir autorização comercial por mensagem e início físico por HTTP.
3. Declarar sucessos com DTOs reais e erros aplicáveis (ProblemDetails), sem prometer respostas que o runtime não produz.
4. Documentar ETag/If-Match, Idempotency-Key, paginação, centavos, identidade BudgetJobId e falha de preço. Incluir exemplos locais sem credenciais/dados reais.
5. Testar metadata e OpenAPI de todas as rotas de negócio, acesso anônimo/cliente, headers e exemplos; manter testes de Scalar Development/Production.
6. Gerar evidência e atualizar este plano/EXECUTION_TRACKER. Próxima etapa: M2.

## M2–M5 — cuidados de implementação

M2: manter JWT obrigatório, mensageria/OTLP condicionais às flags e o comportamento atual de preço indisponível sem configuração. Não criar defaults financeiros nem expor connection strings/segredos em mensagens de validação. Interfaces da aplicação não dependem de ASP.NET/configuração do host.

M3: validação de shape/headers na borda e regras do domínio preservadas. Compartilhar parser não deve exigir Idempotency-Key em endpoints de catálogo que não o exigem hoje. Não usar ausência de HttpContext como permissão automática de sistema.

M4: preservar schema, nomes, índices, constraints, conversões, concorrência e histórico EF em public. Atualizar restore/contexto Docker ao introduzir Directory.Packages.props. Não promover dezenas de sugestões a erros sem revisar impacto.

M5: considerar separação somente se responsabilidades/dependências justificarem. Se bastar manter o serviço explícito atual, registrar essa decisão como conclusão válida. Não implementar um framework próprio de mediator nem migrar para MediatR apenas para obter pastas diferentes.


## Checkpoint M1

16 rotas documentadas; cinco testes novos focados aprovados; suíte completa com 236 casos .NET e 13 Python aprovados, sem falhas/ignorados. Cobertura API 95,31%; demais assemblies >=98,93%. [Evidências](evidence/quality-m1/RESULTS.md).

Retomada: M2. Extrair composição da API/aplicação/infraestrutura e opções tipadas, preservando flags, lifetimes, validação de JWT e comportamento de preço indisponível. Não avançar para handlers ou mudanças de banco como parte de M2.

## Checkpoint M2

DI por camada e opções tipadas concluídas. 273 testes .NET (164 unitários + 109 integração) e 13 Python aprovados, sem falhas/ignorados. Cobertura API 95,12%; demais assemblies >=98,93%. [Evidências](evidence/quality-m2/RESULTS.md) e [configuração](../development/CONFIGURATION.md).

ConnectionStrings:Operation ausente agora falha na inicialização, sem abrir conexão antecipadamente. JWT obrigatório e integrações opcionais preservados; nenhuma política financeira padrão introduzida. Retomada: M3, sem avançar para handlers ou persistência nesta entrega.

## Checkpoint M3

Ator autenticado e ETag compartilhados, contexto HTTP de operações e requests organizados. Códigos/limites e distinção entre catálogo e comandos preservados. Três cenários caracterizados antes da refatoração; 17 novos casos incluídos no total final de 290 testes .NET (164 unitários + 126 no projeto de integração), com 13 Python aprovados, zero falhas/ignorados. API 95,79%; demais assemblies >=98,93%. [Evidências](evidence/quality-m3/RESULTS.md), [guia HTTP](../development/HTTP_INPUT.md).

Retomada: M4 — configurações EF por entidade e convenções/versões NuGet, verificando equivalência do modelo e compatibilidade do build Docker. Nenhuma alteração de banco/handler feita no M3.
