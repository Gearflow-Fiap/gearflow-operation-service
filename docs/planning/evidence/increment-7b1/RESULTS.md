# Incremento 7B.1 — kit de integração e revisão coletiva

Checkpoint: 29/10/2026. Concluído o preparo local do kit; **7B global permanece pendente**.

## Entrega

- Guia para o coordenador, pacote NuGet preview e os 18 contratos/schemas/exemplos atuais num ZIP com lista explícita de arquivos e checksums SHA-256.
- Matriz com oito achados de compatibilidade, baseada na leitura de 14 arquivos de cinco repositórios. Impressões SHA-256 dos arquivos inspecionados ficam em compatibility.json; são snapshots de conteúdo, não prova de execução dos serviços.
- Seis decisões ainda pendentes, com dono/revisão aceitos em branco, e onze cenários de homologação conjunta marcados como não executados.
- Gerador/verificador scripts/build_integration_handoff.py; CI prepara testes e artifact operation-integration-preview. Nada publicado em registry ou enviado ao grupo.

## Validação desta entrega

**Oito testes Python aprovados**, sem falhas/ignorados. Cobrem bundle determinístico, exclusão de arquivos privados/não listados, pacote com contrato desatualizado, versão divergente, arquivo privado dentro do NuGet, caminhos inválidos/entradas duplicadas, alteração/arquivo extra no ZIP e preservação de arquivo de saída existente.

Build Release de Operation.ContractArtifacts: zero avisos/erros. Export CLR fresco comparado com 18 contratos versionados: schemas, exemplos e guards de compatibilidade aprovados. NuGet real GearFlow.Contracts.Operation 1.0.0-preview.1 empacotado, incluído no ZIP e comparado aos JSONs versionados. Geração e verificação do ZIP real aprovadas. YAML da CI analisado localmente; workflow remoto não executado.

Nenhum código do runtime .NET ou contrato público mudou neste checkpoint. A suíte .NET/banco/broker não foi repetida: os 225 testes e cobertura do incremento 7A permanecem evidência daquele checkpoint, sem somar a execução anterior como teste novo da integração distribuída.

## Achados que precisam do grupo

1. Usuário não sabe confirmar quem será o dono da OS/Saga. Budget documenta OS separado, mas isso ainda é proposta; não há runtime OS disponível.
2. Infra-broker usa topic/routing key e wrapper próprios; Operation/Inventory usam envelope/topologia MassTransit. Configurações atuais não são automaticamente intercambiáveis.
3. Budget atribui tarefas de estoque ao antigo Execution, enquanto o Inventory implementa reserva/liberação/baixa. Corrigir fronteiras no desenho conjunto.
4. Ordem de reserva/pagamento diverge; definir política e compensações antes de implementar a Saga.
5. GETs/DTOs Inventory são compatíveis por inspeção, mas faltam chamada real e credencial de serviço homologadas.
6. Budget ainda não oferece contratos executáveis para verificar payload/URN dos nove eventos comunicados pelo grupo.

## Próxima retomada

Revisar o kit com OS/Budget/Inventory/infra-broker e atribuir D-01..D-06. Não implementar Saga financeira no Operation enquanto o owner está indefinido. Com dono/contratos/topologia acordados, executar cenários H-01..H-11 em ambiente isolado, registrando revisões, IDs correlacionados, estados por serviço e evidências.

PostgreSQL próprio/cloud, credenciais/ACL, CI/Sonar remotos, backend de telemetria, publicação e deploy continuam pendentes. Outros repositórios foram apenas lidos; nenhum apply, push, commit, publicação ou mensagem externa foi executado.
