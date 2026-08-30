# The GHCR credential is bootstrapped outside Terraform. It must contain exactly
# {"username":"<github-user>","password":"<package-read-token>"} as required by ECS.

resource "aws_secretsmanager_secret" "database_connection" {
  name                    = "${var.name_prefix}/runtime/database-connection"
  description             = "PulseFlow runtime PostgreSQL Npgsql connection string"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret" "rabbitmq_connection" {
  name                    = "${var.name_prefix}/runtime/rabbitmq-connection"
  description             = "PulseFlow runtime Amazon MQ AMQPS connection string"
  recovery_window_in_days = 0
}
