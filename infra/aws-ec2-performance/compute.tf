locals {
  app_user_data = templatefile("${path.module}/templates/bootstrap.sh.tftpl", {
    compose_file_base64 = base64encode(templatefile("${path.module}/templates/app-compose.yaml.tftpl", {
      pulseflow_image     = var.pulseflow_image
      postgres_private_ip = local.node_private_ips.postgres
      rabbitmq_private_ip = local.node_private_ips.rabbitmq
      redis_private_ip    = local.node_private_ips.redis
    }))
    docker_compose_version = var.docker_compose_version
    helper_files = {
      "haproxy.cfg" = base64encode(file("${path.module}/templates/haproxy.cfg"))
      "start-app.sh" = base64encode(templatefile("${path.module}/templates/start-app.sh.tftpl", {
        aws_region          = var.aws_region
        ghcr_secret_arn     = var.ghcr_registry_credentials_secret_arn
        postgres_secret_arn = aws_secretsmanager_secret.postgres_credentials.arn
        rabbitmq_secret_arn = aws_secretsmanager_secret.rabbitmq_credentials.arn
        postgres_private_ip = local.node_private_ips.postgres
        rabbitmq_private_ip = local.node_private_ips.rabbitmq
        redis_private_ip    = local.node_private_ips.redis
      }))
    }
  })

  rabbitmq_user_data = templatefile("${path.module}/templates/bootstrap.sh.tftpl", {
    compose_file_base64    = base64encode(file("${path.module}/templates/rabbitmq-compose.yaml"))
    docker_compose_version = var.docker_compose_version
    helper_files = {
      "start-rabbitmq.sh" = base64encode(templatefile("${path.module}/templates/start-rabbitmq.sh.tftpl", {
        aws_region          = var.aws_region
        rabbitmq_secret_arn = aws_secretsmanager_secret.rabbitmq_credentials.arn
      }))
    }
  })

  redis_user_data = templatefile("${path.module}/templates/bootstrap.sh.tftpl", {
    compose_file_base64    = base64encode(file("${path.module}/templates/redis-compose.yaml"))
    docker_compose_version = var.docker_compose_version
    helper_files = {
      "start-redis.sh" = base64encode(file("${path.module}/templates/start-redis.sh"))
    }
  })

  postgres_user_data = templatefile("${path.module}/templates/bootstrap.sh.tftpl", {
    compose_file_base64    = base64encode(file("${path.module}/templates/postgres-compose.yaml"))
    docker_compose_version = var.docker_compose_version
    helper_files = {
      "cleanup-smoke-event.sh" = base64encode(file("${path.module}/templates/cleanup-smoke-event.sh"))
      "start-postgres.sh" = base64encode(templatefile("${path.module}/templates/start-postgres.sh.tftpl", {
        aws_region          = var.aws_region
        postgres_secret_arn = aws_secretsmanager_secret.postgres_credentials.arn
      }))
      "wait-for-smoke-event.sh" = base64encode(file("${path.module}/templates/wait-for-smoke-event.sh"))
    }
  })

  loadgen_user_data = templatefile("${path.module}/templates/bootstrap.sh.tftpl", {
    compose_file_base64    = base64encode(file("${path.module}/templates/loadgen-compose.yaml"))
    docker_compose_version = var.docker_compose_version
    helper_files = {
      "run-baseline.sh" = base64encode(file("${path.module}/templates/run-baseline.sh"))
      "run-smoke.sh"    = base64encode(file("${path.module}/templates/run-smoke.sh"))
      "scripts/ingestion-baseline.js" = base64encode(templatefile("${path.module}/templates/ingestion-baseline.js.tftpl", {
        app_private_ip = local.node_private_ips.app
      }))
      "scripts/smoke.js" = base64encode(templatefile("${path.module}/templates/smoke.js.tftpl", {
        app_private_ip = local.node_private_ips.app
      }))
      "wait-for-ready.sh" = base64encode(templatefile("${path.module}/templates/wait-for-ready.sh.tftpl", {
        app_private_ip = local.node_private_ips.app
      }))
    }
  })

  node_configuration = {
    app = {
      instance_type        = var.app_instance_type
      instance_profile     = aws_iam_instance_profile.app_node.name
      private_ip           = local.node_private_ips.app
      root_volume_size_gib = 24
      security_group_id    = aws_security_group.app.id
      user_data            = local.app_user_data
    }
    rabbitmq = {
      instance_type        = var.rabbitmq_instance_type
      instance_profile     = aws_iam_instance_profile.rabbitmq_node.name
      private_ip           = local.node_private_ips.rabbitmq
      root_volume_size_gib = 24
      security_group_id    = aws_security_group.rabbitmq.id
      user_data            = local.rabbitmq_user_data
    }
    redis = {
      instance_type        = var.redis_instance_type
      instance_profile     = aws_iam_instance_profile.standard_node.name
      private_ip           = local.node_private_ips.redis
      root_volume_size_gib = 12
      security_group_id    = aws_security_group.redis.id
      user_data            = local.redis_user_data
    }
    postgres = {
      instance_type        = var.postgres_instance_type
      instance_profile     = aws_iam_instance_profile.postgres_node.name
      private_ip           = local.node_private_ips.postgres
      root_volume_size_gib = 48
      security_group_id    = aws_security_group.postgres.id
      user_data            = local.postgres_user_data
    }
    loadgen = {
      instance_type        = var.loadgen_instance_type
      instance_profile     = aws_iam_instance_profile.standard_node.name
      private_ip           = local.node_private_ips.loadgen
      root_volume_size_gib = 16
      security_group_id    = aws_security_group.loadgen.id
      user_data            = local.loadgen_user_data
    }
  }
}

resource "aws_instance" "nodes" {
  for_each = local.node_configuration

  ami                         = local.ami_id
  instance_type               = each.value.instance_type
  subnet_id                   = aws_subnet.nodes.id
  private_ip                  = each.value.private_ip
  associate_public_ip_address = true
  vpc_security_group_ids      = [each.value.security_group_id]
  iam_instance_profile        = each.value.instance_profile
  user_data                   = each.value.user_data
  user_data_replace_on_change = true
  monitoring                  = false

  metadata_options {
    http_endpoint = "enabled"
    http_tokens   = "required"
  }

  root_block_device {
    delete_on_termination = true
    encrypted             = true
    volume_size           = each.value.root_volume_size_gib
    volume_type           = "gp3"
  }

  tags = {
    Name = "${var.name_prefix}-${each.key}"
    Role = each.key
  }

  depends_on = [
    aws_iam_role_policy.app_node_secrets,
    aws_iam_role_policy.rabbitmq_node_secrets,
    aws_iam_role_policy.postgres_node_secrets,
    aws_iam_role_policy_attachment.app_node_ssm,
    aws_iam_role_policy_attachment.postgres_node_ssm,
    aws_iam_role_policy_attachment.rabbitmq_node_ssm,
    aws_iam_role_policy_attachment.standard_node_ssm,
  ]
}
