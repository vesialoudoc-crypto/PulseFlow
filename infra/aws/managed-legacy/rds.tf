resource "aws_db_subnet_group" "postgresql" {
  name       = "${var.name_prefix}-postgresql"
  subnet_ids = values(aws_subnet.data)[*].id

  tags = {
    Name = "${var.name_prefix}-postgresql"
  }
}

resource "aws_db_instance" "postgresql" {
  identifier                  = "${var.name_prefix}-postgresql"
  allocated_storage           = var.rds_allocated_storage_gib
  storage_type                = "gp3"
  storage_encrypted           = true
  engine                      = "postgres"
  engine_version              = data.aws_rds_engine_version.postgres.version_actual
  instance_class              = var.rds_instance_class
  db_name                     = "pulseflow"
  username                    = "pulseflow"
  manage_master_user_password = true
  port                        = 5432

  db_subnet_group_name   = aws_db_subnet_group.postgresql.name
  vpc_security_group_ids = [aws_security_group.postgresql.id]
  publicly_accessible    = false
  multi_az               = false

  backup_retention_period    = var.rds_backup_retention_days
  auto_minor_version_upgrade = true
  deletion_protection        = false
  skip_final_snapshot        = true
  copy_tags_to_snapshot      = true

  tags = {
    Name = "${var.name_prefix}-postgresql"
  }
}
