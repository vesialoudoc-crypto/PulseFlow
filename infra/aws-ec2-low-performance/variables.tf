variable "aws_region" {
  description = "AWS region for this temporary EC2 environment."
  type        = string
  default     = "eu-central-1"
}

variable "aws_profile" {
  description = "Optional locally configured AWS profile. Leave null to use the default credential chain."
  type        = string
  default     = null
  nullable    = true
}

variable "allowed_http_source_cidr" {
  description = "CIDR permitted to send future external test HTTP traffic to pulseflow-app. Use the tester's narrowly scoped public CIDR; 0.0.0.0/0 is intentionally rejected."
  type        = string

  validation {
    condition     = can(cidrnetmask(var.allowed_http_source_cidr)) && var.allowed_http_source_cidr != "0.0.0.0/0"
    error_message = "allowed_http_source_cidr must be a valid, non-global IPv4 CIDR."
  }
}

variable "vpc_cidr" {
  description = "IPv4 address range for the dedicated EC2 VPC."
  type        = string
  default     = "10.43.0.0/16"
}

variable "public_subnet_cidr" {
  description = "IPv4 address range for the single public EC2 subnet."
  type        = string
  default     = "10.43.10.0/24"
}

variable "ami_id" {
  description = "Required Amazon Linux 2023 x86_64 AMI ID selected explicitly for this environment."
  type        = string

  validation {
    condition     = can(regex("^ami-[0-9a-f]+$", var.ami_id))
    error_message = "ami_id must be a valid AMI identifier, such as ami-0123456789abcdef0."
  }
}

variable "app_instance_type" {
  description = "EC2 type for the future application host."
  type        = string
  default     = "t3.small"
}

variable "rabbitmq_instance_type" {
  description = "EC2 type for the future RabbitMQ host."
  type        = string
  default     = "t3.small"
}

variable "redis_instance_type" {
  description = "EC2 type for the future Redis host."
  type        = string
  default     = "t3.small"
}

variable "postgres_instance_type" {
  description = "EC2 type for the future PostgreSQL host."
  type        = string
  default     = "t3.small"
}

variable "root_volume_size_gib" {
  description = "Small encrypted gp3 root-volume size for each temporary host."
  type        = number
  default     = 8

  validation {
    condition     = var.root_volume_size_gib >= 8
    error_message = "root_volume_size_gib must be at least 8 GiB for Amazon Linux 2023."
  }
}
