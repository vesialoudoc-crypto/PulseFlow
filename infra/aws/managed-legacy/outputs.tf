output "aws_region" {
  description = "Selected AWS region."
  value       = data.aws_region.current.region
}

output "aws_account_id" {
  description = "AWS account that owns the Terraform-managed staging resources."
  value       = data.aws_caller_identity.current.account_id
}

output "api_url" {
  description = "Public HTTP-only Application Load Balancer URL."
  value       = "http://${aws_lb.api.dns_name}"
}

output "api_cluster_name" {
  description = "ECS cluster name used by the deployment script."
  value       = aws_ecs_cluster.staging.name
}

output "api_service_name" {
  description = "ECS API service name controlled by the migration-first deployment script."
  value       = aws_ecs_service.api.name
}

output "api_task_definition_arn" {
  description = "Candidate immutable-image ECS API task definition ARN."
  value       = aws_ecs_task_definition.api.arn
}

output "migration_task_definition_arn" {
  description = "Same-image one-shot migration ECS task definition ARN."
  value       = aws_ecs_task_definition.migration.arn
}

output "database_verifier_task_definition_arn" {
  description = "One-shot PostgreSQL verifier task definition ARN for the exact smoke-test record."
  value       = aws_ecs_task_definition.database_verifier.arn
}

output "api_security_group_id" {
  description = "API task security group ID used by the deployment and verifier tasks."
  value       = aws_security_group.api.id
}

output "api_subnet_ids" {
  description = "Public subnet IDs used by public-IP Fargate tasks."
  value       = values(aws_subnet.public)[*].id
}

output "rds_master_user_secret_arn" {
  description = "RDS-managed master credential secret ARN. Its value is not held in Terraform state."
  value       = aws_db_instance.postgresql.master_user_secret[0].secret_arn
}

output "database_connection_secret_arn" {
  description = "Secret ARN populated by the deployment script with the complete application PostgreSQL connection string."
  value       = aws_secretsmanager_secret.database_connection.arn
}

output "rabbitmq_connection_secret_arn" {
  description = "Secret ARN populated by the deployment script with the complete application AMQPS connection string."
  value       = aws_secretsmanager_secret.rabbitmq_connection.arn
}

output "rabbitmq_amqps_endpoint" {
  description = "Private Amazon MQ AMQPS endpoint used to construct the runtime RabbitMQ URI."
  value       = aws_mq_broker.rabbitmq.instances[0].endpoints[0]
}

output "rabbitmq_broker_id" {
  description = "Amazon MQ broker ID used to verify the broker state during deployment."
  value       = aws_mq_broker.rabbitmq.id
}

output "rabbitmq_username" {
  description = "Initial Amazon MQ RabbitMQ user name."
  value       = var.rabbitmq_username
}

output "rabbitmq_password" {
  description = "Generated initial Amazon MQ RabbitMQ password. Sensitive: deployment automation reads it without printing it."
  value       = random_password.rabbitmq.result
  sensitive   = true
}

output "cloudwatch_log_group_name" {
  description = "CloudWatch log group for API, migration, and one-shot verifier task evidence."
  value       = aws_cloudwatch_log_group.api.name
}
