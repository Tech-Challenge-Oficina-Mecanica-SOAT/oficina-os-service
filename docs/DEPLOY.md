# Deploy, qualidade e observabilidade do OS Service

## 1. Pipeline (`.github/workflows/ci.yml`)

| Job | Quando | O que faz |
|---|---|---|
| **Build & Test** | PR e push em `main` | restore, build, testes unitários + BDD com cobertura, **gate de 80%** (falha o PR se ficar abaixo), SonarCloud (se `SONAR_TOKEN` existir), publica relatório de cobertura |
| **Build & Push Docker Image** | push em `main` | imagem em `ghcr.io/<org>/oficina-os-service` com tags `latest` e `sha-xxxxxxx` |
| **Deploy Kubernetes (homolog)** | push em `main` **se** `vars.DEPLOY_HOMOLOG == 'true'` | `scripts/deploy-k8s.sh homolog` com a imagem `sha-xxxxxxx` |

Deploy manual (homolog ou prod, qualquer tag): workflow **Deploy** (`deploy.yml`, "Run workflow").

> Os PRs exigem o check **Build & Test** (branch protection da `main`): código com teste quebrado ou cobertura < 80% não entra.

### O que configurar no GitHub (uma vez)
| Onde | Nome | Valor |
|---|---|---|
| Settings → Environments | `homolog` e `prod` | (um por ambiente) |
| Secrets (por environment) | `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN` | credenciais do AWS Academy (**expiram em ~4h**: renovar antes de cada deploy) |
| Secrets | `GHCR_USERNAME`, `GHCR_PAT` | usuário e token com `Packages: read` para o cluster puxar a imagem privada |
| Secrets | `NEW_RELIC_LICENSE_KEY` | chave de licença do New Relic |
| Secrets | `SONAR_TOKEN` | token do SonarCloud |
| Variables | `SONAR_ORGANIZATION`, `SONAR_PROJECT_KEY` | organização e chave do projeto no SonarCloud |
| Variables | `DEPLOY_HOMOLOG` | `true` para deploy automático a cada merge (deixe desligado enquanto as credenciais do Academy não estiverem nos secrets) |

## 2. SonarCloud
1. Entrar em sonarcloud.io com o GitHub, importar o repositório `oficina-os-service`.
2. Copiar a organização e a chave do projeto para as variáveis acima e o token para `SONAR_TOKEN`.
3. Em "Administration → Analysis Method", desligar a *Automatic Analysis* (o CI faz a análise com cobertura).
4. A cobertura chega pelo `coverage.opencover.xml` (gerado pelo `coverlet.runsettings`); o CI aguarda o *quality gate*.

Sem `SONAR_TOKEN` o passo é pulado (o gate de 80% continua valendo).

## 3. Deploy no Kubernetes (`scripts/deploy-k8s.sh`)
Segue o padrão do `oficina-infra-k8s`: namespace `oficina`, imagem privada no GHCR (`ghcr-pull-secret`), Secret populado por script,
New Relic via init container, HPA, `Service` tipo NLB. O script:
1. lê o cluster no Parameter Store (`/oficina/{env}/k8s/cluster-name`) e configura o `kubectl`;
2. monta a connection string (RDS via Parameter Store + Secrets Manager) e cria o Secret `os-service-secrets`;
3. descobre a URL do Cadastros (`/oficina/{env}/services/cadastros/api-url`);
4. aplica `k8s/configmap.yaml`, `deployment.yaml`, `service.yaml`, `hpa.yaml` e espera o rollout;
5. publica a URL do serviço em `/oficina/{env}/services/os/api-url` (para Billing e Execução).

```bash
export AWS_ACCESS_KEY_ID=... AWS_SECRET_ACCESS_KEY=... AWS_SESSION_TOKEN=...   # AWS Academy
export GHCR_USERNAME=... GHCR_PAT=... NEW_RELIC_LICENSE_KEY=...
IMAGE=ghcr.io/tech-challenge-oficina-mecanica-soat/oficina-os-service:latest ./scripts/deploy-k8s.sh homolog
```

### Pré-requisitos que NÃO são deste repo
- **Database `oficina_os`** no RDS compartilhado (e, idealmente, role própria). Vem do repo `oficina-infra-db`. Até lá, criar manualmente
  (`CREATE DATABASE oficina_os;`) e, se houver role própria, informar `OS_DB_USER`/`OS_DB_PASSWORD`. Sem o database, o pod não sobe (migration falha).
- **Topics e fila**: `terraform apply` deste repo (tópicos `os.criada`/`os.cancelada`, fila `oficina-{env}-os-queue` + DLQ). A fila só recebe eventos
  dos tópicos dos outros serviços depois que eles existirem (ver `terraform/README.md`).
- **Cluster**: a Fase 3 roda em 1 node `t3.small` (limite de ~11 pods). Um segundo serviço + New Relic ocupam pods: conferir `kubectl get pods -A`.

### ⚠ Permissões AWS dos pods (AWS Academy)
Os pods usam a role do node (`LabEksNodeRole`), que provavelmente **não permite SNS, SQS nem SSM**. Sintoma: o pod sobe, mas o log mostra
`AccessDenied` ao publicar/consumir, e o evento fica pendente em `outbox_events`. Duas saídas:
1. **(já suportada)** exportar `AWS_SESSION_TOKEN` ao rodar o script: as credenciais da sessão do Academy são injetadas no Secret do pod.
   Expiram com a sessão: rode o deploy de novo para renovar.
2. Trocar a role do node group para a `LabRole` (mudança no `oficina-infra-k8s`, decisão do grupo).

## 4. Observabilidade (New Relic)
- O deployment injeta o agente .NET por init container (`newrelic/newrelic-dotnet-init`), igual à API da Fase 3. App name: `oficina-os-service`.
- **Trace distribuído:** HTTP é rastreado automaticamente. Entre serviços, o vínculo é o `correlationId` (W3C `traceparent`) do envelope
  dos eventos: a OS o grava ao publicar e o retoma ao consumir (os logs estruturados trazem o `CorrelationId`). Para o trace atravessar os
  4 serviços, **todos** precisam propagar o `correlationId` nos eventos que publicam.
- Dashboards/alertas sugeridos para o vídeo: taxa de erros 5xx, latência de `POST /ordens-servico`, idade do evento mais antigo em `outbox_events`,
  mensagens na DLQ (`oficina-{env}-os-dlq`).

## 5. Rodar localmente
Ver o README: `docker compose up --build` (API + Postgres + Wiremock + LocalStack), `scripts/demo-happy-path.sh` e `scripts/demo-rollback.sh`.
Coleção Postman: `docs/postman/oficina-os-service.postman_collection.json` (variável `baseUrl`).
