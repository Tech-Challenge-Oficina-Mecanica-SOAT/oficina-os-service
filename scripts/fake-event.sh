#!/usr/bin/env bash
# Publisher FAKE: faz o papel do Billing/Execucao/Cadastros publicando um evento no SNS do LocalStack,
# no envelope padrao do contrato (contratos/eventos/schemas.md). Use com `docker compose up` rodando.
#
#   scripts/fake-event.sh <eventType> '<json do data>' [eventId]
#
# Exemplos:
#   scripts/fake-event.sh diagnostico.concluido '{"osId":"<ID>","diagnosticoId":"11111111-0000-0000-0000-000000000001"}'
#   scripts/fake-event.sh orcamento.recusado    '{"osId":"<ID>","orcamentoId":"22222222-0000-0000-0000-000000000002","motivo":"valor_alto"}'
#   scripts/fake-event.sh cliente.desativado    '{"clienteId":"<ID>","motivo":"inadimplencia"}'
# Repetir o mesmo eventId (3o argumento) simula entrega duplicada do SNS/SQS.
set -euo pipefail

if [ $# -lt 2 ]; then
  sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'
  exit 1
fi

export EVT_TYPE="$1" EVT_DATA="$2" EVT_ID="${3:-}"

docker compose exec -T -e EVT_TYPE -e EVT_DATA -e EVT_ID localstack bash -s <<'REMOTE'
set -e
EID="${EVT_ID:-$(cat /proc/sys/kernel/random/uuid)}"
TRACE="$(tr -d '-' < /proc/sys/kernel/random/uuid)"
SPAN="$(tr -d '-' < /proc/sys/kernel/random/uuid | cut -c1-16)"
NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
MSG="{\"eventId\":\"$EID\",\"eventType\":\"$EVT_TYPE\",\"eventVersion\":\"1.0\",\"occurredAt\":\"$NOW\",\"correlationId\":\"00-$TRACE-$SPAN-01\",\"producer\":\"fake-publisher\",\"data\":$EVT_DATA}"
awslocal sns publish --region us-east-1 \
  --topic-arn "arn:aws:sns:us-east-1:000000000000:oficina-local-${EVT_TYPE//./-}" \
  --message "$MSG" >/dev/null
echo "publicado $EVT_TYPE (eventId=$EID)"
REMOTE
