variable "environment" {
  description = "Ambiente: homolog ou prod"
  type        = string

  validation {
    condition     = contains(["homolog", "prod"], var.environment)
    error_message = "environment deve ser homolog ou prod."
  }
}

variable "consumed_events" {
  description = "Eventos de outros serviços que a fila do OS assina. O parâmetro /oficina/{env}/messaging/topics/{evento}/arn de cada um precisa existir. Reduza a lista enquanto algum tópico ainda não foi criado pelo dono."
  type        = list(string)
  default = [
    "diagnostico.concluido",
    "orcamento.gerado",
    "orcamento.aprovado",
    "orcamento.recusado",
    "execucao.iniciada",
    "execucao.finalizada",
    "pagamento.confirmado",
    "pagamento.recusado",
    "cliente.desativado",
  ]
}

variable "region" {
  description = "Região AWS (AWS Academy usa us-east-1)"
  type        = string
  default     = "us-east-1"
}

variable "node_role_name" {
  description = "Role dos nodes do EKS, que os pods herdam (AWS Academy: LabEksNodeRole). Recebe as policies de recurso do SNS e da SQS."
  type        = string
  default     = "LabEksNodeRole"
}
