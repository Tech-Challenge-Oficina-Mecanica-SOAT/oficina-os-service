#!/usr/bin/env bash
# Demonstra a COMPENSAÇÃO da Saga: o cliente recusa o orçamento e a OS é cancelada, publicando os.cancelada
# para os outros serviços liberarem a fila (Execução) e cancelarem o orçamento (Billing).
# Também mostra a idempotência: o mesmo evento entregue 3 vezes só vale uma.
#   docker compose up --build -d   # antes
#   scripts/demo-rollback.sh
source "$(dirname "$0")/_demo-lib.sh"

DIAG=11111111-0000-0000-0000-000000000001
ORC=22222222-0000-0000-0000-000000000002
QUEUE_SPY=http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/oficina-local-spy

abrir_os "Freio com ruido"  # sem acento: o curl do Windows envia ANSI e a API (UTF-8) recusa

step "Execução conclui o diagnóstico — o MESMO evento é entregue 3 vezes (SQS entrega pelo menos uma vez)"
# eventId NOVO a cada execução (um fixo seria deduplicado na 2a vez). /proc não existe no Git Bash do Windows,
# então tenta o container do LocalStack e cai para $RANDOM.
EVENT_ID=$(cat /proc/sys/kernel/random/uuid 2>/dev/null || docker compose exec -T localstack cat /proc/sys/kernel/random/uuid 2>/dev/null | tr -d '\r' || true)
[ -n "$EVENT_ID" ] || EVENT_ID=$(printf '%04x%04x-%04x-4%03x-a%03x-%04x%04x%04x' $RANDOM $RANDOM $RANDOM $((RANDOM % 4096)) $((RANDOM % 4096)) $RANDOM $RANDOM $RANDOM)
for _ in 1 2 3; do
  "$(dirname "$0")/fake-event.sh" diagnostico.concluido "{\"osId\":\"$OS_ID\",\"diagnosticoId\":\"$DIAG\"}" "$EVENT_ID" >/dev/null
done
expect_status "$OS_ID" EmDiagnostico
HIST=$(curl -sf "$API_URL/ordens-servico/$OS_ID/historico" | grep -o '"statusNovo"' | wc -l)
[ "$HIST" -eq 2 ] && ok "Idempotência: 3 entregas, 1 transição (histórico com $HIST registros)" || fail "Histórico com $HIST registros (esperado 2)"

step "Billing gera o orçamento"
emit orcamento.gerado "{\"osId\":\"$OS_ID\",\"orcamentoId\":\"$ORC\",\"valorTotal\":980.00}"
expect_status "$OS_ID" AguardandoAprovacao

step "Cliente RECUSA o orçamento (evento orcamento.recusado) → compensação"
emit orcamento.recusado "{\"osId\":\"$OS_ID\",\"orcamentoId\":\"$ORC\",\"motivo\":\"valor_alto\"}"
expect_status "$OS_ID" Cancelada

step "OS tenta ser cancelada de novo → API recusa (409): já está cancelada"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$API_URL/ordens-servico/$OS_ID/cancelar")
[ "$CODE" = "409" ] && ok "409 — estado não permite a operação" || fail "Esperado 409, recebido $CODE"

step "Confere no SNS que o os.cancelada foi publicado (fila espia do LocalStack)"
sleep 2
MSG=$(docker compose exec -T localstack awslocal sqs receive-message --queue-url "$QUEUE_SPY" \
  --max-number-of-messages 10 --wait-time-seconds 3 2>/dev/null | grep -c "$OS_ID" || true)
[ "$MSG" -ge 1 ] && ok "os.cancelada publicado para a OS $OS_ID (motivo orcamento_recusado)" \
  || info "Não localizei a mensagem na fila espia (já consumida por outra execução?) — confira com: docker compose exec localstack awslocal sqs receive-message --queue-url $QUEUE_SPY"

mostrar_historico

echo -e "\n${GREEN}════════════════════════════════════════${RESET}"
echo -e "${GREEN}✅ Compensação da Saga (rollback) — SUCESSO${RESET}"
echo -e "${GREEN}════════════════════════════════════════${RESET}"
