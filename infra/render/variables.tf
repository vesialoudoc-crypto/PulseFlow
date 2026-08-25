variable "render_region" {
  description = "Render region for every staging resource."
  type        = string
  default     = "frankfurt"

  validation {
    condition     = contains(["frankfurt", "ohio", "oregon", "singapore", "virginia"], var.render_region)
    error_message = "render_region must be a region supported by the Render provider."
  }
}

variable "pulseflow_image" {
  description = "Immutable GHCR image reference, for example ghcr.io/owner/pulseflow-api:sha-<40 lowercase hexadecimal characters>."
  type        = string

  validation {
    condition     = can(regex("^ghcr\\.io/[a-z0-9][a-z0-9._-]*/pulseflow-api:sha-[0-9a-f]{40}$", var.pulseflow_image))
    error_message = "pulseflow_image must be an immutable GHCR pulseflow-api image with a full SHA tag."
  }
}

variable "ghcr_registry_credential_id" {
  description = "Existing Render registry credential ID for private GHCR access. Leave null only when the selected GHCR image is public."
  type        = string
  default     = null
  nullable    = true
}

variable "api_service_name" {
  description = "Render name for the public PulseFlow API service."
  type        = string
  default     = "pulseflow-api-staging"
}

variable "api_plan" {
  description = "Paid Render web-service plan. Pre-deploy commands require a paid service."
  type        = string
  default     = "starter"
}

variable "api_instance_count" {
  description = "Number of PulseFlow.Api instances. Each instance also hosts RabbitMQ consumers in the current application design."
  type        = number
  default     = 1

  validation {
    condition     = var.api_instance_count >= 1
    error_message = "api_instance_count must be at least one."
  }
}

variable "postgres_name" {
  description = "Render name for the staging PostgreSQL instance."
  type        = string
  default     = "pulseflow-postgres-staging"
}

variable "postgres_plan" {
  description = "Render PostgreSQL plan for disposable staging."
  type        = string
  default     = "free"
}

variable "postgres_version" {
  description = "PostgreSQL major version, kept compatible with the local PostgreSQL 18 topology."
  type        = string
  default     = "18"
}

variable "key_value_name" {
  description = "Render name for the Redis-compatible staging Key Value instance."
  type        = string
  default     = "pulseflow-rate-limit-staging"
}

variable "key_value_plan" {
  description = "Render Key Value plan for disposable rate-limit state."
  type        = string
  default     = "free"
}

variable "rabbitmq_service_name" {
  description = "Render name for the private RabbitMQ service. Its Render slug is the private AMQP hostname."
  type        = string
  default     = "pulseflow-rabbitmq-staging"
}

variable "rabbitmq_plan" {
  description = "Paid Render private-service plan required for RabbitMQ persistent storage."
  type        = string
  default     = "starter"
}

variable "rabbitmq_disk_size_gb" {
  description = "Persistent RabbitMQ disk size in GiB. Render's RabbitMQ deployment guide uses 10 GB."
  type        = number
  default     = 10

  validation {
    condition     = var.rabbitmq_disk_size_gb >= 10
    error_message = "rabbitmq_disk_size_gb must be at least 10 GiB for the documented RabbitMQ staging layout."
  }
}

variable "rabbitmq_username" {
  description = "RabbitMQ application username."
  type        = string
  default     = "pulseflow"

  validation {
    condition     = can(regex("^[^:/@]+$", var.rabbitmq_username))
    error_message = "rabbitmq_username must not contain URI delimiter characters."
  }
}

variable "rabbitmq_password" {
  description = "RabbitMQ application password. Supply it through a sensitive external variable."
  type        = string
  sensitive   = true
}

variable "rabbitmq_erlang_cookie" {
  description = "RabbitMQ Erlang distribution cookie. Supply it through a sensitive external variable."
  type        = string
  sensitive   = true
}

variable "trusted_operator_cidrs" {
  description = "Optional trusted operator CIDRs for temporary PostgreSQL and Key Value external access. Empty keeps them private-only."
  type        = set(string)
  default     = []
}

variable "rabbitmq_queue_name" {
  description = "Existing PulseFlow RabbitMQ queue name."
  type        = string
  default     = "pulseflow.ingestion-batches"
}

variable "rabbitmq_consumer_count" {
  description = "Existing RabbitMQ consumer count per API process."
  type        = number
  default     = 1
}

variable "rabbitmq_publisher_channel_count" {
  description = "Existing RabbitMQ publisher channel count per API process."
  type        = number
  default     = 4
}

variable "ingestion_chunk_capacity" {
  description = "Existing PulseFlow ingestion chunk capacity."
  type        = number
  default     = 100
}

variable "ingestion_rate_limit_request_limit" {
  description = "Staging global ingestion rate-limit request count per window."
  type        = number
  default     = 10000000
}

variable "ingestion_rate_limit_window_duration" {
  description = "Staging global ingestion rate-limit window as a .NET TimeSpan string."
  type        = string
  default     = "00:01:00"
}
