# Scalar — 29/10/2026

Scalar.AspNetCore 2.16.4 integrado em /scalar/v1. Perfil Docker Compose e DockerServiceUrl abrem essa URL após reconstruir com F5.

Em Development, UI e /openapi/v1.json são anônimos para carregar a documentação antes de informar token. Endpoints de negócio mantêm JWT/Staff. Fora de Development, Scalar não é mapeado e o esquema continua protegido. Bearer, If-Match e Idempotency-Key existentes são preservados; nenhum token é pré-preenchido na página.

New-LocalStaffToken.ps1 gera JWT HS256 para o ambiente local, válido por 30 minutos (configurável de 1 a 60), lendo .env.local sem modificar arquivos. Saída é o token para copiar ao Scalar; nenhum token real consta nas evidências. Teste valida assinatura, issuer/audience, Staff e expiração.

## Validação

- Restore/build Release sem avisos ou erros.
- 231 casos .NET distintos aprovados: 164 unitários + 67 integração. A execução inicial encontrou três timeouts de broker indisponível. Após o usuário iniciar o RabbitMQ isolado, os três casos foram reexecutados e aprovados; nenhuma falha restante. Identidade usa TestId do TRX, pois nomes de teorias longas podem ser truncados igualmente.
- Três novos cenários aprovados: Development com UI/esquema públicos e APIs 401/403/200; Production/Testing com UI 404 e esquema restrito a Staff.
- 13 testes Python aprovados, sem ignorados, incluindo assinatura/claims/expiração do token local.
- Cobertura: Contracts 26/26 (100%), API 309/330 (93,64%), Application 345/345 (100%), Domain 370/374 (98,93%), Infrastructure 2511/2532 (99,17%). Gate >=80% por assembly aprovado.
- URLs da IDE concordam em /scalar/v1. TRX/Cobertura/OpenCover em TestResults/scalar, incluindo retry (ignorados). PostgreSQL nativo encerrado após testes.

Testes HTTP usam WebApplicationFactory/ASP.NET e PostgreSQL reais. Não representam inspeção visual do navegador nem deploy da imagem. Reiniciar F5 é necessário para exibir a UI atualizada. Nenhum deploy remoto ou outro repositório alterado.
