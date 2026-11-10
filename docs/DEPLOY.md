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
- **Database `oficina_os`**: **nao precisa criar**. Com `Database__MigrateOnStartup=true` (padrao no `k8s/configmap.yaml`) e o usuario admin do RDS
  (`oficina_admin`, que tem permissao de CREATEDB), o `Migrate()` do EF Core cria o database e as tabelas no primeiro start do pod (validado: banco inexistente
  -> API sobe, cria `oficina_os` e aplica as migrations). E o mesmo padrao de EF migrations dos outros servicos. O `deploy-k8s.sh` usa o admin por padrao;
  para role propria, informar `OS_DB_USER`/`OS_DB_PASSWORD` (a role precisa de CREATEDB ou o database ja deve existir).
- **Topics e fila**: `terraform apply` deste repo (tópicos `os.criada`/`os.cancelada`, fila `oficina-{env}-os-queue` + DLQ). A fila só recebe eventos
  dos tópicos dos outros serviços depois que eles existirem (ver `terraform/README.md`).
- **Cluster**: a Fase 3 roda em 1 node `t3.small` (limite de ~11 pods). Um segundo serviço + New Relic ocupam pods: conferir `kubectl get pods -A`.

### ⚠ Permissões AWS dos pods (AWS Academy)
Os pods usam a role do node (`LabEksNodeRole`); o Academy não permite anexar policy a ela nem IRSA. O caminho adotado (o mesmo do Cadastros):
1. **Policy de recurso** (`terraform/iam.tf` e `sqs.tf`): os tópicos `os.criada`/`os.cancelada` aceitam `sns:Publish` da role do node e a fila aceita
   o consumo dela. Exige o **limite de salto 2 do IMDS** no node group (já na `main` do `oficina-infra-k8s`).
2. **SSM não tem policy de recurso:** o `deploy-k8s.sh` resolve os ARNs dos tópicos e a URL da fila com a sessão de quem faz o deploy e os passa
   ao pod por variável (`Messaging__TopicArns__*`, `Messaging__QueueUrl`). Rode o `terraform apply` **antes** do deploy.
3. **Fallback:** `INJECT_AWS_CREDENTIALS=true ./scripts/deploy-k8s.sh homolog` injeta as credenciais da sessão no Secret do pod (como a Execução faz);
   expiram com a sessão (~4h), então rode o deploy de novo para renovar.

Sintoma de permissão faltando: o pod sobe, mas o log mostra `AccessDenied` e o evento fica pendente em `outbox_events`.

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
