variable "aws_region" {
  description = "AWS region for the EC2 performance environment. Frankfurt is the initial reviewed region."
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
  description = "Lowercase prefix used in EC2 performance environment resource names."
  type        = string
  default     = "pulseflow-ec2-performance"

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
  sensitive   = true

  validation {
    condition     = can(regex("^arn:[^:]+:secretsmanager:[^:]+:[0-9]{12}:secret:.+$", var.ghcr_registry_credentials_secret_arn))
    error_message = "ghcr_registry_credentials_secret_arn must be an AWS Secrets Manager secret ARN."
  }
}

variable "vpc_cidr" {
  description = "IPv4 address range for the dedicated EC2 performance VPC."
  type        = string
  default     = "10.43.0.0/16"

  validation {
    condition     = var.vpc_cidr == "10.43.0.0/16"
    error_message = "The fixed private node addresses are allocated from 10.43.0.0/16. Change the address plan and documentation before selecting another VPC CIDR."
  }
}

variable "ami_id" {
  description = "Optional Amazon Linux 2023 x86_64 AMI override. Leave null to resolve the current AWS public AL2023 AMI parameter at plan time."
  type        = string
  default     = null
  nullable    = true

  validation {
    condition     = var.ami_id == null || can(regex("^ami-[0-9a-f]+$", var.ami_id))
    error_message = "ami_id must be a valid AMI identifier, such as ami-0123456789abcdef0."
  }
}

variable "docker_compose_version" {
  description = "Pinned Docker Compose plugin release installed during first boot."
  type        = string
  default     = "v5.5.0"

  validation {
    condition     = can(regex("^v[0-9]+\\.[0-9]+\\.[0-9]+$", var.docker_compose_version))
    error_message = "docker_compose_version must be a semantic Docker Compose release tag, for example v5.5.0."
  }
}

variable "app_instance_type" {
  description = "EC2 type for HAProxy and two API containers. The cost-sensitive default provides 2 vCPUs and 4 GiB on x86_64."
  type        = string
  default     = "c7i-flex.large"
}

variable "rabbitmq_instance_type" {
  description = "EC2 type for the isolated RabbitMQ node in a short cloud proof."
  type        = string
  default     = "t3.small"
}

variable "postgres_instance_type" {
  description = "EC2 type for the isolated PostgreSQL node in a short cloud proof."
  type        = string
  default     = "t3.small"
}

variable "redis_instance_type" {
  description = "EC2 type for the low-footprint Redis rate-limit node."
  type        = string
  default     = "t3.small"
}

variable "loadgen_instance_type" {
  description = "EC2 type for the isolated k6 node in a short cloud proof."
  type        = string
  default     = "t3.small"
}

variable "enable_business_hours_schedule" {
  description = "Whether EventBridge Scheduler automatically starts and stops all nodes on weekdays in the configured IANA time zone."
  type        = bool
  default     = true
}

variable "schedule_timezone" {
  description = "IANA time zone in which the EventBridge Scheduler cron expressions are evaluated."
  type        = string
  default     = "Europe/Warsaw"

  validation {
    condition     = var.schedule_timezone == "Europe/Warsaw"
    error_message = "The accepted initial schedule time zone is Europe/Warsaw. Change it only with a documented operating-hours decision."
  }
}
