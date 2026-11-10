# ADR 0001 — Saga orquestrada, com o orquestrador no OS Service

Status: aceita · Relacionada à ADR 0004 do Billing (decisão global do grupo).

## Contexto
O Tech Challenge exige coordenar as transações distribuídas da OS (abrir → diagnosticar → orçar → aprovar → executar → pagar) com rollback/compensação em qualquer etapa, e pede para documentar e justificar a escolha entre Saga **orquestrada** e **coreografada**.

## Decisão
**Orquestrada.** O OS Service é o dono do estado global da OS e decide o próximo passo ao receber os eventos dos outros serviços (Execução, Billing, Cadastros). Os demais serviços são *participantes*: executam a etapa local, publicam o desfecho (`diagnostico.concluido`, `orcamento.*`, `execucao.*`, `pagamento.*`) e atendem às compensações (`os.cancelada`), sem conhecer o fluxo inteiro.

## Justificativa
- O fluxo é **linear, com poucos serviços e com compensação bem definida**: um ponto central que conhece o estado facilita depurar e rastrear ("em que passo a OS está e por quê?" é uma consulta ao histórico da OS).
- Na coreografia, regras como "o que acontece se o orçamento for recusado" ficariam espalhadas nos 4 serviços e cada novo passo exigiria mudar vários deles.
- A máquina de estados fica num lugar só (`OrdemServico`), com transições validadas e histórico (`historico_status_os`), o que também simplifica os testes (unitários e BDD).
- **Custo aceito:** o OS Service vira ponto de coordenação. Mitigações: ele só **reage a eventos** (stateless fora do banco), usa **Outbox** (nenhum evento se perde se o SNS cair), **idempotência** por `eventId` (reentrega não duplica efeito) e **DLQ** para falhas técnicas.

## Compensações
| Falha | Reação do OS | Efeito nos outros |
|---|---|---|
| `orcamento.recusado` | OS `Cancelada` + `os.cancelada` (`orcamento_recusado`) | Execução libera a fila; Billing cancela o orçamento |
| `cliente.desativado` | cancela as OS abertas do cliente + `os.cancelada` (`cliente_desativado`) | idem |
| `pagamento.recusado` | OS `PendenteCobranca` (não cancela: o serviço já foi feito); `pagamento.confirmado` posterior conclui | — |
| Cancelamento manual (`POST /ordens-servico/{id}/cancelar`, até `Aprovada`) | OS `Cancelada` + `os.cancelada` | Billing cancela/estorna; Execução cancela |

## Alternativas descartadas
- **Coreografia pura:** menos acoplamento ao OS, porém regras de compensação difusas e rastreio mais difícil com 4 serviços.
- **Orquestrador separado (novo serviço):** mais um repo, banco e pipeline sem ganho para o escopo; o OS já é o dono natural do estado.
