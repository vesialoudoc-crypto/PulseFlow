resource "aws_cloudwatch_log_group" "api" {
  name              = "/ecs/${var.name_prefix}"
  retention_in_days = 14
}

data "aws_iam_policy_document" "ecs_tasks_assume_role" {
  statement {
    effect = "Allow"

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }

    actions = ["sts:AssumeRole"]
  }
}

resource "aws_iam_role" "task_execution" {
  name               = "${var.name_prefix}-ecs-execution"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume_role.json
}

resource "aws_iam_role_policy_attachment" "task_execution_base" {
  role       = aws_iam_role.task_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

data "aws_iam_policy_document" "task_execution_secrets" {
  statement {
    sid    = "ReadOnlyTaskSecrets"
    effect = "Allow"
    actions = [
      "secretsmanager:GetSecretValue",
    ]
    resources = [
      var.ghcr_registry_credentials_secret_arn,
      aws_secretsmanager_secret.database_connection.arn,
      aws_secretsmanager_secret.rabbitmq_connection.arn,
    ]
  }
}

resource "aws_iam_role_policy" "task_execution_secrets" {
  name   = "${var.name_prefix}-read-task-secrets"
  role   = aws_iam_role.task_execution.id
  policy = data.aws_iam_policy_document.task_execution_secrets.json
}

resource "aws_iam_role" "application" {
  name               = "${var.name_prefix}-ecs-application"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume_role.json
}

resource "aws_iam_role" "database_verifier_execution" {
  name               = "${var.name_prefix}-database-verifier-execution"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume_role.json
}

resource "aws_iam_role_policy_attachment" "database_verifier_execution_base" {
  role       = aws_iam_role.database_verifier_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

data "aws_iam_policy_document" "database_verifier_execution_secrets" {
  statement {
    sid    = "ReadOnlyRdsMasterCredential"
    effect = "Allow"
    actions = [
      "secretsmanager:GetSecretValue",
    ]
    resources = [
      aws_db_instance.postgresql.master_user_secret[0].secret_arn,
    ]
  }
}

resource "aws_iam_role_policy" "database_verifier_execution_secrets" {
  name   = "${var.name_prefix}-read-rds-master-secret"
  role   = aws_iam_role.database_verifier_execution.id
  policy = data.aws_iam_policy_document.database_verifier_execution_secrets.json
}

locals {
  api_environment = {
    ASPNETCORE_URLS                      = "http://+:8080"
    ConnectionStrings__Redis             = "${aws_elasticache_serverless_cache.valkey.endpoint[0].address}:${aws_elasticache_serverless_cache.valkey.endpoint[0].port},ssl=true,abortConnect=false"
    HealthChecks__Timeout                = "00:00:02"
    Ingestion__ChunkCapacity             = tostring(var.ingestion_chunk_capacity)
    Ingestion__MaxBatchBytes             = tostring(var.ingestion_max_batch_bytes)
    IngestionRateLimit__RequestLimit     = tostring(var.ingestion_rate_limit_request_limit)
    IngestionRateLimit__WindowDuration   = var.ingestion_rate_limit_window_duration
    PostgreSql__CommandTimeout           = "00:00:10"
    PostgreSql__ConnectionTimeout        = "00:00:10"
    RabbitMq__CleanupTimeout             = "00:00:01"
    RabbitMq__ConnectionTimeout          = "00:00:10"
    RabbitMq__ConsumerCount              = tostring(var.rabbitmq_consumer_count)
    RabbitMq__ContinuationTimeout        = "00:00:10"
    RabbitMq__HandshakeTimeout           = "00:00:10"
    RabbitMq__PublisherChannelCount      = tostring(var.rabbitmq_publisher_channel_count)
    RabbitMq__PublisherChannelTimeout    = "00:00:05"
    RabbitMq__PublishConfirmationTimeout = "00:00:05"
    RabbitMq__QueueName                  = var.rabbitmq_queue_name
    RabbitMq__TopologyDeclarationTimeout = "00:00:10"
    Redis__AsyncTimeout                  = "00:00:01"
    Redis__ConnectTimeout                = "00:00:10"
    Startup__InitializationTimeout       = "00:00:30"
  }

  api_runtime_secrets = [
    {
      name      = "ConnectionStrings__PulseFlow"
      valueFrom = "${aws_secretsmanager_secret.database_connection.arn}:connectionString::"
    },
    {
      name      = "ConnectionStrings__RabbitMq"
      valueFrom = "${aws_secretsmanager_secret.rabbitmq_connection.arn}:connectionString::"
    },
  ]

  api_container_definition = {
    name      = "pulseflow-api"
    image     = var.pulseflow_image
    essential = true
    environment = [
      for name, value in local.api_environment : {
        name  = name
        value = value
      }
    ]
    secrets = local.api_runtime_secrets
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.api.name
        awslogs-region        = data.aws_region.current.region
        awslogs-stream-prefix = "api"
      }
    }
    portMappings = [
      {
        containerPort = 8080
        hostPort      = 8080
        protocol      = "tcp"
      },
    ]
    repositoryCredentials = {
      credentialsParameter = var.ghcr_registry_credentials_secret_arn
    }
  }
}

resource "aws_ecs_cluster" "staging" {
  name = "${var.name_prefix}-cluster"
}

resource "aws_ecs_task_definition" "api" {
  family                   = "${var.name_prefix}-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.api_task_cpu
  memory                   = var.api_task_memory
  execution_role_arn       = aws_iam_role.task_execution.arn
  task_role_arn            = aws_iam_role.application.arn
  container_definitions    = jsonencode([local.api_container_definition])

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }
}

resource "aws_ecs_task_definition" "migration" {
  family                   = "${var.name_prefix}-migration"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.api_task_cpu
  memory                   = var.api_task_memory
  execution_role_arn       = aws_iam_role.task_execution.arn
  task_role_arn            = aws_iam_role.application.arn
  container_definitions = jsonencode([
    merge(
      local.api_container_definition,
      {
        name       = "pulseflow-migration"
        entryPoint = ["/bin/sh", "-c"]
        command    = ["exec /app/migrations/pulseflow-migrations --connection \"$ConnectionStrings__PulseFlow\""]
      }
    ),
  ])

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }
}

resource "aws_ecs_task_definition" "database_verifier" {
  family                   = "${var.name_prefix}-database-verifier"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 256
  memory                   = 512
  execution_role_arn       = aws_iam_role.database_verifier_execution.arn
  task_role_arn            = aws_iam_role.application.arn
  container_definitions = jsonencode([
    {
      name      = "database-verifier"
      image     = "postgres:17-alpine"
      essential = true
      environment = [
        {
          name  = "PGDATABASE"
          value = aws_db_instance.postgresql.db_name
        },
        {
          name  = "PGHOST"
          value = aws_db_instance.postgresql.address
        },
        {
          name  = "PGPORT"
          value = tostring(aws_db_instance.postgresql.port)
        },
        {
          name  = "PGSSLMODE"
          value = "require"
        },
        {
          name  = "PGUSER"
          value = aws_db_instance.postgresql.username
        },
      ]
      secrets = [
        {
          name      = "PGPASSWORD"
          valueFrom = "${aws_db_instance.postgresql.master_user_secret[0].secret_arn}:password::"
        },
      ]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.api.name
          awslogs-region        = data.aws_region.current.region
          awslogs-stream-prefix = "database-verifier"
        }
      }
    },
  ])

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  depends_on = [aws_iam_role_policy.database_verifier_execution_secrets]
}

resource "aws_ecs_service" "api" {
  name            = "${var.name_prefix}-api"
  cluster         = aws_ecs_cluster.staging.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = 0
  launch_type     = "FARGATE"

  network_configuration {
    assign_public_ip = true
    security_groups  = [aws_security_group.api.id]
    subnets          = values(aws_subnet.public)[*].id
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "pulseflow-api"
    container_port   = 8080
  }

  health_check_grace_period_seconds = 60

  lifecycle {
    # The deployment script updates these only after the same-image migration task exits 0.
    ignore_changes = [desired_count, task_definition]
  }

  depends_on = [
    aws_iam_role_policy.task_execution_secrets,
    aws_lb_listener.http,
  ]
}
