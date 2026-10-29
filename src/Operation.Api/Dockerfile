# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Operation.Domain/Operation.Domain.csproj src/Operation.Domain/
COPY src/Operation.Application/Operation.Application.csproj src/Operation.Application/
COPY src/Operation.Contracts/Operation.Contracts.csproj src/Operation.Contracts/
COPY src/Operation.Infrastructure/Operation.Infrastructure.csproj src/Operation.Infrastructure/
COPY src/Operation.Api/Operation.Api.csproj src/Operation.Api/
COPY tools/Operation.Migrate/Operation.Migrate.csproj tools/Operation.Migrate/
RUN dotnet restore src/Operation.Api/Operation.Api.csproj && dotnet restore tools/Operation.Migrate/Operation.Migrate.csproj
COPY src/ src/
COPY tools/Operation.Migrate/ tools/Operation.Migrate/
RUN dotnet publish src/Operation.Api/Operation.Api.csproj -c $BUILD_CONFIGURATION --no-restore -o /publish/api /p:UseAppHost=false \
    && dotnet publish tools/Operation.Migrate/Operation.Migrate.csproj -c $BUILD_CONFIGURATION --no-restore -o /publish/migrate /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app/api
ENV ASPNETCORE_HTTP_PORTS=8080 DOTNET_EnableDiagnostics=0
COPY --from=build /publish/api/ /app/api/
COPY --from=build /publish/migrate/ /app/migrate/
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "Operation.Api.dll", "--health-probe"]
ENTRYPOINT ["dotnet", "Operation.Api.dll"]
