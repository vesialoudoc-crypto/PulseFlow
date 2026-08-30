data "aws_availability_zones" "available" {
  state = "available"
}

data "aws_caller_identity" "current" {}

data "aws_region" "current" {}

data "aws_rds_engine_version" "postgres" {
  engine  = "postgres"
  version = var.rds_postgres_major_version
  latest  = true
}

locals {
  availability_zones = slice(data.aws_availability_zones.available.names, 0, 2)

  public_subnet_cidrs = {
    for index, availability_zone in local.availability_zones : availability_zone => cidrsubnet(var.vpc_cidr, 8, index)
  }

  data_subnet_cidrs = {
    for index, availability_zone in local.availability_zones : availability_zone => cidrsubnet(var.vpc_cidr, 8, index + 10)
  }
}
