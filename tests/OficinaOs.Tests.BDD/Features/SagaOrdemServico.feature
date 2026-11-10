# language: pt
Funcionalidade: Saga da Ordem de Serviço
  O OS Service é o orquestrador da Saga: abre a OS, reage aos fatos publicados pelos outros serviços
  (Execução, Billing, Cadastros) e compensa quando algo dá errado.

  Contexto:
    Dado que existe o cliente ativo com CPF "12345678900" e o veículo de placa "ABC1D23"

  Cenário: Fluxo completo da OS até a conclusão (caminho feliz)
    Quando o cliente abre uma OS com o defeito "Barulho no motor"
    Então a OS está com o status "AguardandoDiagnostico"
    E o evento "os.criada" foi enfileirado para publicação
    Quando a execução conclui o diagnóstico
    Então a OS está com o status "EmDiagnostico"
    Quando o billing gera o orçamento
    Então a OS está com o status "AguardandoAprovacao"
    Quando o cliente aprova o orçamento
    Então a OS está com o status "Aprovada"
    Quando a execução inicia o reparo
    Então a OS está com o status "EmReparo"
    Quando a execução finaliza o reparo
    Então a OS está com o status "AguardandoPagamento"
    Quando o pagamento é confirmado
    Então a OS está com o status "Concluida"
    E o histórico da OS tem 7 registros

  Cenário: Cliente recusa o orçamento e a OS é cancelada (compensação)
    Dado que existe uma OS com orçamento aguardando aprovação
    Quando o cliente recusa o orçamento
    Então a OS está com o status "Cancelada"
    E o evento "os.cancelada" foi enfileirado para publicação com o motivo "orcamento_recusado"

  Cenário: Pagamento recusado não cancela a OS e pode ser regularizado depois
    Dado que existe uma OS aguardando pagamento
    Quando o pagamento é recusado
    Então a OS está com o status "PendenteCobranca"
    Quando o pagamento é confirmado
    Então a OS está com o status "Concluida"

  Cenário: Cliente desativado tem as OSes abertas canceladas
    Dado que existe uma OS aberta
    Quando o cliente é desativado
    Então a OS está com o status "Cancelada"
    E o evento "os.cancelada" foi enfileirado para publicação com o motivo "cliente_desativado"

  Cenário: Entrega repetida do mesmo evento não é aplicada duas vezes
    Dado que existe uma OS aberta
    Quando o mesmo evento de diagnóstico concluído chega 3 vezes
    Então a OS está com o status "EmDiagnostico"
    E o histórico da OS tem 2 registros

  Cenário: Evento fora de ordem é ignorado sem quebrar a OS
    Dado que existe uma OS aberta
    Quando a execução finaliza o reparo
    Então a OS está com o status "AguardandoDiagnostico"

  Cenário: Não é possível cancelar a OS depois que o reparo começou
    Dado que existe uma OS em reparo
    Quando alguém tenta cancelar a OS
    Então a resposta é 409
    E a OS está com o status "EmReparo"

  Cenário: Cliente inativo não consegue abrir uma OS
    Dado que existe o cliente inativo com CPF "99999999999" e o veículo de placa "ABC1D23"
    Quando o cliente abre uma OS com o defeito "Qualquer defeito" usando o CPF "99999999999"
    Então a resposta é 403
