resource "aws_elasticache_serverless_cache" "valkey" {
  name                 = "${var.name_prefix}-valkey"
  description          = "Disposable PulseFlow distributed rate-limit state"
  engine               = "valkey"
  major_engine_version = "8"
  network_type         = "ipv4"
  security_group_ids   = [aws_security_group.valkey.id]
  subnet_ids           = values(aws_subnet.data)[*].id

  cache_usage_limits {
    data_storage {
      maximum = var.valkey_maximum_data_storage_gb
      unit    = "GB"
    }

    ecpu_per_second {
      maximum = var.valkey_maximum_ecpu_per_second
    }
  }

  tags = {
    Name = "${var.name_prefix}-valkey"
  }
}

resource "random_password" "rabbitmq" {
  length  = 32
  special = true
}

resource "aws_mq_broker" "rabbitmq" {
  broker_name                = "${var.name_prefix}-rabbitmq"
  engine_type                = "RabbitMQ"
  engine_version             = "4.2"
  host_instance_type         = "mq.m7g.medium"
  deployment_mode            = "SINGLE_INSTANCE"
  publicly_accessible        = false
  auto_minor_version_upgrade = true
  apply_immediately          = true
  security_groups            = [aws_security_group.rabbitmq.id]
  subnet_ids                 = [values(aws_subnet.data)[0].id]

  user {
    username = var.rabbitmq_username
    password = random_password.rabbitmq.result
  }

  logs {
    general = true
  }

  tags = {
    Name = "${var.name_prefix}-rabbitmq"
  }
}
