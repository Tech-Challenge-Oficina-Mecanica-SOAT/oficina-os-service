#!/usr/bin/env bash
# Deploy do OS Service no EKS (mesmo padrão do oficina-infra-k8s/scripts/deploy-manifests.sh).
#
#   IMAGE=ghcr.io/<org>/oficina-os-service:sha-1a2b3c4 ./scripts/deploy-k8s.sh homolog
#
# Pré-requisitos: credenciais AWS exportadas (AWS Academy: expiram em ~4h), kubectl, envsubst, aws cli.
# Variáveis opcionais:
#   IMAGE                  imagem a publicar (padrão: ghcr.io/tech-challenge-oficina-mecanica-soat/oficina-os-service:latest)
#   GHCR_USERNAME/GHCR_PAT credencial para puxar a imagem privada do GHCR (cria o secret ghcr-pull-secret)
#   NEW_RELIC_LICENSE_KEY  chave do New Relic (vazio = agente carrega mas não reporta)
#   OS_DB_NAME             database lógico da OS (padrão: oficina_os) — PRECISA existir (ver docs/DEPLOY.md)
#   OS_DB_USER / OS_DB_PASSWORD   credenciais do banco da OS; se vazios, usa as do RDS compartilhado (SSM + Secrets Manager)
#   CADASTROS_URL          URL do Cadastros; se vazia, lê o Parameter Store /oficina/{env}/services/cadastros/api-url
#
# ATENÇÃO (IAM do AWS Academy): os pods herdam a role do node (LabEksNodeRole), que pode NÃO ter permissão de
# SNS/SQS/SSM. Se AWS_SESSION_TOKEN estiver definido, as credenciais da sessão são injetadas no Secret do pod
# (AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY/AWS_SESSION_TOKEN). Elas expiram junto com a sessão do Academy:
# rode este script de novo para renová-las.
set -euo pipefail

ENVIRONMENT=${1:-homolog}
NS=oficina
IMAGE=${IMAGE:-ghcr.io/tech-challenge-oficina-mecanica-soat/oficina-os-service:latest}
export AWS_DEFAULT_REGION=${AWS_DEFAULT_REGION:-us-east-1}
cd "$(dirname "$0")/.."

ssm() { aws ssm get-parameter --name "$1" --query Parameter.Value --output text; }

echo "==> Deploy do OS Service | env=$ENVIRONMENT | imagem=$IMAGE"

CLUSTER_NAME=$(ssm "/oficina/$ENVIRONMENT/k8s/cluster-name")
aws eks update-kubeconfig --region "$AWS_DEFAULT_REGION" --name "$CLUSTER_NAME"

kubectl get namespace "$NS" >/dev/null 2>&1 || kubectl create namespace "$NS"

# --- Cadastros ---------------------------------------------------------------------------------------
if [ -z "${CADASTROS_URL:-}" ]; then
  CADASTROS_URL=$(ssm "/oficina/$ENVIRONMENT/services/cadastros/api-url" 2>/dev/null || true)
fi
if [ -z "${CADASTROS_URL:-}" ]; then
  echo "AVISO: URL do Cadastros não encontrada (SSM /oficina/$ENVIRONMENT/services/cadastros/api-url). Abrir OS vai responder 503 até configurar." >&2
  CADASTROS_URL="http://cadastros-service.${NS}"
fi

# --- Banco -------------------------------------------------------------------------------------------
DB_ENDPOINT=$(ssm "/oficina/$ENVIRONMENT/db/endpoint")
DB_PORT=$(ssm "/oficina/$ENVIRONMENT/db/port")
DB_NAME=${OS_DB_NAME:-oficina_os}
DB_USER=${OS_DB_USER:-$(ssm "/oficina/$ENVIRONMENT/db/username")}
DB_PASSWORD=${OS_DB_PASSWORD:-$(aws secretsmanager get-secret-value --secret-id "oficina/$ENVIRONMENT/db-password" --query SecretString --output text)}
CONNECTION_STRING="Host=$DB_ENDPOINT;Port=$DB_PORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD;Trust Server Certificate=true"

# --- Secret do serviço ---------------------------------------------------------------------------------
SECRET_ARGS=(
  --from-literal=ConnectionStrings__Default="$CONNECTION_STRING"
  --from-literal=NEW_RELIC_LICENSE_KEY="${NEW_RELIC_LICENSE_KEY:-}"
)
if [ -n "${AWS_SESSION_TOKEN:-}" ]; then
  echo "Injetando credenciais da sessão AWS no Secret (expiram com a sessão do Academy)."
  SECRET_ARGS+=(
    --from-literal=AWS_ACCESS_KEY_ID="${AWS_ACCESS_KEY_ID:?}"
    --from-literal=AWS_SECRET_ACCESS_KEY="${AWS_SECRET_ACCESS_KEY:?}"
    --from-literal=AWS_SESSION_TOKEN="$AWS_SESSION_TOKEN"
  )
fi
kubectl create secret generic os-service-secrets --namespace "$NS" "${SECRET_ARGS[@]}" \
  --dry-run=client -o yaml | kubectl apply -f -

# --- Pull secret do GHCR (mesmo nome usado pelo oficina-infra-k8s) -----------------------------------
if [ -n "${GHCR_USERNAME:-}" ] && [ -n "${GHCR_PAT:-}" ]; then
  kubectl create secret docker-registry ghcr-pull-secret --namespace "$NS" \
    --docker-server=ghcr.io --docker-username="$GHCR_USERNAME" --docker-password="$GHCR_PAT" \
    --dry-run=client -o yaml | kubectl apply -f -
elif ! kubectl get secret ghcr-pull-secret -n "$NS" >/dev/null 2>&1; then
  echo "AVISO: secret ghcr-pull-secret não existe e GHCR_USERNAME/GHCR_PAT não foram informados: o pod não conseguirá puxar a imagem privada." >&2
fi

# --- Manifestos ------------------------------------------------------------------------------------------
export ENVIRONMENT CADASTROS_URL IMAGE
envsubst '${ENVIRONMENT} ${CADASTROS_URL}' < k8s/configmap.yaml | kubectl apply -f -
envsubst '${IMAGE}' < k8s/deployment.yaml | kubectl apply -f -
kubectl apply -f k8s/service.yaml -f k8s/hpa.yaml

# O Secret é lido na criação do pod: reinicia para pegar credenciais/connection string novas.
kubectl rollout restart deployment/oficina-os-service -n "$NS"
kubectl rollout status deployment/oficina-os-service -n "$NS" --timeout=5m

# --- Publica o endpoint para Billing/Execução (contratos/eventos/schemas.md) ---------------------------
echo "Aguardando o NLB provisionar..."
LB_HOSTNAME=""
for _ in $(seq 1 18); do
  LB_HOSTNAME=$(kubectl get svc oficina-os-service -n "$NS" -o jsonpath='{.status.loadBalancer.ingress[0].hostname}' 2>/dev/null || true)
  [ -n "$LB_HOSTNAME" ] && break
  sleep 10
done
if [ -z "$LB_HOSTNAME" ]; then
  echo "NLB ainda sem hostname. Rode 'kubectl get svc oficina-os-service -n $NS' e publique manualmente em /oficina/$ENVIRONMENT/services/os/api-url." >&2
  exit 1
fi
aws ssm put-parameter --name "/oficina/$ENVIRONMENT/services/os/api-url" --type String \
  --value "http://$LB_HOSTNAME" --overwrite >/dev/null

echo "Deploy concluído. OS Service: http://$LB_HOSTNAME  (health: /health/ready)"
