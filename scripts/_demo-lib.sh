#!/usr/bin/env bash
# Funções comuns dos scripts de demonstração local (docker compose up). Não executar diretamente.
set -euo pipefail

API_URL=${API_URL:-http://localhost:8080}
GREEN='\033[0;32m'; RED='\033[0;31m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; RESET='\033[0m'
STEP=0

step() { STEP=$((STEP+1)); echo -e "\n${YELLOW}▶ ${STEP}. $1${RESET}"; }
ok()   { echo -e "${GREEN}✅ $1${RESET}"; }
fail() { echo -e "${RED}❌ $1${RESET}"; exit 1; }
info() { echo -e "${CYAN}   $1${RESET}"; }

# Extrai um campo string simples do JSON (sem depender de jq).
json_field() { sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" | head -1; }

os_status() { curl -sf "$API_URL/ordens-servico/$1" | json_field status; }

# Espera a OS chegar ao status esperado (o consumidor SQS é assíncrono).
expect_status() {
  local id=$1 esperado=$2 atual=""
  for _ in $(seq 1 20); do
    atual=$(os_status "$id" || true)
    [ "$atual" = "$esperado" ] && { ok "OS está em $esperado"; return 0; }
    sleep 1
  done
  fail "Esperado status $esperado, mas a OS está em '${atual:-?}'"
}

# Publica, via LocalStack, um evento que outro serviço publicaria (ver scripts/fake-event.sh).
emit() {
  local tipo=$1 data=$2
  "$(dirname "${BASH_SOURCE[0]}")/fake-event.sh" "$tipo" "$data" >/dev/null
  info "evento publicado: $tipo"
}

abrir_os() {
  local defeito=$1
  step "Abrindo OS ($defeito)"
  local resp; resp=$(curl -sf -X POST "$API_URL/ordens-servico" -H 'Content-Type: application/json' \
    -d "{\"cpf\":\"12345678900\",\"placa\":\"ABC1D23\",\"defeitoRelatado\":\"$defeito\"}") || fail "Falha ao abrir OS (a API e o Wiremock estão no ar? docker compose up)"
  OS_ID=$(echo "$resp" | json_field id)
  [ -n "$OS_ID" ] || fail "Resposta sem id: $resp"
  ok "OS $OS_ID criada em $(echo "$resp" | json_field status)"
}

mostrar_historico() {
  step "Histórico de status da OS"
  curl -sf "$API_URL/ordens-servico/$OS_ID/historico" \
    | grep -o '"statusNovo":"[^"]*"' | cut -d'"' -f4 | paste -sd'>' - | sed 's/>/ → /g' | sed 's/^/   /'
}
