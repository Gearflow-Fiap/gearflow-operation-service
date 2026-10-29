# Incremento 4 — resultado

Data: 28/10/2026. PostgreSQL 17.11 real, instância temporária isolada no Windows. Docker Desktop aberto pelo usuário, mas acesso ao pipe do engine negado no ambiente do agente; não declarar teste Docker executado. Cada cenário criou e removeu seu próprio banco aleatório. Múltiplos contextos/conexões verificaram recuperação persistida; não foi ensaiado reinício abrupto do processo PostgreSQL.

## Execução

`dotnet test GearFlow.Operation.slnx --no-restore --configuration Release -m:1 -nr:false --collect:"XPlat Code Coverage" --results-directory TestResults/increment-4-final --logger trx`

- Operation.Tests: 130 aprovados.
- Operation.IntegrationTests: 14 aprovados.
- Total: 144; 0 falhas; 0 ignorados.
- Build Release sem warnings/erros; formatação whitespace e diff conferidos.

`python scripts/check_coverage.py TestResults/increment-4-final`

| Assembly | Linhas cobertas/instrumentadas | Cobertura |
|---|---|---|
| Operation.Application | 301/301 | 100% |
| Operation.Domain | 370/374 | 98,93% |
| Operation.Infrastructure | 919/933 | 98,50% |

Cobertura de Infrastructure inclui código de migrations/modelo gerado. Gate >=80% por assembly aprovado. Relatórios brutos TRX/Cobertura ficam no diretório TestResults local ignorado pelo Git; CI configurada para armazená-los como artifacts, ainda não executada remotamente.

## Comportamentos comprovados

Migrations up/down em banco vazio exclusivo; roundtrip completo; igualdade de estado e auditoria; rejeição de formato/corrupção; replay original e semântico; chaves reutilizadas com conteúdo divergente; rollback após SaveChanges inicial e após constraint real; corrida de criação e atualização por conexões independentes; cancelamento versus início físico; tombstone; listas e médias SQL; histórico paginado; ausência de carga do estado nas listas.

## Limites

Preços usam fake nos testes de integração. Outbox não publica e não possui dispatcher. Inbox do transporte, host API, JWT, consumidores, contrato financeiro/consumíveis definitivo e integração com OS/Budget/Inventory permanecem pendentes. Sem deploy e sem alteração em gearflow-app. Não declarar o serviço completo.
