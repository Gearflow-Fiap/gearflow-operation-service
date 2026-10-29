# Composição e configuração

O `Program.cs` combina três registros explícitos: `AddOperationApplication`, `AddOperationInfrastructure` e `AddOperationApi`. Mantém a ordem do pipeline HTTP e o probe de container. As camadas não criam um segundo service provider.

| Registro | Responsabilidade |
|---|---|
| Application/DependencyInjection.cs | Casos de uso e consultas scoped, pricer scoped e relógio singleton substituível; recebe a política por função, sem depender de IConfiguration, ASP.NET ou Options |
| Infrastructure/DependencyInjection.cs | Factory EF singleton, store e aliases scoped, catálogo, cliente HTTP, credencial singleton e transporte opcional |
| Api/Composition/OperationApiRegistration.cs | Autenticação, autorização, JSON, ProblemDetails, OpenAPI, health checks, telemetria e hosted workers |

`IOperationUnitOfWork` e `IOperationReadStore` compartilham a mesma instância de `PostgresOperationStore` dentro do scope. Outro scope recebe outro store. O pipeline de estado/receipt/outbox permanece único e inalterado.

## Opções e momento de validação

As chaves existentes, inclusive variáveis com `__`, continuam válidas. Não há novo arquivo obrigatório nem credenciais padrão.

| Grupo tipado | Regra |
|---|---|
| JwtOptions / StaffAuthorizationOptions | Obrigatórios na composição; segredo com ao menos 32 bytes UTF-8, issuer, audience e claim Staff explícitos. HS256, expiração, sub GUID único e policy Staff preservados |
| DatabaseOptions | ConnectionStrings:Operation não vazio validado na inicialização. Essa antecipação é intencional: antes a ausência era detectada ao resolver a factory. Não abre conexão nem aplica migrations na inicialização; banco inacessível continua sendo problema de readiness |
| InventoryOptions | URL e token podem faltar. URL configurada é validada ao resolver o cliente/opções: HTTPS, ou HTTP loopback/explicitamente permitido, sem credenciais/query/fragmento. Timeout 5s e redirects desativados |
| PricingOptions | Política ausente/desconhecida resulta em modo nulo e erro de preço indisponível quando exigida. Nenhuma regra financeira é escolhida automaticamente |
| MessagingOptions / RabbitMqOptions | Mensageria false por padrão. Somente Enabled=true exige host, porta, vhost, usuário e senha. DispatchEnabled registra o worker apenas junto com Enabled=true |
| TelemetryOptions / OtlpOptions | Exportação false por padrão; endpoint/protocolo exigidos apenas com exportação ativa. BacklogEnabled permanece true por padrão e independente do broker |

OTLP usa `Otlp:Endpoint` antes de `OTEL_EXPORTER_OTLP_ENDPOINT`; protocolo padrão grpc, alternativa http/protobuf. Autenticação do coletor continua em OTEL_EXPORTER_OTLP_HEADERS. Flags de ativação, parâmetros de autenticação e transporte são decisões de inicialização: reiniciar a API após alterá-los.

A credencial singleton do Inventory usa `IOptionsMonitor<InventoryOptions>`: observa reload dos providers que o suportam. Editar variáveis de ambiente de um processo em execução não provoca reload; reiniciar o container nesse caso. URL é lida quando um cliente é criado e política usa snapshot por scope.

As novas opções com segredos usam classes comuns, sem ToString gerado de records. As mensagens de validação de endereço, conexão ausente e transporte incompleto identificam a chave/regra sem imprimir seus valores. Não registrar objetos de opções ou dumps de configuração.

## Verificação

`Operation.IntegrationTests/Api/ConfigurationTests` verifica composição sem conectar a serviços externos, ativação de workers, requisitos de configuração, validação lazy do Inventory, recarga de credencial e ausência de default financeiro. Os demais testes HTTP, PostgreSQL, RabbitMQ e OTLP verificam o comportamento real dos adapters. `LayerDependencyTests` impede dependências de configuração/host na aplicação.

M2 não altera endpoints, contratos de mensagens, migrations, política financeira nem a configuração de F5/Compose. [Plano](../planning/CODE_QUALITY_PLAN.md).
