# M3 — entrada HTTP

Concluído localmente em 29/10/2026. Ator autenticado, ETag e contexto de comando HTTP compartilhados; DTOs/conversão de requests organizados. Sem alteração em rotas, contratos de mensagens, migrations, política financeira ou configuração de F5/Compose.

## Mudanças

- StaffActor concentra leitura do sub autenticado e a validação do sujeito usada pela policy Staff; mantém texto original do ator no histórico/fingerprint. Identidade ausente não gera fallback system.
- StrongETag concentra parser canônico de If-Match e escrita de ETag/no-store para catálogo/operação.
- OperationCommandInput reúne Idempotency-Key, versão e ator somente para comandos HTTP de operação. Catálogo continua sem exigência de chave de idempotência.
- FinishDiagnosticRequest valida shape antes da conversão para objetos de domínio; JobRequest fica em arquivo próprio e reutiliza JobDefinition. Limites e regras continuam protegidos na aplicação/domínio; motivo de falha continua validado na aplicação.
- Mesma ordem das validações. Pipeline de replay, concorrência e commit de estado/receipt/outbox inalterado. Consumers mantêm ator explícito, sem depender de HttpContext.

## Validação

Build Release dos nove projetos .NET: zero erros/avisos. Suíte final: **290 testes .NET distintos** (164 unitários + 126 no projeto de integração), zero falhas/ignorados; **13 testes Python aprovados**. Dezessete novos casos de borda HTTP: cinco cenários com host/PostgreSQL e doze casos de helpers sem serviços externos. Execuções focadas de 16 casos e baseline de três casos não são somadas ao total final.

Gate >=80% por assembly aprovado:

| Assembly | Linhas cobertas | Cobertura |
|---|---:|---:|
| Contracts | 26/26 | 100,00% |
| API | 432/451 | 95,79% |
| Application | 351/351 | 100,00% |
| Domain | 370/374 | 98,93% |
| Infrastructure | 2557/2578 | 99,19% |

PostgreSQL 17 isolado, iniciado/parado na execução; bancos exclusivos por fixture. RabbitMQ real em 5674/15674 com vhosts exclusivos. Regressão inclui HTTP/OpenAPI/Scalar, autenticação/autorização, replay, concorrência, preços, migrations, inbox/outbox, recuperação e OTLP. `git diff --check` aprovado.

TRX e cobertura estão em TestResults/quality-m3 (ignorados pelo Git); [resumo estruturado](validation.json). A [caracterização anterior](BEFORE.md) detalha os três cenários confirmados antes da refatoração, incluindo 13 variações de header para cada uma das oito escritas com versão.

O teste JWT distingue sub inválido com token autenticado (403 pela policy) de sub em array (401 pelo validador JWT antes da policy). Nos testes iniciais, essa expectativa foi ajustada ao comportamento real da biblioteca, sem alteração da configuração de autenticação.

## Limites e retomada

Nenhum commit, push ou deploy executado pelo agente. Imagem/F5 não foram reconstruídos pelo agente; testes HTTP usam o host real ASP.NET de teste. Reiniciar F5 para executar o código novo. Pendências coletivas OS/Saga, integração entre serviços e entrega remota permanecem no 7B.

[Guia de entrada HTTP](../../../development/HTTP_INPUT.md). Próximo passo: **M4 — configurações EF por entidade, versões NuGet centralizadas e convenções incrementais**, preservando modelo/migrations e suporte Docker.
