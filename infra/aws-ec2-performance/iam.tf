data "aws_iam_policy_document" "ec2_assume_role" {
  statement {
    effect = "Allow"

    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }

    actions = ["sts:AssumeRole"]
  }
}

resource "aws_iam_role" "standard_node" {
  name               = "${var.name_prefix}-standard-node"
  assume_role_policy = data.aws_iam_policy_document.ec2_assume_role.json
}

resource "aws_iam_role" "app_node" {
  name               = "${var.name_prefix}-app-node"
  assume_role_policy = data.aws_iam_policy_document.ec2_assume_role.json
}

resource "aws_iam_role" "rabbitmq_node" {
  name               = "${var.name_prefix}-rabbitmq-node"
  assume_role_policy = data.aws_iam_policy_document.ec2_assume_role.json
}

resource "aws_iam_role" "postgres_node" {
  name               = "${var.name_prefix}-postgres-node"
  assume_role_policy = data.aws_iam_policy_document.ec2_assume_role.json
}

resource "aws_iam_role_policy_attachment" "standard_node_ssm" {
  role       = aws_iam_role.standard_node.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_role_policy_attachment" "app_node_ssm" {
  role       = aws_iam_role.app_node.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_role_policy_attachment" "rabbitmq_node_ssm" {
  role       = aws_iam_role.rabbitmq_node.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_role_policy_attachment" "postgres_node_ssm" {
  role       = aws_iam_role.postgres_node.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

data "aws_iam_policy_document" "app_node_secrets" {
  statement {
    sid     = "ReadRequiredRuntimeSecrets"
    effect  = "Allow"
    actions = ["secretsmanager:GetSecretValue"]
    resources = [
      var.ghcr_registry_credentials_secret_arn,
      aws_secretsmanager_secret.postgres_credentials.arn,
      aws_secretsmanager_secret.rabbitmq_credentials.arn,
    ]
  }
}

resource "aws_iam_role_policy" "app_node_secrets" {
  name   = "${var.name_prefix}-read-runtime-secrets"
  role   = aws_iam_role.app_node.id
  policy = data.aws_iam_policy_document.app_node_secrets.json
}

data "aws_iam_policy_document" "rabbitmq_node_secrets" {
  statement {
    sid       = "ReadRabbitMqCredential"
    effect    = "Allow"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.rabbitmq_credentials.arn]
  }
}

resource "aws_iam_role_policy" "rabbitmq_node_secrets" {
  name   = "${var.name_prefix}-read-rabbitmq-secret"
  role   = aws_iam_role.rabbitmq_node.id
  policy = data.aws_iam_policy_document.rabbitmq_node_secrets.json
}

data "aws_iam_policy_document" "postgres_node_secrets" {
  statement {
    sid       = "ReadPostgresCredential"
    effect    = "Allow"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.postgres_credentials.arn]
  }
}

resource "aws_iam_role_policy" "postgres_node_secrets" {
  name   = "${var.name_prefix}-read-postgres-secret"
  role   = aws_iam_role.postgres_node.id
  policy = data.aws_iam_policy_document.postgres_node_secrets.json
}

resource "aws_iam_instance_profile" "standard_node" {
  name = "${var.name_prefix}-standard-node"
  role = aws_iam_role.standard_node.name
}

resource "aws_iam_instance_profile" "app_node" {
  name = "${var.name_prefix}-app-node"
  role = aws_iam_role.app_node.name
}

resource "aws_iam_instance_profile" "rabbitmq_node" {
  name = "${var.name_prefix}-rabbitmq-node"
  role = aws_iam_role.rabbitmq_node.name
}

resource "aws_iam_instance_profile" "postgres_node" {
  name = "${var.name_prefix}-postgres-node"
  role = aws_iam_role.postgres_node.name
}
