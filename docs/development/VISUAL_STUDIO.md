# Executar no Visual Studio com Docker Compose

## Preparação

Visual Studio com suporte a .NET 10 e Container Development Tools, Docker Desktop com engine Linux e .NET 10 SDK. Abra **GearFlow.Operation.slnx**, que inclui o projeto docker-compose, os cinco projetos de src, testes e ferramentas.

1. Deixe o Docker Desktop iniciado.
2. Defina **docker-compose** como Startup Project (clique direito no projeto → Set as Startup Project).
3. Selecione o perfil **Docker Compose** e execute F5; Ctrl+F5 executa sem debugger.
4. O navegador abre http://localhost:5085/scalar/v1, com a documentação interativa. O health check continua em /health/ready. Endpoints de negócio continuam exigindo JWT/Staff; Scalar e seu esquema são públicos apenas em Development.

O projeto usa compose.local.yml, .env.local e o overlay compose.visualstudio.yml. O arquivo docker-compose.vs.release.yml é aplicado depois do arquivo gerado da IDE para manter o caminho correto do debugger no Regular Mode. A inicialização cria credenciais aleatórias apenas se .env.local ainda não existir. Se existir, valida os campos mínimos e preserva o arquivo inteiro. Não imprime os valores. Se o arquivo existente estiver incompleto, configure-o; o script não troca a senha de um banco já criado.

Caso queira preparar o arquivo antes de abrir a IDE:

```powershell
powershell -NoProfile -File scripts/Initialize-LocalEnvironment.ps1
```

## Containers e depuração

PostgreSQL inicia primeiro; o container migrate aplica migrations e deve sair com código 0; a API só inicia depois. Apenas API usa debugger. O banco não publica porta no host. O projeto Compose chama-se gearflow-operation-local, igual à execução CLI existente, preservando o volume de dados desse ambiente.

O Dockerfile fica em src/Operation.Api/Dockerfile, junto ao projeto que o Container Tools precisa identificar; o contexto continua sendo a raiz do repositório. Usamos Regular Mode: o Dockerfile completo publica API e ferramenta de migration. Isso evita pular a migration ou depender de DLLs montadas do host no Fast Mode. O overlay da IDE compila Debug, habilita diagnósticos .NET, configura o programa/argumentos/diretório do debugger e permite escrita no container de desenvolvimento. A imagem recebe tag :dev. CLI/CI continuam com Release, diagnósticos desligados e filesystem somente leitura.

O suporte foi configurado conforme os [build settings oficiais](https://learn.microsoft.com/en-us/visualstudio/containers/docker-compose-properties) e [perfis Compose](https://learn.microsoft.com/en-us/visualstudio/containers/launch-profiles). O SDK Microsoft.Docker.Sdk é fornecido pelo Visual Studio; a CLI/CI usa GearFlow.Operation.CI.slnf para trabalhar sem esse SDK.

## CLI equivalente

```powershell
# Ambiente padrão, sem alterações de depuração:
docker compose --env-file .env.local -f compose.local.yml up --build -d api

# Overlay de desenvolvimento usado pela IDE (sem anexar debugger pelo terminal):
docker compose --env-file .env.local -f compose.local.yml -f compose.visualstudio.yml up --build -d api

pwsh -NoProfile -File scripts/Test-LocalDelivery.ps1
```

Ao alternar IDE/CLI, ambos usam o mesmo projeto/porta/volume local. Não iniciar duas cópias concorrentes. Parar a execução não exige apagar volumes. Se migrate falhar, examinar logs do serviço e corrigir a causa; não executar down --volumes como correção automática.

Não há configuração de tokens financeiros, credenciais Inventory ou telemetria remota inventada para permitir F5. Os componentes que dependem dessas integrações continuam exigindo configuração própria.


## Diagnóstico: processo encerra antes de CoreCLR

A imagem publica a API em /app/api/Operation.Api.dll. O padrão do Container Tools gerava /app/Operation.Api.dll e diretório /app; isso encerra dotnet antes de iniciar o runtime e causa o erro de CoreCLR/ERR_EMPTY_RESPONSE. O arquivo docker-compose.vs.release.yml fixa program/arguments/workingdirectory do debugger depois das configurações geradas da IDE. As propriedades equivalentes no csproj também registram o caminho da imagem, mas não substituem o override final no fluxo Compose.

Não editar obj/Docker/*.g.yml: são sobrescritos. Labels do overlay inicial podem ser substituídos pelo arquivo gerado que vem depois; por isso há um override final docker-compose.vs.release.yml específico para Regular Mode. Após alterar essas propriedades, parar a sessão anterior e executar F5 novamente para regenerar a configuração. O container pode estar running apenas com o helper do Visual Studio, sem a API atendendo; verificar readiness e breakpoint, não só a presença do container.


## O container migrate fica parado?

Sim. migrate é uma tarefa de execução única: aplica migrations e encerra com código 0. Não deve ficar rodando como a API/PostgreSQL e não deve receber debugger. Exited (0) é sucesso; saída diferente de zero bloqueia a API e exige investigar os logs. O aviso do Container Tools de que migrate não será depurado é compatível com esse papel. Não reiniciar continuamente nem apagar o volume para fazê-lo aparecer como running.


## Testar endpoints no Scalar

Após alterar o código, encerre a sessão anterior e execute F5 para reconstruir a imagem. A UI permite visualizar os endpoints sem token no ambiente Development; para enviar chamadas aos endpoints de negócio, configure Authentication/Bearer no Scalar.

Gere um token Staff temporário para o ambiente local:

```powershell
pwsh -NoProfile -File scripts/New-LocalStaffToken.ps1
# Opcional: token válido por 5 minutos em vez dos 30 padrão:
pwsh -NoProfile -File scripts/New-LocalStaffToken.ps1 -ExpiresInMinutes 5
```

Copie somente o JWT retornado para o campo de token Bearer do Scalar. O comando lê o segredo privado de .env.local, usa issuer gearflow-local/audience gearflow-operation e não altera arquivos. O token é uma credencial temporária: não versionar nem compartilhar. Não é mecanismo de login produtivo. Não há token pré-preenchido na página nem persistência de autenticação habilitada por esta integração.

Para comandos de operação, obtenha ETag no GET e informe If-Match e Idempotency-Key nos campos documentados. A UI não remove essas precondições. Fora de Development, /scalar/v1 retorna 404 e /openapi/v1.json continua protegido por Staff.

O Compose CLI padrão usa Production; para abrir Scalar pela CLI local, use o overlay de desenvolvimento mostrado acima. Kubernetes/produção não habilitam a UI automaticamente.
