# Entrada HTTP: ator, precondições e requests

A API traduz entradas HTTP para os casos de uso. A aplicação e o domínio continuam responsáveis por regras de negócio e invariantes. Comandos recebidos por mensageria mantêm seu ator explícito e não dependem de HttpContext.

## Pontos compartilhados

| Arquivo | Responsabilidade |
|---|---|
| Authentication/StaffActor.cs | Verifica sub GUID único e não vazio; lê o ator autenticado como staff:<sub>. A policy Staff continua exigindo autenticação e a claim de perfil configurada |
| Http/StrongETag.cs | Parser único de If-Match e escrita de ETag/Cache-Control para catálogo e operações |
| Http/OperationCommandInput.cs | Lê Idempotency-Key, versão esperada e ator dos comandos HTTP de operação |
| Endpoints/Operations/OperationRequests.cs | DTOs e validação de shape da lista de consumíveis; converte para objetos do domínio |
| Endpoints/Catalog/JobRequest.cs | DTO e conversão para JobDefinition, reutilizando a validação/normalização da aplicação |

Não há fallback de ator para system quando faltar identidade/contexto HTTP. O helper recusa identidade não autenticada, sub ausente/duplicado/inválido/vazio. Requisições externas passam antes pela policy Staff: JWT inválido/anônimo continua 401; cliente ou sub inválido com JWT válido continua 403. Um JWT com sub em array (produzido por claims sub duplicadas no teste) é rejeitado pelo validador do token com 401 antes da policy. O erro invalid_actor/401 é uma proteção defensiva do helper, não uma substituição desses resultados da policy.

O texto original do sub é preservado após validar o GUID. Não converter caixa, remover chaves ou normalizar seu formato: o ator participa do fingerprint de idempotência e do histórico existente. Actor no corpo não é aceito; headers livres como X-Actor não definem a identidade.

## Precondições

| Escrita | Idempotency-Key | If-Match |
|---|---|---|
| Comandos de diagnóstico/execução | Uma chave GUID não vazia obrigatória | Uma versão forte obrigatória |
| Criar mão de obra | Não exigida | Não exigido |
| Atualizar/desativar mão de obra | Não exigida | Uma versão forte obrigatória |

If-Match aceita apenas a forma canônica `"1"` até `"9223372036854775807"`. Rejeita W/, wildcard, listas/headers múltiplos, sinal, zero, números com zeros à esquerda, espaços internos, decimais e overflow. Ausência retorna 428/if_match_required; formato inválido retorna 400/invalid_if_match. Um header vazio omitido pelo transporte equivale a ausente; não foi criada uma regra nova para esse caso.

Operações validam Idempotency-Key antes de If-Match, preservando 400/invalid_idempotency_key quando ambos estiverem ausentes/inválidos. Catálogo não chama o leitor de contexto de operação. Atualizações de catálogo validam versão antes de converter o corpo, como antes.

Escritas e detalhes continuam retornando ETag forte com Cache-Control: no-store. Versão divergente é 412/version_conflict. Replay exato continua devolvendo o resultado/ETag históricos; o parser HTTP não decide conflito, não consulta versões no banco e não antecipa a política de preço.

## Corpos e regras

JSON mantém enums textuais estritos, membros desconhecidos proibidos e parâmetros obrigatórios do construtor. Falhas de binding retornam 400/invalid_request. A conclusão de diagnóstico recusa lista nula, elemento nulo e mais de DiagnosticSnapshot.MaximumLines (200) com 400/invalid_consumables; lista vazia permanece permitida.

Identificadores/quantidade de consumíveis continuam validados por DiagnosticConsumable/DomainGuard. Nome, descrição e preço do catálogo continuam validados por JobDefinition. Motivo de falha continua normalizado/validado na aplicação: obrigatório, trim e até 2000 caracteres após trim. Não duplicar essas regras na API nem enfraquecê-las para consumers.

Validação de shape ocorre antes do caso de uso, na mesma ordem anterior. Dentro da aplicação permanece um único pipeline transacional: replay antes de consulta externa/controle de versão, com commit atômico de estado, receipt e outbox. Nenhum filtro/mediator foi inserido nesse fluxo.

## Testes

HttpInputContractTests caracteriza as escritas reais com PostgreSQL: códigos de precondição, ordem das validações, limites de corpo, identidade JWT e ausência de efeitos parciais. Os três primeiros cenários foram confirmados no binário anterior à refatoração e repetidos no serviço refatorado. HttpInputTests verifica os helpers sem dependências externas, incluindo headers físicos múltiplos, versão máxima e ausência de fallback de ator.

A regressão HTTP/OpenAPI/Scalar, persistência, mensageria e telemetria permanece no gate completo. [Plano de evolução](../planning/CODE_QUALITY_PLAN.md).
