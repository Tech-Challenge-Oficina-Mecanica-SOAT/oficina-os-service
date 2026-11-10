#!/bin/bash
# Roda quando o LocalStack fica pronto. Monta no ambiente local o que na AWS e criado pelo Terraform:
#  - topicos publicados pelo OS Service (os.criada, os.cancelada) + fila "espia" que assina os dois;
#  - topicos que OUTROS servicos publicariam (usados pelo publisher fake: scripts/fake-event.sh);
#  - fila do OS (oficina-local-os-queue) com DLQ (3 tentativas) assinando esses topicos com entrega raw.
set -e
R=us-east-1
ACC=000000000000

topic() { awslocal sns create-topic --name "oficina-local-${1//./-}" --region $R >/dev/null; }
arn()   { echo "arn:aws:sns:$R:$ACC:oficina-local-${1//./-}"; }

# 1) topicos do OS Service + fila espia (para conferir o que a API publicou)
for e in os.criada os.cancelada; do topic $e; done
awslocal sqs create-queue --queue-name oficina-local-spy --region $R >/dev/null
for e in os.criada os.cancelada; do
  awslocal sns subscribe --region $R --topic-arn "$(arn $e)" --protocol sqs \
    --notification-endpoint "arn:aws:sqs:$R:$ACC:oficina-local-spy" --attributes RawMessageDelivery=true >/dev/null
done

# 2) topicos que o OS consome (publicados por Cadastros, Billing e Execucao)
CONSUMED="diagnostico.concluido orcamento.gerado orcamento.aprovado orcamento.recusado execucao.iniciada execucao.finalizada pagamento.confirmado pagamento.recusado cliente.desativado"
for e in $CONSUMED; do topic $e; done

# 3) DLQ + fila do OS (visibility curto so para a demo local da DLQ ser rapida)
awslocal sqs create-queue --queue-name oficina-local-os-dlq --region $R >/dev/null
DLQ_ARN="arn:aws:sqs:$R:$ACC:oficina-local-os-dlq"
awslocal sqs create-queue --queue-name oficina-local-os-queue --region $R \
  --attributes "{\"VisibilityTimeout\":\"4\",\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"$DLQ_ARN\\\",\\\"maxReceiveCount\\\":\\\"3\\\"}\"}" >/dev/null
for e in $CONSUMED; do
  awslocal sns subscribe --region $R --topic-arn "$(arn $e)" --protocol sqs \
    --notification-endpoint "arn:aws:sqs:$R:$ACC:oficina-local-os-queue" --attributes RawMessageDelivery=true >/dev/null
done

echo "LocalStack pronto: topicos, fila oficina-local-os-queue (+DLQ) e fila espia oficina-local-spy"
