resource "aws_instance" "nodes" {
  for_each = local.nodes

  ami                         = var.ami_id
  instance_type               = each.value.instance_type
  subnet_id                   = aws_subnet.public.id
  associate_public_ip_address = true
  vpc_security_group_ids      = [each.value.security_group_id]
  iam_instance_profile        = aws_iam_instance_profile.ssm.name
  monitoring                  = false

  credit_specification {
    cpu_credits = "standard"
  }

  metadata_options {
    http_endpoint = "enabled"
    http_tokens   = "required"
  }

  root_block_device {
    delete_on_termination = true
    encrypted             = true
    volume_size           = var.root_volume_size_gib
    volume_type           = "gp3"
  }

  tags = {
    Name = "pulseflow-${each.key}"
    Role = each.key
  }

  depends_on = [aws_iam_role_policy_attachment.ssm]
}
