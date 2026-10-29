# ADR-009 — Host HTTP operacional

Status: implementado no incremento 5A; preços externos e catálogo aguardam 5B.

ASP.NET Core 10 Minimal APIs reutiliza OperationService/OperationQueries e o storage PostgreSQL. O host não contém máquina de estados paralela. JWT valida assinatura/algoritmo HS256, emissor, audiência e expiração sem tolerância adicional; Staff exige claim/valor configurados e sub único GUID válido. Sem segredos ou claim de staff presumidos no repositório. Configuração ausente de identidade falha no boot.

Comandos exigem intenção GUID e ETag forte de versão. Application consulta receipt antes de checar versão, portanto replay histórico continua funcionando. Erros técnicos e de domínio são traduzidos em ProblemDetails com códigos estáveis, sem mensagens internas. JSON rejeita propriedades desconhecidas e enums numéricos. Preço e ator do cliente não são aceitos.

Preço não configurado mantém diagnóstico aberto e retorna 503. DiagnosticPricer agora aceita política ausente apenas como estado bloqueado: Price/PolicyId falham explicitamente; nenhuma política financeira é inferida. Outros comandos permanecem utilizáveis. Catálogo provisório do host sempre falha, até substituição por adapter real no 5B.

Testes HTTP usam WebApplicationFactory com middleware JWT real, banco PostgreSQL e catálogo fake explícito para testar o transporte independentemente da rede Inventory. Um cenário mantém o catálogo bloqueado para provar 503 sem mutação. Não confundir esses testes com integração HTTP real entre os serviços.

Referências: [testes Minimal APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/test-min-api?view=aspnetcore-10.0), [autenticação Minimal APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/security?view=aspnetcore-10.0). Dependência Microsoft.OpenApi fixada em 2.7.5 para evitar versão transitiva afetada pelo [advisory](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc).
