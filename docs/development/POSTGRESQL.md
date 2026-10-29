# PostgreSQL e testes

SDK .NET 10. Unitários não exigem banco:

```powershell
dotnet test tests/Operation.UnitTests --configuration Release
```

Integração usa PostgreSQL 17 via Testcontainers quando Docker Linux está disponível:

```powershell
dotnet test tests/Operation.IntegrationTests --configuration Release
```

Alternativamente, defina OPERATION_TEST_POSTGRES com uma conexão para servidor PostgreSQL 17 descartável. O usuário precisa criar bancos; a fixture cria bancos operation_it_<guid> e exclui somente os que criou. Nunca aponte para servidor de produção. A CI usa um service PostgreSQL e essa variável. Não há fallback silencioso para testes em memória.

Para desenvolvimento:

```powershell
docker compose -f compose.yml up -d
dotnet tool restore
# Defina OPERATION_CONNECTION_STRING com Host=localhost;Port=5434;Database=gearflow_operation;Username=gearflow;Password=<senha local>
dotnet ef database update --project src/Operation.Infrastructure --startup-project src/Operation.Infrastructure
```

O compose usa senha exclusivamente local, substituível por OPERATION_POSTGRES_PASSWORD. A aplicação ainda não tem host HTTP nem inicialização de migrations automática. Em produção, migrations devem ser executadas como tarefa controlada de implantação.

Cobertura consolidada, em diretório limpo por execução:

```powershell
dotnet test GearFlow.Operation.CI.slnf --configuration Release --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/current
python scripts/check_coverage.py TestResults/current
```

O script une os hits por assembly/classe/arquivo/linha entre unitários e integração; não soma percentuais nem conta cópias de relatórios como novas linhas. Gate mínimo de 80% por assembly.

A configuração exclui somente OpenApiXmlCommentSupport.generated.cs, gerado pela biblioteca Microsoft.AspNetCore.OpenApi. Código escrito do host, domínio, aplicação, infraestrutura e migrations continua medido.

A migration AddLaborCatalog acrescenta catálogo local. Para migrar ambiente com dados, faça backup e aplique a migration progressiva; rollback da tabela remove apenas o catálogo e não deve ser usado como estratégia de reversão de dados em produção. Testes de down usam banco exclusivo vazio.

Desde o incremento 6, a suíte completa de integração também exige RabbitMQ/management isolados, configurados em MESSAGING.md. A fixture nativa de PostgreSQL não fornece broker nem pula testes quando ele falta. CI sobe ambos os serviços.
