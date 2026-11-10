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

`iam.tf` e a policy da fila dão aos pods (role do node, `LabEksNodeRole`, descoberta por regex; variável `node_role_name` força outra) `sns:Publish` nos tópicos do OS e o consumo da fila, por **policy de recurso** (o Academy não permite anexar policy à role nem IRSA; mesmo padrão do Cadastros). Exige o limite de salto 2 do IMDS no node group. O SSM não tem policy de recurso: o `scripts/deploy-k8s.sh` resolve os ARNs e a URL da fila e os passa por variável de ambiente.
O `apply` exige que os tópicos dos outros serviços já existam; enquanto isso, restrinja: `terraform apply -var environment=homolog -var 'consumed_events=["diagnostico.concluido"]'`.
