data "aws_iam_policy_document" "scheduler_assume_role" {
  statement {
    effect = "Allow"

    principals {
      type        = "Service"
      identifiers = ["scheduler.amazonaws.com"]
    }

    actions = ["sts:AssumeRole"]
  }
}

resource "aws_iam_role" "scheduler" {
  name               = "${var.name_prefix}-scheduler"
  assume_role_policy = data.aws_iam_policy_document.scheduler_assume_role.json
}

data "aws_iam_policy_document" "scheduler_instances" {
  statement {
    sid       = "StartAndStopPerformanceNodes"
    effect    = "Allow"
    actions   = ["ec2:StartInstances", "ec2:StopInstances"]
    resources = values(aws_instance.nodes)[*].arn
  }
}

resource "aws_iam_role_policy" "scheduler_instances" {
  name   = "${var.name_prefix}-start-stop-instances"
  role   = aws_iam_role.scheduler.id
  policy = data.aws_iam_policy_document.scheduler_instances.json
}

resource "aws_scheduler_schedule_group" "business_hours" {
  count = var.enable_business_hours_schedule ? 1 : 0
  name  = "${var.name_prefix}-business-hours"
}

resource "aws_scheduler_schedule" "start" {
  count = var.enable_business_hours_schedule ? 1 : 0

  name                         = "${var.name_prefix}-start"
  group_name                   = aws_scheduler_schedule_group.business_hours[0].name
  schedule_expression          = "cron(0 8 ? * MON-FRI *)"
  schedule_expression_timezone = var.schedule_timezone

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = "arn:aws:scheduler:::aws-sdk:ec2:startInstances"
    role_arn = aws_iam_role.scheduler.arn
    input = jsonencode({
      InstanceIds = values(aws_instance.nodes)[*].id
    })
  }
}

resource "aws_scheduler_schedule" "stop" {
  count = var.enable_business_hours_schedule ? 1 : 0

  name                         = "${var.name_prefix}-stop"
  group_name                   = aws_scheduler_schedule_group.business_hours[0].name
  schedule_expression          = "cron(0 17 ? * MON-FRI *)"
  schedule_expression_timezone = var.schedule_timezone

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = "arn:aws:scheduler:::aws-sdk:ec2:stopInstances"
    role_arn = aws_iam_role.scheduler.arn
    input = jsonencode({
      InstanceIds = values(aws_instance.nodes)[*].id
    })
  }
}
