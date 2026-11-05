#!/usr/bin/env bash
# Demonstra a Saga completa (caminho feliz) no ambiente local: a OS é aberta pela API e os outros serviços
# (Execução, Billing) são simulados por eventos no LocalStack (publisher fake).
#   docker compose up --build -d   # antes
#   scripts/demo-happy-path.sh
source "$(dirname "$0")/_demo-lib.sh"

DIAG=11111111-0000-0000-0000-000000000001
ORC=22222222-0000-0000-0000-000000000002
PAG=33333333-0000-0000-0000-000000000003

abrir_os "Barulho no motor"
expect_status "$OS_ID" AguardandoDiagnostico

step "Execução conclui o diagnóstico (evento diagnostico.concluido)"
emit diagnostico.concluido "{\"osId\":\"$OS_ID\",\"diagnosticoId\":\"$DIAG\"}"
expect_status "$OS_ID" EmDiagnostico

step "Billing gera o orçamento (evento orcamento.gerado)"
emit orcamento.gerado "{\"osId\":\"$OS_ID\",\"orcamentoId\":\"$ORC\",\"valorTotal\":1250.50}"
expect_status "$OS_ID" AguardandoAprovacao

step "Cliente aprova o orçamento (evento orcamento.aprovado)"
emit orcamento.aprovado "{\"osId\":\"$OS_ID\",\"orcamentoId\":\"$ORC\"}"
expect_status "$OS_ID" Aprovada

step "Execução inicia o reparo (evento execucao.iniciada)"
emit execucao.iniciada "{\"osId\":\"$OS_ID\",\"execucaoId\":\"44444444-0000-0000-0000-000000000004\"}"
expect_status "$OS_ID" EmReparo

step "Execução finaliza o reparo (evento execucao.finalizada)"
emit execucao.finalizada "{\"osId\":\"$OS_ID\",\"execucaoId\":\"44444444-0000-0000-0000-000000000004\"}"
expect_status "$OS_ID" AguardandoPagamento

step "Mercado Pago confirma o pagamento (evento pagamento.confirmado)"
emit pagamento.confirmado "{\"osId\":\"$OS_ID\",\"pagamentoId\":\"$PAG\",\"valor\":1250.50}"
expect_status "$OS_ID" Concluida

mostrar_historico

echo -e "\n${GREEN}════════════════════════════════════════${RESET}"
echo -e "${GREEN}✅ Saga completa (caminho feliz) — SUCESSO${RESET}"
echo -e "${GREEN}════════════════════════════════════════${RESET}"
