resource "aws_vpc" "environment" {
  cidr_block           = var.vpc_cidr
  enable_dns_hostnames = true
  enable_dns_support   = true

  tags = {
    Name = "pulseflow-ec2-low-performance-vpc"
  }
}

resource "aws_internet_gateway" "environment" {
  vpc_id = aws_vpc.environment.id

  tags = {
    Name = "pulseflow-ec2-low-performance-igw"
  }
}

resource "aws_subnet" "public" {
  vpc_id                  = aws_vpc.environment.id
  availability_zone       = local.availability_zone
  cidr_block              = var.public_subnet_cidr
  map_public_ip_on_launch = true

  tags = {
    Name = "pulseflow-ec2-low-performance-public-${local.availability_zone}"
  }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.environment.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.environment.id
  }

  tags = {
    Name = "pulseflow-ec2-low-performance-public"
  }
}

resource "aws_route_table_association" "public" {
  subnet_id      = aws_subnet.public.id
  route_table_id = aws_route_table.public.id
}

resource "aws_security_group" "app" {
  name        = "pulseflow-app"
  description = "Allows approved external HTTP traffic to the PulseFlow application host."
  vpc_id      = aws_vpc.environment.id

  ingress {
    description = "Approved external test traffic"
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = [var.allowed_http_source_cidr]
  }

  egress {
    description = "Outbound connectivity for host operations and future runtime installation"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "pulseflow-app"
  }
}

resource "aws_security_group" "rabbitmq" {
  name        = "pulseflow-rabbitmq"
  description = "Allows AMQP traffic only from the PulseFlow application host."
  vpc_id      = aws_vpc.environment.id

  ingress {
    description     = "PulseFlow application AMQP traffic"
    from_port       = 5672
    to_port         = 5672
    protocol        = "tcp"
    security_groups = [aws_security_group.app.id]
  }

  egress {
    description = "Outbound connectivity for host operations and future runtime installation"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "pulseflow-rabbitmq"
  }
}

resource "aws_security_group" "redis" {
  name        = "pulseflow-redis"
  description = "Allows Redis traffic only from the PulseFlow application host."
  vpc_id      = aws_vpc.environment.id

  ingress {
    description     = "PulseFlow application Redis traffic"
    from_port       = 6379
    to_port         = 6379
    protocol        = "tcp"
    security_groups = [aws_security_group.app.id]
  }

  egress {
    description = "Outbound connectivity for host operations and future runtime installation"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "pulseflow-redis"
  }
}

resource "aws_security_group" "postgres" {
  name        = "pulseflow-postgres"
  description = "Allows PostgreSQL traffic only from the PulseFlow application host."
  vpc_id      = aws_vpc.environment.id

  ingress {
    description     = "PulseFlow application PostgreSQL traffic"
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [aws_security_group.app.id]
  }

  egress {
    description = "Outbound connectivity for host operations and future runtime installation"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "pulseflow-postgres"
  }
}
