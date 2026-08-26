variable "aws_region" {
  description = "AWS region for the first staging deployment. eu-central-1 (Frankfurt) is selected for the first reviewed plan."
  type        = string
  default     = "eu-central-1"

  validation {
    condition     = can(regex("^[a-z]{2}-[a-z]+-[0-9]+$", var.aws_region))
    error_message = "aws_region must be an AWS region identifier, such as eu-central-1."
  }
}

variable "aws_profile" {
  description = "Optional locally configured AWS CLI/shared-credentials profile. Leave null to use the default AWS credential chain."
  type        = string
  default     = null
  nullable    = true
}

variable "name_prefix" {
  description = "Lowercase prefix used in AWS resource names."
  type        = string
  default     = "pulseflow-staging"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{2,31}$", var.name_prefix))
    error_message = "name_prefix must contain 3-32 lowercase letters, digits, or hyphens and start with a letter."
  }
}

variable "pulseflow_image" {
  description = "Required immutable GHCR PulseFlow.Api image reference, for example ghcr.io/owner/pulseflow-api:sha-<40 lowercase hexadecimal characters>."
  type        = string

  validation {
    condition     = can(regex("^ghcr\\.io/[a-z0-9][a-z0-9._-]*/pulseflow-api:sha-[0-9a-f]{40}$", var.pulseflow_image))
    error_message = "pulseflow_image must be an immutable GHCR pulseflow-api image with a full SHA tag."
  }
}

variable "ghcr_registry_credentials_secret_arn" {
  description = "Existing AWS Secrets Manager ARN containing the private GHCR credential JSON. Terraform references this secret but never receives its token value."
  type        = string

  validation {
    condition     = can(regex("^arn:[^:]+:secretsmanager:[^:]+:[0-9]{12}:secret:.+$", var.ghcr_registry_credentials_secret_arn))
    error_message = "ghcr_registry_credentials_secret_arn must be an AWS Secrets Manager secret ARN."
  }
}

variable "vpc_cidr" {
  description = "IPv4 address range for the disposable staging VPC."
  type        = string
  default     = "10.42.0.0/16"
}

variable "api_task_cpu" {
  description = "Fargate CPU units for the API and migration tasks. 256 equals 0.25 vCPU."
  type        = number
  default     = 256

  validation {
    condition     = contains([256, 512, 1024, 2048, 4096], var.api_task_cpu)
    error_message = "api_task_cpu must be a Fargate CPU value supported by ECS."
  }
}

variable "api_task_memory" {
  description = "Fargate memory in MiB for the API and migration tasks. 512 MiB is valid with 256 CPU units."
  type        = number
  default     = 512

  validation {
    condition     = var.api_task_memory >= 512
    error_message = "api_task_memory must be at least 512 MiB."
  }
}

variable "rds_postgres_major_version" {
  description = "PostgreSQL major version. Terraform resolves the latest available patch for this major in the selected region during plan."
  type        = string
  default     = "17"

  validation {
    condition     = can(regex("^[0-9]+$", var.rds_postgres_major_version))
    error_message = "rds_postgres_major_version must be a PostgreSQL major version number."
  }
}

variable "rds_instance_class" {
  description = "Single-AZ RDS instance class for disposable staging."
  type        = string
  default     = "db.t4g.micro"
}

variable "rds_allocated_storage_gib" {
  description = "Allocated gp3 RDS storage in GiB."
  type        = number
  default     = 20

  validation {
    condition     = var.rds_allocated_storage_gib >= 20
    error_message = "rds_allocated_storage_gib must meet the RDS minimum of 20 GiB."
  }
}

variable "rds_backup_retention_days" {
  description = "Automated RDS backup retention for staging."
  type        = number
  default     = 1

  validation {
    condition     = var.rds_backup_retention_days >= 1 && var.rds_backup_retention_days <= 35
    error_message = "rds_backup_retention_days must be from 1 through 35."
  }
}

variable "valkey_maximum_data_storage_gb" {
  description = "ElastiCache Serverless Valkey maximum data-storage guardrail in GB."
  type        = number
  default     = 1

  validation {
    condition     = var.valkey_maximum_data_storage_gb >= 1
    error_message = "valkey_maximum_data_storage_gb must be at least 1 GB."
  }
}

variable "valkey_maximum_ecpu_per_second" {
  description = "ElastiCache Serverless Valkey maximum ECPU/second guardrail. No paid minimum pre-scaling is configured."
  type        = number
  default     = 1000

  validation {
    condition     = var.valkey_maximum_ecpu_per_second >= 1000
    error_message = "valkey_maximum_ecpu_per_second must be at least 1000."
  }
}

variable "rabbitmq_username" {
  description = "Initial Amazon MQ RabbitMQ user. Amazon MQ creates this one administrative user during provisioning."
  type        = string
  default     = "pulseflow"

  validation {
    condition     = can(regex("^[A-Za-z0-9._-]{1,64}$", var.rabbitmq_username))
    error_message = "rabbitmq_username may contain letters, digits, periods, underscores, and hyphens only."
  }
}

variable "rabbitmq_queue_name" {
  description = "PulseFlow RabbitMQ queue name."
  type        = string
  default     = "pulseflow.ingestion-batches"
}

variable "rabbitmq_consumer_count" {
  description = "RabbitMQ consumers per PulseFlow.Api process. The first staging service has one API process."
  type        = number
  default     = 1

  validation {
    condition     = var.rabbitmq_consumer_count >= 1
    error_message = "rabbitmq_consumer_count must be at least one."
  }
}

variable "rabbitmq_publisher_channel_count" {
  description = "RabbitMQ publisher-channel pool size per PulseFlow.Api process."
  type        = number
  default     = 4

  validation {
    condition     = var.rabbitmq_publisher_channel_count >= 1
    error_message = "rabbitmq_publisher_channel_count must be at least one."
  }
}

variable "ingestion_chunk_capacity" {
  description = "PulseFlow ingestion chunk capacity."
  type        = number
  default     = 100

  validation {
    condition     = var.ingestion_chunk_capacity >= 1
    error_message = "ingestion_chunk_capacity must be at least one."
  }
}

variable "ingestion_max_batch_bytes" {
  description = "PulseFlow raw ingestion body limit in bytes."
  type        = number
  default     = 10485760

  validation {
    condition = (
      var.ingestion_max_batch_bytes >= 1
      && var.ingestion_max_batch_bytes <= 104857600
      && floor(var.ingestion_max_batch_bytes) == var.ingestion_max_batch_bytes
    )
    error_message = "ingestion_max_batch_bytes must be a whole number from 1 through 104857600."
  }
}

variable "ingestion_rate_limit_request_limit" {
  description = "Global distributed ingestion request limit per configured rate-limit window."
  type        = number
  default     = 10000000

  validation {
    condition     = var.ingestion_rate_limit_request_limit >= 1
    error_message = "ingestion_rate_limit_request_limit must be at least one."
  }
}

variable "ingestion_rate_limit_window_duration" {
  description = "Global distributed ingestion rate-limit window as a .NET TimeSpan string."
  type        = string
  default     = "00:01:00"
}
