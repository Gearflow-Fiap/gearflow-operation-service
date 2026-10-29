# ADR-012 — Observabilidade do Operation

Data: 29/10/2026. Estado: implementado localmente; backend remoto pendente.

## Contexto e decisão

O participante precisa explicar falhas HTTP, comandos e entrega assíncrona sem mudar os contratos públicos. Adotamos OpenTelemetry para traces, métricas e logs; logs JSON também ficam no console. O recurso usa service.name `GearFlow.Operation`. A exportação OTLP é opt-in e exige endpoint válido. Reutiliza o padrão OTLP/New Relic encontrado na infraestrutura da fase 3, sem instalar um agente proprietário na imagem.

Comandos registram ação, duração e resultado (changed/replay/unchanged/error). A outbox preserva contexto W3C no JSON privado de eventos e a inbox em duas colunas opcionais, com migration explícita. O dispatcher retoma o contexto após reinício; IDs de OS/intenção ficam em traces, sem virar rótulos das métricas próprias. O formato público dos 18 contratos permanece inalterado. Mensagens antigas sem contexto continuam processáveis.

Métricas próprias usam dimensões limitadas: ação/resultado ou tipo de entrega. Backlog consulta dados persistidos a cada 15 segundos: eventos/replies pendentes, inbox incompleta e idade da pendência mais antiga. Falha de coleta sinaliza sample_success=0 e suprime os gauges de dados, evitando apresentar zero como sucesso. Isso não substitui alarmes do broker/DLQ.

Liveness só verifica processo. Readiness exige conexão e schema atualizado; com mensageria habilitada inclui os checks registrados pelo MassTransit. O boot não aplica migrations. Respostas de saúde não divulgam detalhes de conexão.

## Consequências e validação

Logs próprios de HTTP usam método, template da rota, status e escopo de trace/OS; não registram token, corpo ou query. Bibliotecas ainda exigem política de retenção e revisão do coletor. Health probes não entram nos traces HTTP. OTLP possui timeout, exporta em background e não participa da transação de negócio.

Testes cobrem medição de resultados, retomada da outbox no RabbitMQ real, endpoint de coleta OTLP local (traces/métricas/logs), backlog e readiness. Recebimento no coletor local não comprova indexação ou dashboards no New Relic. Credenciais, retenção, amostragem e alertas de homologação ficam no incremento 7B.
