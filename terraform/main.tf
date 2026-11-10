terraform {
  required_version = ">= 1.9.0"
  required_providers {
    aws = { source = "hashicorp/aws", version = ">= 5.81, < 7.0" }
  }
}

provider "aws" {
  region = var.region
}

# Tópicos publicados pelo OS Service (contratos/eventos/schemas.md).
# O "." do nome do evento vira "-" no nome do tópico (a AWS não aceita ponto), mas o caminho
# do Parameter Store mantém o nome original do evento — é por ele que os consumidores descobrem o ARN.
locals {
  topics = toset(["os.criada", "os.cancelada"])

  tags = {
    Project   = "oficina-mecanica"
    Service   = "os-service"
    Env       = var.environment
    ManagedBy = "terraform"
  }
}

resource "aws_sns_topic" "os" {
  for_each = local.topics
  name     = "oficina-${var.environment}-${replace(each.key, ".", "-")}"

  tags = local.tags
}

resource "aws_ssm_parameter" "topic_arn" {
  for_each    = local.topics
  name        = "/oficina/${var.environment}/messaging/topics/${each.key}/arn"
  type        = "String"
  value       = aws_sns_topic.os[each.key].arn
  description = "ARN do tópico SNS ${each.key} (publicado pelo OS Service)"
}
