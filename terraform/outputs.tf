output "queue_url" {
  description = "URL da fila do OS Service (Messaging:QueueUrl)"
  value       = aws_sqs_queue.os.id
}

output "dlq_url" {
  description = "URL da DLQ do OS Service"
  value       = aws_sqs_queue.os_dlq.id
}

output "topic_arns" {
  description = "ARNs dos tópicos SNS publicados pelo OS Service"
  value       = { for k, t in aws_sns_topic.os : k => t.arn }
}
