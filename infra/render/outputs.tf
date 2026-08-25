output "api_url" {
  description = "Render-managed public HTTPS URL for PulseFlow.Api."
  value       = render_web_service.api.url
}

output "api_service_id" {
  description = "Render service ID for PulseFlow.Api."
  value       = render_web_service.api.id
}

output "postgres_id" {
  description = "Render PostgreSQL resource ID."
  value       = render_postgres.pulseflow.id
}

output "key_value_id" {
  description = "Render Key Value resource ID."
  value       = render_keyvalue.rate_limit.id
}

output "rabbitmq_service_id" {
  description = "Render private-service ID for RabbitMQ."
  value       = render_private_service.rabbitmq.id
}

output "rabbitmq_internal_hostname" {
  description = "Private Render hostname used by PulseFlow.Api for AMQP."
  value       = render_private_service.rabbitmq.slug
}
