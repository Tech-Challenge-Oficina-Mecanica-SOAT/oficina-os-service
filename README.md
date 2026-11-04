# OS Service — Oficina Mecânica (Fase 4)

Microsserviço de Ordem de Serviço e **orquestrador da Saga** do Tech Challenge Fase 4 (FIAP SOAT).

- **Stack:** .NET 10, Clean Architecture (Domain / Application / Infrastructure / API), PostgreSQL (`oficina_os`).
- **Contrato REST:** [docs/openapi.yaml](docs/openapi.yaml).
- **Mensageria:** SNS/SQS (publica `os.criada`, `os.cancelada`; consome 9 eventos) — a partir das próximas etapas.

## Status por etapa (um PR por etapa)

| Branch | Etapa | Status |
|---|---|---|
| `feat/RafaelPacheco_Fase4_2` | Bootstrap: solução, entidades, Dockerfile, CI, OpenAPI | em andamento |
| `feat/RafaelPacheco_Fase4_3` | REST + máquina de estados | pendente |
| `feat/RafaelPacheco_Fase4_4` | Outbox + publicação SNS | pendente |
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
