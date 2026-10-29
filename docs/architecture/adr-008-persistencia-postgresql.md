# ADR-008 — Persistência transacional de operações

Status: implementada no incremento 4; publicação de mensagens ainda pendente.

## Decisão

PostgreSQL 17 e EF Core 10 em Operation.Infrastructure. Banco exclusivo do serviço. Metadados consultáveis ficam em colunas relacionais; o estado composto usa JSONB versionado (formato 1), sem duplicar o histórico. A reidratação reaplica o histórico pelas regras de domínio e confere os metadados. Estado incompatível ou corrompido falha explicitamente.

Tabelas: operations, histories, command_receipts e outbox_events, no schema operation. Histórico é acrescentado por versão; receipts guardam fingerprint e resultado original. Não existe remoção automática de histórico, receipts ou tombstones nesta versão. Qualquer retenção futura deve preservar replay e bloqueio de recebimentos atrasados; alteração de formato/fingerprint exige migração explícita.

Cada commit usa uma transação: trava consultiva por intenção e operação em ordem determinística, consulta primeiro o receipt, verifica versão esperada, preserva o prefixo do histórico, grava o estado e depois histórico/receipt/outbox. Os dois SaveChanges pertencem à mesma transação. O token de concorrência Version também protege o UPDATE. As travas existem somente durante o commit; a consulta de preços ocorre antes. Timeout de lock e conflitos de concorrência são reportados à aplicação; não há retry cego de comando com preço novo.

Replay exato retorna o resultado gravado, mesmo após alterações posteriores. Mesmo intent com outro fingerprint falha. Replay sem mudança pode gerar receipt, sem nova versão ou evento. O journal de comandos é a proteção idempotente da aplicação; não substitui a futura inbox do transporte MassTransit.

Listas, filtros, paginação, histórico e médias são consultados no banco. Listas não carregam JSON nem todo o histórico. Count e página usam uma leitura consistente. Estado completo é reconstruído apenas na consulta individual e nos comandos.

Outbox persiste eventos internos na mesma transação, mas ainda não publica, confirma entrega ou garante consumo externo. Contratos públicos, dispatcher, retries e inbox do broker pertencem ao incremento de mensageria. OS e Budget continuam serviços separados.

## Consequências

Migrations versionadas e banco real são necessários nos testes. O formato JSON reduz mapeamento invasivo no domínio, mas exige evolução explícita e custo de reconstrução proporcional ao histórico. Se o volume crescer, medir antes de adotar snapshots de histórico ou outro modelo. A retenção conservadora aumenta armazenamento.

## Verificação

Testes independentes exercitam migrations up/down, rollback após a primeira gravação, violação real de chave única, replay, corrida de criação, disputa por versão, cancelamento versus início, tombstone e projeções SQL. Bancos de teste recebem nomes aleatórios e são removidos apenas pela fixture que os criou. Catálogo de preços continua fake nesses testes.

Referências: [transações EF Core](https://learn.microsoft.com/en-us/ef/core/saving/transactions) e [locks PostgreSQL 17](https://www.postgresql.org/docs/17/explicit-locking.html).
