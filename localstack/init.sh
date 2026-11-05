#!/bin/bash
# Roda quando o LocalStack fica pronto: cria os topicos do OS Service e uma fila "espia" assinando ambos,
# para conferir no ambiente local que os eventos realmente chegaram ao SNS.
set -e
REGION=us-east-1
for t in os-criada os-cancelada; do
  awslocal sns create-topic --name "oficina-local-$t" --region $REGION >/dev/null
done
awslocal sqs create-queue --queue-name oficina-local-spy --region $REGION >/dev/null
SPY_ARN=arn:aws:sqs:$REGION:000000000000:oficina-local-spy
for t in os-criada os-cancelada; do
  awslocal sns subscribe --region $REGION --topic-arn "arn:aws:sns:$REGION:000000000000:oficina-local-$t" --protocol sqs --notification-endpoint "$SPY_ARN" --attributes RawMessageDelivery=true >/dev/null
done
echo "LocalStack pronto: topicos oficina-local-os-criada/os-cancelada + fila oficina-local-spy"
