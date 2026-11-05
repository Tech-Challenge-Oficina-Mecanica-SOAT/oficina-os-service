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
| `feat/RafaelPacheco_Fase4_5` | Consumers SQS + compensação | pendente |
| `feat/RafaelPacheco_Fase4_6` | BDD, Sonar, deploy K8s, New Relic | pendente |

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
