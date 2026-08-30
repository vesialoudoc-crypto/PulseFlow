terraform {
  required_version = ">= 1.6.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

provider "aws" {
  region  = var.aws_region
  profile = var.aws_profile

  default_tags {
    tags = {
      Environment = "ec2"
      ManagedBy   = "Terraform"
      Project     = "PulseFlow"
    }
  }
}

data "aws_availability_zones" "available" {
  state = "available"
}

locals {
  availability_zone = data.aws_availability_zones.available.names[0]

  nodes = {
    app = {
      instance_type     = var.app_instance_type
      security_group_id = aws_security_group.app.id
    }
    rabbitmq = {
      instance_type     = var.rabbitmq_instance_type
      security_group_id = aws_security_group.rabbitmq.id
    }
    redis = {
      instance_type     = var.redis_instance_type
      security_group_id = aws_security_group.redis.id
    }
    postgres = {
      instance_type     = var.postgres_instance_type
      security_group_id = aws_security_group.postgres.id
    }
  }
}
