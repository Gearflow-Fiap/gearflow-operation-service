# M2 — composição e configuração

Concluído localmente em 29/10/2026. Registros DI por camada, opções tipadas e Program reduzido. Sem alteração em contratos HTTP/mensagens, migrations ou configuração de F5/Compose. Nenhum commit, push ou deploy executado pelo agente.

## Resultado

- Build Release dos nove projetos .NET via GearFlow.Operation.CI.slnf: zero erros/avisos.
- Suíte completa: **273 testes .NET distintos** (164 unitários + 109 integração), zero falhas/ignorados. Inclui 37 novos casos de configuração/composição. A execução focada inicial de 32 casos é suplementar e não foi somada ao total.
- **13 testes Python aprovados**, incluindo proteção do override do debugger após o arquivo gerado pela IDE e preservação das credenciais locais.
- Gate por assembly >=80% aprovado: Contracts 100%; API 95,12%; Application 100%; Domain 98,93%; Infrastructure 99,19%.
- PostgreSQL 17 nativo isolado, iniciado/parado na execução de testes; bancos exclusivos por fixture. RabbitMQ real nas portas 5674/15674, vhosts exclusivos. Coletores locais exercitados pelos testes existentes de OTLP.
- `git diff --check` aprovado. Relatórios TRX e cobertura em TestResults/quality-m2 (ignorados pelo Git); resumo em [validation.json](validation.json).

## O que foi verificado

Lifetime singleton da factory EF/relógio/credencial, scope dos casos de uso/pricer/store e mesma instância de store para leitura e unidade de trabalho. Recarga da credencial do Inventory via IOptionsMonitor. URL validada somente ao resolver cliente/opções; ausência continua permitida. JWT obrigatório com mínimo em bytes UTF-8 e policy Staff. Mensageria incompleta falha apenas quando habilitada; flags de workers respeitadas. OTLP opcional, protocolo/endpoint validados sem valores sensíveis nas mensagens. Política de consumíveis ausente/desconhecida continua sem default financeiro.

Regressão de HTTP/OpenAPI/Scalar, autorização, preço real via stub HTTP, idempotência/replay, concorrência, migrations, inbox/outbox e recuperação RabbitMQ aprovada. O teste de arquitetura agora também impede dependências de ASP.NET, Configuration e Options na aplicação.

## Comportamento explicitamente antecipado

ConnectionStrings:Operation não vazio passa a ser validado na inicialização; antes a ausência era detectada ao resolver a factory. Isso não conecta ao banco nem aplica migrations. Host com conexão configurada mas indisponível continua iniciando; readiness permanece responsável pela disponibilidade.

[Guia de composição/configuração](../../../development/CONFIGURATION.md). A abstração de DI é a única dependência nova na aplicação; configuração de host continua na API/infraestrutura. Não foi adicionado MediatR.

## Limites e retomada

Imagem/F5 não foram reconstruídos nesta etapa pelo agente, que não tem acesso ao pipe Docker. Os testes HTTP usam o host real ASP.NET de teste. Reiniciar F5 recompila a aplicação para conferir localmente. Integração coletiva/OS/Saga e entrega remota continuam pendentes no 7B.

Próxima etapa: **M3 — entrada HTTP**, padronizando validação de requests, ator autenticado e parser de ETag com equivalência de comportamento.
