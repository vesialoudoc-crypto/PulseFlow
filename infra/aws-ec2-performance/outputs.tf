output "aws_region" {
  description = "Selected AWS region."
  value       = data.aws_region.current.region
}

output "aws_account_id" {
  description = "AWS account that owns the Terraform-managed EC2 performance resources."
  value       = data.aws_caller_identity.current.account_id
}

output "node_instance_ids" {
  description = "Role-to-EC2-instance-ID map used by the deployment script and EventBridge Scheduler."
  value = {
    for role, instance in aws_instance.nodes : role => instance.id
  }
}

output "node_private_ips" {
  description = "Stable private IPv4 addresses used directly by the portable Docker runtime configuration."
  value = {
    for role, instance in aws_instance.nodes : role => instance.private_ip
  }
}

output "loadgen_instance_id" {
  description = "SSM target for the k6 container runner."
  value       = aws_instance.nodes["loadgen"].id
}

output "business_hours_schedule" {
  description = "Enabled EventBridge Scheduler group, or null when automatic business-hours scheduling is disabled."
  value       = try(aws_scheduler_schedule_group.business_hours[0].name, null)
}
