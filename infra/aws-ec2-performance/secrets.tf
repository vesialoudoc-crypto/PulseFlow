resource "random_password" "postgres" {
  length  = 32
  special = false
}

resource "random_password" "rabbitmq" {
  length  = 32
  special = false
}

# Runtime node scripts retrieve these secret values through narrowly scoped instance
# roles. The random values are still sensitive Terraform state and must remain local
# and protected like the existing managed-AWS environment state.
resource "aws_secretsmanager_secret" "postgres_credentials" {
  name                    = "${var.name_prefix}/runtime/postgres-credentials"
  description             = "PulseFlow EC2 performance PostgreSQL container credential"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "postgres_credentials" {
  secret_id = aws_secretsmanager_secret.postgres_credentials.id
  secret_string = jsonencode({
    username = "pulseflow"
    password = random_password.postgres.result
  })
}

resource "aws_secretsmanager_secret" "rabbitmq_credentials" {
  name                    = "${var.name_prefix}/runtime/rabbitmq-credentials"
  description             = "PulseFlow EC2 performance RabbitMQ container credential"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "rabbitmq_credentials" {
  secret_id = aws_secretsmanager_secret.rabbitmq_credentials.id
  secret_string = jsonencode({
    username = "pulseflow"
    password = random_password.rabbitmq.result
  })
}
