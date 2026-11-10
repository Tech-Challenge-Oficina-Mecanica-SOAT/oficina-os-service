# Terraform — mensageria do OS Service

Cria os tópicos SNS `oficina-{env}-os-criada` e `oficina-{env}-os-cancelada` e publica os ARNs no Parameter Store em
`/oficina/{env}/messaging/topics/{evento}/arn`, para Billing e Execução criarem suas filas/subscriptions.

```bash
cd terraform
terraform init
terraform plan  -var environment=homolog
terraform apply -var environment=homolog     # credenciais do AWS Academy exportadas (expiram em ~4h)
terraform destroy -var environment=homolog
```

Avisar o grupo depois do `apply`: "topics os.criada e os.cancelada no ar".

O database lógico `oficina_os` e a role PostgreSQL ainda não estão aqui: dependem do repo `oficina-infra-db`.

## Fila e DLQ (etapa 5)

`sqs.tf` cria `oficina-{env}-os-queue` (+ `oficina-{env}-os-dlq`, 3 tentativas), a policy que deixa os tópicos publicarem na fila e as subscriptions (`raw_message_delivery = true`) lendo os ARNs no Parameter Store.
O `apply` exige que os tópicos dos outros serviços já existam; enquanto isso, restrinja: `terraform apply -var environment=homolog -var 'consumed_events=["diagnostico.concluido"]'`.
