output "instance_ids" {
  description = "Role-to-instance-ID map for explicit SSM operations."
  value = {
    for role, instance in aws_instance.nodes : role => instance.id
  }
}

output "instance_private_ips" {
  description = "Role-to-private-IPv4 map for the later runtime configuration."
  value = {
    for role, instance in aws_instance.nodes : role => instance.private_ip
  }
}

output "instance_public_ips" {
  description = "Role-to-auto-assigned-public-IPv4 map. Public addresses are for outbound-capable hosts and approved app HTTP testing only; SSM is the operator path."
  value = {
    for role, instance in aws_instance.nodes : role => instance.public_ip
  }
}
