# Deploy the AWS EC2 environment

For implementation details, see [INFO.md](INFO.md).

Run every command below from the repository root unless the command says otherwise.

## 1. Install local prerequisites

Install Terraform 1.6 or later, AWS CLI v2, PowerShell 7, and the AWS Session Manager plugin. Confirm the tools are available:

```powershell
terraform version
aws --version
pwsh --version
session-manager-plugin --version
```

Expected: each command reports a version.

## 2. Configure local AWS credentials

Copy [`.env.example`](../../../.env.example) to the ignored repository-root `.env`. Set `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_REGION`, and `AWS_DEFAULT_REGION` for the intended AWS account. Use `eu-central-1` for both region variables.

Load the file and confirm the account:

```powershell
. ./scripts/aws/EnvFileLoader.ps1
aws sts get-caller-identity
```

Expected: the returned account and ARN are the account in which you intend to create the disposable environment.

## 3. Set Terraform variables

Create or edit the ignored `infra/aws/ec2/terraform.tfvars` file. Set a current Amazon Linux 2023 x86_64 AMI ID for `eu-central-1` and the default HTTP source CIDR.

```hcl
ami_id                   = "ami-<current-amazon-linux-2023-x86_64-ami>"
allowed_http_source_cidr = "0.0.0.0/0"
```

For this disposable environment, `0.0.0.0/0` exposes the app HTTP port publicly. SSH remains closed, and PostgreSQL, RabbitMQ, and Redis remain available only through the private VPC. You may use a narrower CIDR if needed, but it is optional.

Expected: both required variables are present.

## 4. Initialize Terraform

```powershell
terraform -chdir=infra/aws/ec2 init
```

Expected: Terraform reports successful initialization.

## 5. Validate the configuration

```powershell
terraform -chdir=infra/aws/ec2 validate
```

Expected: `Success! The configuration is valid.`

## 6. Create and inspect the plan

```powershell
terraform -chdir=infra/aws/ec2 plan
```

`plan` only previews the AWS changes; it does not create resources.

Expected: the plan shows the intended four EC2 hosts and their supporting network and SSM resources, with no unexpected account or region.

## 7. Create the environment

```powershell
terraform -chdir=infra/aws/ec2 apply
```

Type `yes` only after confirming that the reviewed plan is the one Terraform will apply.

Expected: Terraform reports `Apply complete!`, then continue with [EC2 operations and runtime deployment](ops/README.md).

## Destroy the environment

Run this after you no longer need the disposable environment. This removes the AWS resources Terraform created.

```powershell
. ./scripts/aws/EnvFileLoader.ps1
terraform -chdir=infra/aws/ec2 plan -destroy
terraform -chdir=infra/aws/ec2 destroy
terraform -chdir=infra/aws/ec2 state list
```

Review the destroy plan, then type `yes` at the destroy confirmation. Expected: `Destroy complete!` and no output from `state list`.
