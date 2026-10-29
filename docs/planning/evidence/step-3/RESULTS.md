# Passo 3 — validação do núcleo de aplicação

**Data:** 28/10/2026. **SDK:** .NET 10.0.402. **Configuração:** Release. **Resultado final:** 120 aprovados, 0 falhas, 0 ignorados, exit code 0. Os testes dos passos anteriores foram preservados/evoluídos, não são 120 casos novos.

## Comando executado

```powershell
dotnet test GearFlow.Operation.slnx --no-restore --configuration Release -m:1 -nr:false --collect:"XPlat Code Coverage" --logger "trx;LogFileName=step-3.trx" --results-directory TestResults/step-3-final-2026-10-28
```

Restore existente reutilizado, sem alteração de versões de pacote. Testes compilaram o código com TreatWarningsAsErrors habilitado. Usado DOTNET_CLI_HOME gravável e conexão local do VSTest autorizada.

## Cobertura

| Assembly | Linhas | Branches |
|---|---|---|
| Operation.Application | 100.00% | 98.67% |
| Operation.Domain | 100.00% | 97.72% |

Total: **632/632 linhas (100%)**, **278/283 branches (98.23%)**. Gate de 80% de linhas por assembly aprovado.

- [Relatório Cobertura](coverage.cobertura.xml).
- [Resumo com testes individuais](test-summary.json).
- TRX original (ignorado pelo Git): `TestResults/step-3-final-2026-10-28/step-3.trx`. SHA-256 `9571fd7d31e83125334609350e01b84b56abfc40dd3f2a1377117975608f7eac`.

## Cenários novos relevantes

- Fluxo local completo com preços autoritativos, snapshot, aprovação externa recebida como autorização e progresso por linha.
- Cotação ausente, incompleta, extra, duplicada, inválida, fora da janela, moeda divergente, zero explícito, fração/arredondamento e overflow.
- Replay de intenção antes de versão/preço; reuso divergente rejeitado; canonicalização de ordem/escala/cultura.
- Falha de commit após mutar cópia para diagnóstico/progresso: estado armazenado, receipt e eventos permanecem intactos, com retry possível.
- Dois comandos lendo mesma versão: protocolo CAS no double evita sobrescrita; mesma intenção concorrente só grava uma vez.
- Cancelamento pré-criação cria fence e bloqueia mensagens atrasadas; reparo iniciado não é desfeito.
- DTO congelado, paginação/filtros/history, páginas grandes sem overflow e duração com amostras por fase.

## Outras verificações

Whitespace C# formatado/conferido em modo folder e diff checado. Referências/testes/links revisados no checkpoint. Esta execução não realizou commit/push nem alterou o index. Ao fechar o checkpoint, HEAD contém o commit do passo 2 e o index coincide com ele; as alterações desta etapa permanecem no working tree. A conferência usa blobs Git, sem comparar incorretamente hashes de arquivos de trabalho com EOL diferente.

## Limites

Nenhum teste com PostgreSQL, RabbitMQ, API/JWT, preço externo ou Saga/BDD real foi executado. IsolatedStore é exclusivamente teste, com lock/CAS e falha antes de publicação atômica em memória; não comprova índices/isolamento/outbox do banco. O próximo incremento precisa implementar e testar os adapters reais. Duas políticas financeiras estão disponíveis sem default de produção; escolha coletiva ainda pendente.

[Manifesto das fontes e verificações do checkpoint](change-manifest.json).
