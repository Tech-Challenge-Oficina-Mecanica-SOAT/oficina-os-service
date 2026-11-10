# Fila do OS Service (consumo) + DLQ + subscriptions nos tópicos dos outros serviços.
# Convenção do schemas.md: consumers criam a fila e as subscriptions lendo o ARN dos tópicos no Parameter Store.
#
# ATENCAO: o `apply` só funciona se os parâmetros /oficina/{env}/messaging/topics/{evento}/arn já existirem
# (ou seja, o dono de cada tópico já aplicou o Terraform dele). Enquanto algum não existir, restrinja a lista
# com -var 'consumed_events=["diagnostico.concluido"]' e amplie depois.

data "aws_ssm_parameter" "consumed_topic" {
  for_each = toset(var.consumed_events)
  name     = "/oficina/${var.environment}/messaging/topics/${each.key}/arn"
}

resource "aws_sqs_queue" "os_dlq" {
  name                      = "oficina-${var.environment}-os-dlq"
  message_retention_seconds = 1209600 # 14 dias para investigar

  tags = local.tags
}

resource "aws_sqs_queue" "os" {
  name                       = "oficina-${var.environment}-os-queue"
  visibility_timeout_seconds = 60
  message_retention_seconds  = 345600 # 4 dias

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.os_dlq.arn
    maxReceiveCount     = 3 # após 3 recebimentos sem sucesso, vai para a DLQ
  })

  tags = local.tags
}

# Permite que os tópicos publiquem na fila (sem isso o SNS não entrega e nada chega, sem erro visível).
data "aws_iam_policy_document" "os_queue" {
  count = length(var.consumed_events) > 0 ? 1 : 0

  statement {
    sid       = "AllowSnsTopicsToSend"
    effect    = "Allow"
    actions   = ["sqs:SendMessage"]
    resources = [aws_sqs_queue.os.arn]

    principals {
      type        = "Service"
      identifiers = ["sns.amazonaws.com"]
    }

    condition {
      test     = "ArnEquals"
      variable = "aws:SourceArn"
      values   = [for p in data.aws_ssm_parameter.consumed_topic : p.value]
    }
  }
}

resource "aws_sqs_queue_policy" "os" {
  count     = length(var.consumed_events) > 0 ? 1 : 0
  queue_url = aws_sqs_queue.os.id
  policy    = data.aws_iam_policy_document.os_queue[0].json
}

# raw_message_delivery = true: o corpo da mensagem na fila é o envelope direto (não embrulhado pelo SNS).
resource "aws_sns_topic_subscription" "os" {
  for_each             = data.aws_ssm_parameter.consumed_topic
  topic_arn            = each.value.value
  protocol             = "sqs"
  endpoint             = aws_sqs_queue.os.arn
  raw_message_delivery = true

  depends_on = [aws_sqs_queue_policy.os]
}
