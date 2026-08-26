resource "aws_security_group" "alb" {
  name        = "${var.name_prefix}-alb"
  description = "Public HTTP ingress for the PulseFlow staging Application Load Balancer."
  vpc_id      = aws_vpc.staging.id

  tags = {
    Name = "${var.name_prefix}-alb"
  }
}

resource "aws_vpc_security_group_ingress_rule" "alb_http" {
  security_group_id = aws_security_group.alb.id
  cidr_ipv4         = "0.0.0.0/0"
  from_port         = 80
  to_port           = 80
  ip_protocol       = "tcp"
  description       = "Public staging HTTP ingress"
}

resource "aws_vpc_security_group_egress_rule" "alb_to_api" {
  security_group_id            = aws_security_group.alb.id
  referenced_security_group_id = aws_security_group.api.id
  from_port                    = 8080
  to_port                      = 8080
  ip_protocol                  = "tcp"
  description                  = "Forward HTTP traffic only to PulseFlow API tasks"
}

resource "aws_security_group" "api" {
  name        = "${var.name_prefix}-api"
  description = "PulseFlow API and one-shot staging task network boundary. No direct public application ingress."
  vpc_id      = aws_vpc.staging.id

  tags = {
    Name = "${var.name_prefix}-api"
  }
}

resource "aws_vpc_security_group_ingress_rule" "api_from_alb" {
  security_group_id            = aws_security_group.api.id
  referenced_security_group_id = aws_security_group.alb.id
  from_port                    = 8080
  to_port                      = 8080
  ip_protocol                  = "tcp"
  description                  = "Application Load Balancer traffic only"
}

resource "aws_vpc_security_group_egress_rule" "api_https" {
  security_group_id = aws_security_group.api.id
  cidr_ipv4         = "0.0.0.0/0"
  from_port         = 443
  to_port           = 443
  ip_protocol       = "tcp"
  description       = "GHCR pulls and AWS public API access from public-IP staging tasks"
}

resource "aws_vpc_security_group_egress_rule" "api_dns_udp" {
  security_group_id = aws_security_group.api.id
  cidr_ipv4         = "0.0.0.0/0"
  from_port         = 53
  to_port           = 53
  ip_protocol       = "udp"
  description       = "DNS resolution"
}

resource "aws_vpc_security_group_egress_rule" "api_dns_tcp" {
  security_group_id = aws_security_group.api.id
  cidr_ipv4         = "0.0.0.0/0"
  from_port         = 53
  to_port           = 53
  ip_protocol       = "tcp"
  description       = "DNS resolution fallback"
}

resource "aws_vpc_security_group_egress_rule" "api_to_postgres" {
  security_group_id            = aws_security_group.api.id
  referenced_security_group_id = aws_security_group.postgresql.id
  from_port                    = 5432
  to_port                      = 5432
  ip_protocol                  = "tcp"
  description                  = "PostgreSQL only"
}

resource "aws_vpc_security_group_egress_rule" "api_to_valkey" {
  security_group_id            = aws_security_group.api.id
  referenced_security_group_id = aws_security_group.valkey.id
  from_port                    = 6379
  to_port                      = 6379
  ip_protocol                  = "tcp"
  description                  = "Valkey TLS endpoint"
}

resource "aws_vpc_security_group_egress_rule" "api_to_rabbitmq" {
  security_group_id            = aws_security_group.api.id
  referenced_security_group_id = aws_security_group.rabbitmq.id
  from_port                    = 5671
  to_port                      = 5671
  ip_protocol                  = "tcp"
  description                  = "Amazon MQ secure AMQP only"
}

resource "aws_security_group" "postgresql" {
  name        = "${var.name_prefix}-postgresql"
  description = "Private RDS PostgreSQL access from API tasks only."
  vpc_id      = aws_vpc.staging.id

  tags = {
    Name = "${var.name_prefix}-postgresql"
  }
}

resource "aws_vpc_security_group_ingress_rule" "postgresql_from_api" {
  security_group_id            = aws_security_group.postgresql.id
  referenced_security_group_id = aws_security_group.api.id
  from_port                    = 5432
  to_port                      = 5432
  ip_protocol                  = "tcp"
  description                  = "PulseFlow API and controlled one-shot task PostgreSQL access"
}

resource "aws_security_group" "valkey" {
  name        = "${var.name_prefix}-valkey"
  description = "Private ElastiCache Serverless Valkey access from API tasks only."
  vpc_id      = aws_vpc.staging.id

  tags = {
    Name = "${var.name_prefix}-valkey"
  }
}

resource "aws_vpc_security_group_ingress_rule" "valkey_from_api" {
  security_group_id            = aws_security_group.valkey.id
  referenced_security_group_id = aws_security_group.api.id
  from_port                    = 6379
  to_port                      = 6379
  ip_protocol                  = "tcp"
  description                  = "PulseFlow API Valkey TLS access"
}

resource "aws_security_group" "rabbitmq" {
  name        = "${var.name_prefix}-rabbitmq"
  description = "Private Amazon MQ RabbitMQ AMQPS access from API tasks only."
  vpc_id      = aws_vpc.staging.id

  tags = {
    Name = "${var.name_prefix}-rabbitmq"
  }
}

resource "aws_vpc_security_group_ingress_rule" "rabbitmq_from_api" {
  security_group_id            = aws_security_group.rabbitmq.id
  referenced_security_group_id = aws_security_group.api.id
  from_port                    = 5671
  to_port                      = 5671
  ip_protocol                  = "tcp"
  description                  = "PulseFlow API secure AMQP access"
}
