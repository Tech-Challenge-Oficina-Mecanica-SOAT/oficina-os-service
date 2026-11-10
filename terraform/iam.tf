# Permissões dos pods por policy de RECURSO (AWS Academy: não dá para anexar policy à LabEksNodeRole nem usar IRSA).
# Mesmo padrão validado no oficina-cadastros-service (terraform/sns-policy.tf). Requer o limite de salto 2 do IMDS
# no node group (já na main do oficina-infra-k8s) para o pod alcançar as credenciais do node.
# O SSM não tem policy de recurso: por isso o deploy passa os ARNs dos tópicos por variável (Messaging__TopicArns__*).

data "aws_caller_identity" "current" {}

data "aws_iam_role" "node" {
  name = var.node_role_name
}

# Publicação: os pods (role do node) podem publicar nos tópicos do OS.
resource "aws_sns_topic_policy" "os" {
  for_each = aws_sns_topic.os
  arn      = each.value.arn

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid       = "DonoDaContaGerencia"
        Effect    = "Allow"
        Principal = { AWS = "*" }
        Action    = ["SNS:GetTopicAttributes", "SNS:SetTopicAttributes", "SNS:AddPermission", "SNS:RemovePermission", "SNS:DeleteTopic", "SNS:Subscribe", "SNS:ListSubscriptionsByTopic", "SNS:Publish"]
        Resource  = each.value.arn
        Condition = { StringEquals = { "AWS:SourceOwner" = data.aws_caller_identity.current.account_id } }
      },
      {
        Sid       = "PodsDoClusterPublicam"
        Effect    = "Allow"
        Principal = { AWS = data.aws_iam_role.node.arn }
        Action    = "SNS:Publish"
        Resource  = each.value.arn
      }
    ]
  })
}
