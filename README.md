# OS Service — Oficina Mecânica (Fase 4)

Microsserviço de Ordem de Serviço e **orquestrador da Saga** do Tech Challenge Fase 4 (FIAP SOAT).

- **Stack:** .NET 10, Clean Architecture (Domain / Application / Infrastructure / API), PostgreSQL (`oficina_os`).
- **Contrato REST:** [docs/openapi.yaml](docs/openapi.yaml).
- **Mensageria:** SNS/SQS (publica `os.criada`, `os.cancelada`; consome 9 eventos) — a partir das próximas etapas.

## Status por etapa (um PR por etapa)

| Branch | Etapa | Status |
|---|---|---|
| `feat/RafaelPacheco_Fase4_2` | Bootstrap: solução, entidades, Dockerfile, CI, OpenAPI | concluída (PR #1) |
| `feat/RafaelPacheco_Fase4_3` | REST + máquina de estados (Cadastros via Wiremock) | em revisão |
| `feat/RafaelPacheco_Fase4_4` | Outbox + publicação SNS (os.criada, os.cancelada) | em revisão (PR empilhado sobre a _3) |
| `feat/RafaelPacheco_Fase4_5` | Consumers SQS, dedupe por eventId e compensação | em revisão (empilhada sobre a _4) |
| `feat/RafaelPacheco_Fase4_6` | BDD (Reqnroll), gate de 80%, SonarCloud, manifestos K8s, deploy, Postman | em revisão (empilhada sobre a _5) |

## Rodar

```bash
dotnet build OficinaOs.slnx
dotnet test OficinaOs.slnx --settings coverlet.runsettings --collect:"XPlat Code Coverage"
docker build -t oficina-os-service .
```

Health: `GET /health/live`, `GET /health/ready`.
# oficina-os-service

## Rodar localmente com Wiremock (sem o Cadastros real)

```bash
docker compose up --build        # API :8080, Wiremock :8081, Postgres :5433
curl -X POST http://localhost:8080/ordens-servico -H "Content-Type: application/json" \
     -d '{"cpf":"12345678900","placa":"ABC1D23","defeitoRelatado":"Barulho no motor"}'
```

Massa de teste no Wiremock (`wiremock/mappings/`): CPF `12345678900` (ativo) com placa `ABC1D23`; CPF `99999999999` (inativo → 403); placa `XYZ9K88` (de outro cliente → 400); qualquer outro CPF/placa → 404.
Para usar o Cadastros real basta trocar `Cadastros__BaseUrl`.

## Mensageria (Outbox + SNS)

Cada mudança de estado grava a OS **e** uma linha em `outbox_events` na mesma transação; o `OutboxWorker` publica no SNS e marca como processado (entrega "pelo menos uma vez"; os consumidores deduplicam por `eventId`).

| Evento | Quando | Tópico |
|---|---|---|
| `os.criada` | ao abrir a OS | `oficina-{env}-os-criada` |
| `os.cancelada` | ao cancelar a OS | `oficina-{env}-os-cancelada` |

Configuração (`Messaging:*`): `Enabled`, `Environment`, `Region`, `ServiceUrl` (LocalStack), `PollIntervalSeconds`, `BatchSize`, `MaxAttempts`, `TopicArns` (sobrescreve o Parameter Store `/oficina/{env}/messaging/topics/{evento}/arn`).
Com `Enabled=false` os eventos ficam pendentes na tabela. O `docker-compose.yml` sobe um LocalStack com os tópicos e uma fila `oficina-local-spy` para conferir o que foi publicado:

```bash
docker compose exec localstack awslocal sqs receive-message --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/oficina-local-spy
```

Terraform dos tópicos e dos parâmetros: [terraform/](terraform/).

## Consumo de eventos (SQS) e compensação

O `SqsConsumer` lê `oficina-{env}-os-queue` (long polling) e entrega cada mensagem ao `IncomingEventHandler`, que decide o próximo estado da OS (orquestração da Saga).

| Evento recebido | Efeito na OS |
|---|---|
| `diagnostico.concluido` | `AguardandoDiagnostico → EmDiagnostico` (guarda `diagnosticoId`) |
| `orcamento.gerado` | `→ AguardandoAprovacao` (guarda `orcamentoId`) |
| `orcamento.aprovado` | `→ Aprovada` |
| `execucao.iniciada` | `→ EmReparo` |
| `execucao.finalizada` | `→ AguardandoPagamento` |
| `pagamento.confirmado` | `→ Concluida` (também resolve `PendenteCobranca`) |
| `pagamento.recusado` | `→ PendenteCobranca` (não cancela) |
| `orcamento.recusado` | **compensação:** `→ Cancelada` e publica `os.cancelada` (`orcamento_recusado`) |
| `cliente.desativado` | **compensação:** cancela as OSes canceláveis do cliente e publica `os.cancelada` (`cliente_desativado`) |

- **Idempotência:** o `eventId` é gravado em `processed_events` na mesma transação da mudança; entrega repetida é ignorada.
- **Evento fora de ordem / sem efeito** (transição inválida, OS inexistente, tipo desconhecido): registrado e descartado (log + ack), sem ir para a DLQ.
- **Mensagem malformada ou falha técnica:** a mensagem não é apagada; após 3 recebimentos a redrive policy a envia para `oficina-{env}-os-dlq`.
- **Rastreamento:** o `correlationId` do evento recebido vira o trace da operação; o `os.cancelada` gerado sai com o mesmo trace.
- Aceita o envelope direto (subscription com `raw_message_delivery=true`) e o embrulhado pelo SNS.

Simular os outros serviços localmente (com `docker compose up`):

```bash
scripts/fake-event.sh diagnostico.concluido '{"osId":"<ID>","diagnosticoId":"11111111-0000-0000-0000-000000000001"}'
scripts/fake-event.sh orcamento.recusado    '{"osId":"<ID>","orcamentoId":"22222222-0000-0000-0000-000000000002"}'
```

## Qualidade, BDD e demonstração

- **BDD (Reqnroll):** `tests/OficinaOs.Tests.BDD/Features/SagaOrdemServico.feature` — 8 cenários em português (Saga feliz, recusa de orçamento, pagamento recusado, cliente desativado, evento duplicado, evento fora de ordem, cancelamento proibido em reparo, cliente inativo). Sobem a API de verdade com Wiremock no papel do Cadastros.
- **Testes:** 101 unitários + 8 BDD. **Cobertura de linhas ≈ 88%**; o CI falha o PR abaixo de 80% e roda o SonarCloud (ver [docs/DEPLOY.md](docs/DEPLOY.md)).
- **Demonstração local** (com `docker compose up --build -d`): `scripts/demo-happy-path.sh` (Saga completa) e `scripts/demo-rollback.sh` (compensação + idempotência).
- **Contrato REST:** [docs/openapi.yaml](docs/openapi.yaml) · **Postman:** [docs/postman/](docs/postman/oficina-os-service.postman_collection.json).
- **Deploy Kubernetes, SonarCloud e New Relic:** [docs/DEPLOY.md](docs/DEPLOY.md); manifestos em [k8s/](k8s/).
