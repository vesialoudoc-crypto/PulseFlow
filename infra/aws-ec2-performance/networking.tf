resource "aws_vpc" "performance" {
  cidr_block           = var.vpc_cidr
  enable_dns_hostnames = true
  enable_dns_support   = true

  tags = {
    Name = "${var.name_prefix}-vpc"
  }
}

resource "aws_internet_gateway" "performance" {
  vpc_id = aws_vpc.performance.id

  tags = {
    Name = "${var.name_prefix}-igw"
  }
}

# These nodes require outbound HTTPS for SSM, Docker Hub, GHCR, and package/bootstrap
# downloads. Public IPv4 addresses are assigned only while nodes run; no Elastic IP or
# public inbound rule is created.
resource "aws_subnet" "nodes" {
  vpc_id                  = aws_vpc.performance.id
  availability_zone       = local.availability_zone
  cidr_block              = "10.43.10.0/24"
  map_public_ip_on_launch = false

  tags = {
    Name = "${var.name_prefix}-nodes-${local.availability_zone}"
    Tier = "controlled-egress"
  }
}

resource "aws_route_table" "nodes" {
  vpc_id = aws_vpc.performance.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.performance.id
  }

  tags = {
    Name = "${var.name_prefix}-nodes"
  }
}

resource "aws_route_table_association" "nodes" {
  subnet_id      = aws_subnet.nodes.id
  route_table_id = aws_route_table.nodes.id
}
