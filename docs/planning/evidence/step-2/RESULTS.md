# Passo 2 — resultado da validação

**Data:** 28/10/2026. **SDK:** .NET 10.0.402. **Configuração:** Release. **Resultado:** 72 aprovados, 0 falhas, 0 ignorados; exit code 0. Inclui domínio e regressão do esqueleto Application após evolução das assinaturas.

## Comando

```powershell
dotnet test GearFlow.Operation.slnx --no-restore --configuration Release -m:1 -nr:false --collect:"XPlat Code Coverage" --logger "trx;LogFileName=step-2.trx" --results-directory TestResults/step-2-final-2026-10-28
```

Testes também compilaram a solução com TreatWarningsAsErrors habilitado. Reutilizado restore existente, sem atualização de pacotes. DOTNET_CLI_HOME gravável e comunicação local VSTest disponíveis no ambiente restrito.

## Cobertura

| Assembly | Linhas | Branches |
|---|---|---|
| Operation.Application | 100.00% | 100.00% |
| Operation.Domain | 100.00% | 97.72% |

Total: **342/342 linhas**, **150/153 branches**. Cobertura de linhas 100%; branches 98.03%. Não criar testes artificiais/reflexão sobre estados impossíveis só para atingir 100% de branches.

- [Cobertura XML](coverage.cobertura.xml).
- [Resumo e resultados individuais](test-summary.json).
- TRX original ignorado pelo Git: `TestResults/step-2-final-2026-10-28/step-2.trx`, SHA-256 `a481bbe6a41851b4eacb610293a024fd57550d71150eb363e5603b0d7436b1ba`.

## Verificações adicionais

- Gate de 80% de linhas por assembly via script existente.
- Formatação C# conferida com `dotnet format whitespace . --folder --include src tests --verify-no-changes --verbosity quiet`.
- `git diff --check`, links de documentação e referências dos critérios conferidos ao fechar o passo.
- Fontes/testes antes e depois identificados no manifesto de alterações do passo 2; nenhum arquivo dos outros repositórios alterado.

A formatação por solution/MSBuild inicialmente não conseguiu conectar ao named pipe no sandbox. O modo folder de whitespace foi usado com sucesso; não declarar análise semântica de estilo/Roslyn/Sonar por esse comando.

## Limites

Snapshot preserva preços/totais de política identificada, sem impor nova fórmula financeira de insumos. Fonte confiável/política de precificação devem ser resolvidas na aplicação. Tipos de domínio não são contratos wire aprovados. Nenhum banco, broker, API, JWT, Saga ou BDD foi executado. Testes de ordem start/cancel são sequenciais: disputa entre transações depende de persistência real.

O fake da aplicação continua uma regressão do núcleo inicial, não prova rollback de agregado carregado. Receive duplicado, criação de tombstone pela aplicação, consultas/precificação por portas e journal permanecem no passo 3. Não confundir 100% de linhas do que existe com conclusão do serviço.
