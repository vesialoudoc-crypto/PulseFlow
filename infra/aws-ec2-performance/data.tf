data "aws_availability_zones" "available" {
  state = "available"
}

data "aws_caller_identity" "current" {}

data "aws_region" "current" {}

data "aws_ssm_parameter" "amazon_linux_2023" {
  count = var.ami_id == null ? 1 : 0
  name  = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64"
}

locals {
  availability_zone = data.aws_availability_zones.available.names[0]
  ami_id            = var.ami_id != null ? var.ami_id : data.aws_ssm_parameter.amazon_linux_2023[0].value

  node_private_ips = {
    app      = "10.43.10.10"
    rabbitmq = "10.43.10.20"
    redis    = "10.43.10.30"
    postgres = "10.43.10.40"
    loadgen  = "10.43.10.50"
  }
}
