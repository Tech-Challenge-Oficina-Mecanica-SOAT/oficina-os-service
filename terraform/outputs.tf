output "topic_arns" {
  description = "ARNs dos tópicos SNS publicados pelo OS Service"
  value       = { for k, t in aws_sns_topic.os : k => t.arn }
}
