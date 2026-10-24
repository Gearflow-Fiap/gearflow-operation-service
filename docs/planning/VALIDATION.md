# Validação documental da baseline

Data: 2026-10-24.

- 14 exemplos de corpos lógicos validados com PowerShell Test-Json contra as respectivas definições JSON Schema.
- Casos inválidos de Quantity Labor diferente de 1 e DiagnosticFinished sem diagnosticId rejeitados pelo schema.
- Links Markdown locais conferidos sem destinos ausentes.
- Manifesto conferido: 14 mensagens, 14 definições correspondentes e 14 exemplos.
- IDs de mensagem distintos da OS e totais monetários dos exemplos conferidos com decimal e arredondamento midpoint away from zero.
- Snapshot InventoryMessages.snapshot.cs idêntico em bytes à referência lida no Inventory; SHA-256 registrado no guia de compatibilidade.
- Guia de migração acrescentado ao monólito e índice atualizado; sem mudança de runtime.

Limites: não houve roundtrip MassTransit, publicação de pacote, execução dos novos endpoints, deploy ou BDD. Testes de negócio do núcleo não foram reexecutados porque esta etapa altera somente documentação. Os 19 testes e cobertura inicial são evidência anterior, não validação do desenho novo.

Reprodução: executar docs/contracts/Validate-Examples.ps1 com PowerShell 7. Regras semânticas de snapshot, totais, unicidade por LineId e estado de Saga ainda precisam de testes do pacote/serviços.
