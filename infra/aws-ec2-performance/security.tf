resource "aws_security_group" "app" {
  name        = "${var.name_prefix}-app"
  description = "HAProxy ingress and application egress for the PulseFlow EC2 performance node."
  vpc_id      = aws_vpc.performance.id

  ingress {
    description     = "k6 load generator access to HAProxy only"
    from_port       = 80
    to_port         = 80
    protocol        = "tcp"
    security_groups = [aws_security_group.loadgen.id]
  }

  egress {
    description = "Docker image pulls, package bootstrap, and SSM control channel"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    description = "VPC DNS resolution"
    from_port   = 53
    to_port     = 53
    protocol    = "udp"
    cidr_blocks = [var.vpc_cidr]
  }

  egress {
    description = "VPC DNS TCP fallback"
    from_port   = 53
    to_port     = 53
    protocol    = "tcp"
    cidr_blocks = [var.vpc_cidr]
  }

  egress {
    description = "PostgreSQL private node only"
    from_port   = 5432
    to_port     = 5432
    protocol    = "tcp"
    cidr_blocks = ["${local.node_private_ips.postgres}/32"]
  }

  egress {
    description = "RabbitMQ private node only"
    from_port   = 5672
    to_port     = 5672
    protocol    = "tcp"
    cidr_blocks = ["${local.node_private_ips.rabbitmq}/32"]
  }

  egress {
    description = "Redis private node only"
    from_port   = 6379
    to_port     = 6379
    protocol    = "tcp"
    cidr_blocks = ["${local.node_private_ips.redis}/32"]
  }

  tags = {
    Name = "${var.name_prefix}-app"
  }
}

resource "aws_security_group" "rabbitmq" {
  name        = "${var.name_prefix}-rabbitmq"
  description = "RabbitMQ accepts AMQP only from the application node."
  vpc_id      = aws_vpc.performance.id

  ingress {
    description     = "PulseFlow application AMQP traffic"
    from_port       = 5672
    to_port         = 5672
    protocol        = "tcp"
    security_groups = [aws_security_group.app.id]
  }

  egress {
    description = "Docker image pulls, package bootstrap, and SSM control channel"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    description = "VPC DNS resolution"
    from_port   = 53
    to_port     = 53
    protocol    = "udp"
    cidr_blocks = [var.vpc_cidr]
  }

  egress {
    description = "VPC DNS TCP fallback"
    from_port   = 53
    to_port     = 53
    protocol    = "tcp"
    cidr_blocks = [var.vpc_cidr]
  }

  tags = {
    Name = "${var.name_prefix}-rabbitmq"
  }
}

resource "aws_security_group" "redis" {
  name        = "${var.name_prefix}-redis"
  description = "Redis accepts rate-limit traffic only from the application node."
  vpc_id      = aws_vpc.performance.id

  ingress {
    description     = "PulseFlow application Redis traffic"
    from_port       = 6379
    to_port         = 6379
    protocol        = "tcp"
    security_groups = [aws_security_group.app.id]
  }

  egress {
    description = "Docker image pulls, package bootstrap, and SSM control channel"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    description = "VPC DNS resolution"
    from_port   = 53
    to_port     = 53
    protocol    = "udp"
    cidr_blocks = [var.vpc_cidr]
  }

  egress {
    description = "VPC DNS TCP fallback"
    from_port   = 53
    to_port     = 53
    protocol    = "tcp"
    cidr_blocks = [var.vpc_cidr]
  }

  tags = {
    Name = "${var.name_prefix}-redis"
  }
}

resource "aws_security_group" "postgres" {
  name        = "${var.name_prefix}-postgres"
  description = "PostgreSQL accepts database traffic only from the application node."
  vpc_id      = aws_vpc.performance.id

  ingress {
    description     = "PulseFlow application PostgreSQL traffic"
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [aws_security_group.app.id]
  }

  egress {
    description = "Docker image pulls, package bootstrap, and SSM control channel"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    description = "VPC DNS resolution"
    from_port   = 53
    to_port     = 53
    protocol    = "udp"
    cidr_blocks = [var.vpc_cidr]
  }

  egress {
    description = "VPC DNS TCP fallback"
    from_port   = 53
    to_port     = 53
    protocol    = "tcp"
    cidr_blocks = [var.vpc_cidr]
  }

  tags = {
    Name = "${var.name_prefix}-postgres"
  }
}

resource "aws_security_group" "loadgen" {
  name        = "${var.name_prefix}-loadgen"
  description = "k6 has egress only to HAProxy plus required bootstrap and SSM endpoints."
  vpc_id      = aws_vpc.performance.id

  egress {
    description = "k6 to HAProxy private node only"
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["${local.node_private_ips.app}/32"]
  }

  egress {
    description = "Docker image pulls, package bootstrap, and SSM control channel"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    description = "VPC DNS resolution"
    from_port   = 53
    to_port     = 53
    protocol    = "udp"
    cidr_blocks = [var.vpc_cidr]
  }

  egress {
    description = "VPC DNS TCP fallback"
    from_port   = 53
    to_port     = 53
    protocol    = "tcp"
    cidr_blocks = [var.vpc_cidr]
  }

  tags = {
    Name = "${var.name_prefix}-loadgen"
  }
}
