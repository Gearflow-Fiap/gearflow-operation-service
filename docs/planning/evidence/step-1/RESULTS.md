# Passo 1 — evidência de testes

**Data:** 28/10/2026. **SDK:** .NET 10.0.402. **Configuração:** Release. **Resultado:** 19/19 aprovados, 0 falhas, 0 não executados. Processo terminou com código 0.

## Comando executado

```powershell
dotnet test GearFlow.Operation.slnx --no-restore --configuration Release -m:1 -nr:false --collect:"XPlat Code Coverage" --logger "trx;LogFileName=step-1.trx" --results-directory TestResults/step-1-2026-10-28
```

O ambiente restrito utilizou DOTNET_CLI_HOME gravável e comunicação local do VSTest liberada. O restore pré-existente foi reutilizado; não houve atualização de dependências.

## Cobertura atual

| Assembly | Linhas | Branches |
|---|---|---|
| Operation.Application | 100.00% | 100.00% |
| Operation.Domain | 100.00% | 100.00% |

Total instrumentado: 91/91 linhas e 44/44 branches.

- [Relatório Cobertura XML](coverage.cobertura.xml).
- [Resumo com resultados individuais e hashes](test-summary.json).
- TRX original: `TestResults/step-1-2026-10-28/step-1.trx` na raiz do repositório (ignorado pelo Git). SHA-256: `d513cd5d714ebe77d1584e4e31a10b6f3f6bc4f69d0e3f66b38c96fd5a99d616`.

O gate de cobertura existente será conferido sobre esta pasta canônica, que contém apenas um relatório de cobertura, evitando duplicatas de anexos do TRX.

## Limites da evidência

Esses testes validam o núcleo atual, não os dados/regras ainda ausentes. O teste ApplicationCommitsStateAndPendingEventTogether usa fake: não prova transação de banco ou outbox. CommitFailurePropagatesWithoutClaimingSuccess testa falha na criação e não comprova rollback de um agregado já carregado por referência. Nenhum teste de banco, broker, JWT/API ou Saga foi executado. Os testes de origem Workshop/Catalog foram inspecionados, não executados.

O passo 1 é inventário/escopo e não implementa código de produção; não se criaram testes artificiais para um comportamento que só será implementado nos passos 2/3. A matriz de aceite registra o trabalho de testes que falta.

## Validação da paridade documental

[Resultado da conferência](parity-validation.json): inventário completo dos handlers do recorte, hashes de fonte, referências a testes/critérios e links internos. Os arquivos de runtime permaneceram iguais ao início do passo. Isso valida o inventário e a não alteração de produção, não a implementação dos cenários futuros.
