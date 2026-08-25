locals {
  pulseflow_image_parts = regex("^(.+):(sha-[0-9a-f]{40})$", var.pulseflow_image)

  operator_ip_allow_list = length(var.trusted_operator_cidrs) == 0 ? null : [
    for cidr in var.trusted_operator_cidrs : {
      cidr_block  = cidr
      description = "Trusted PulseFlow staging operator"
    }
  ]
}

resource "render_postgres" "pulseflow" {
  name    = var.postgres_name
  plan    = var.postgres_plan
  region  = var.render_region
  version = var.postgres_version

  database_name = "pulseflow"
  database_user = "pulseflow"
  ip_allow_list = local.operator_ip_allow_list
}

resource "render_keyvalue" "rate_limit" {
  name              = var.key_value_name
  plan              = var.key_value_plan
  region            = var.render_region
  max_memory_policy = "allkeys_lru"
  persistence_mode  = "off"
  ip_allow_list     = local.operator_ip_allow_list
}

resource "render_private_service" "rabbitmq" {
  name          = var.rabbitmq_service_name
  plan          = var.rabbitmq_plan
  region        = var.render_region
  num_instances = 1

  runtime_source = {
    image = {
      image_url = "docker.io/library/rabbitmq"
      tag       = "4.1.0-management"
    }
  }

  disk = {
    name       = "rabbitmq-data"
    mount_path = "/var/lib/rabbitmq"
    size_gb    = var.rabbitmq_disk_size_gb
  }

  env_vars = {
    RABBITMQ_DEFAULT_USER = {
      value = var.rabbitmq_username
    }
    RABBITMQ_DEFAULT_PASS = {
      value = var.rabbitmq_password
    }
    RABBITMQ_ERLANG_COOKIE = {
      value = var.rabbitmq_erlang_cookie
    }
  }
}

resource "render_web_service" "api" {
  name              = var.api_service_name
  plan              = var.api_plan
  region            = var.render_region
  num_instances     = var.api_instance_count
  health_check_path = "/health/ready"

  runtime_source = {
    image = {
      image_url              = local.pulseflow_image_parts[0]
      tag                    = local.pulseflow_image_parts[1]
      registry_credential_id = var.ghcr_registry_credential_id
    }
  }

  pre_deploy_command = "/app/migrations/pulseflow-migrations --connection \"$ConnectionStrings__PulseFlow\""

  env_vars = {
    ASPNETCORE_URLS = {
      value = "http://+:8080"
    }
    ConnectionStrings__PulseFlow = {
      value = render_postgres.pulseflow.connection_info.internal_connection_string
    }
    ConnectionStrings__Redis = {
      value = render_keyvalue.rate_limit.connection_info.internal_connection_string
    }
    ConnectionStrings__RabbitMq = {
      value = format(
        "amqp://%s:%s@%s:5672/",
        urlencode(var.rabbitmq_username),
        urlencode(var.rabbitmq_password),
        render_private_service.rabbitmq.slug,
      )
    }
    RabbitMq__QueueName = {
      value = var.rabbitmq_queue_name
    }
    RabbitMq__ConsumerCount = {
      value = tostring(var.rabbitmq_consumer_count)
    }
    RabbitMq__PublisherChannelCount = {
      value = tostring(var.rabbitmq_publisher_channel_count)
    }
    Ingestion__ChunkCapacity = {
      value = tostring(var.ingestion_chunk_capacity)
    }
    Ingestion__MaxBatchBytes = {
      value = tostring(var.ingestion_max_batch_bytes)
    }
    IngestionRateLimit__RequestLimit = {
      value = tostring(var.ingestion_rate_limit_request_limit)
    }
    IngestionRateLimit__WindowDuration = {
      value = var.ingestion_rate_limit_window_duration
    }
  }
}
