# Caracterização anterior à refatoração

Baseline do repositório: 5034401df6143c777a1f4203ce182b8a272f6430 (M2). Primeiro foram adicionados três testes em HttpInputContractTests e compilada a solução sem alteração no código de produção.

A primeira execução revelou uma expectativa incorreta no teste: If-Match vazio é omitido pelo transporte, chegando como ausente e retornando 428, não 400. A expectativa foi corrigida. Para recompilar somente o teste preservando os assemblies de produção anteriores, usou-se `dotnet build tests/Operation.IntegrationTests/Operation.IntegrationTests.csproj -c Release --no-restore -m:1 -nr:false -p:BuildProjectReferences=false`.

A execução confirmada dos três casos foi aprovada, zero falhas/ignorados, em TestResults/quality-m3-before-confirmed/miran_MIRANDA_2026-10-29_20_06_34_net10.0.trx:

- Todas as oito escritas com versão rejeitam precondições inválidas antes de mutar estado. Matriz com 13 variações de header por rota.
- Idempotency-Key é validado antes de If-Match nas operações; criação/atualização do catálogo não passam a exigir chave de idempotência.
- Shape de consumíveis, limites e motivo inválido mantêm seus códigos HTTP e não deixam estado/receipt/outbox parciais.

Esses mesmos cenários fazem parte da regressão final com os assemblies recompilados após a refatoração. Execuções focadas/repetidas não são somadas como novos casos distintos.
