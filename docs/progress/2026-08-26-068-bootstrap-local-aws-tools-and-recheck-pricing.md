# Checkpoint: Bootstrap local AWS tooling and recheck Frankfurt staging pricing

**Date:** 2026-08-26

## Starting point

The AWS staging Terraform from checkpoint 067 was formatted and locally validated, but
Terraform and AWS CLI v2 were absent from PATH. No AWS CLI profile, authenticated
identity, GHCR bootstrap secret ARN, or selected verified private GHCR image was
available. No AWS resources, Terraform state, or real account-specific plan existed.

## What changed

- Installed AWS CLI v2.36.29 and Terraform v1.15.8 locally. Terraform runs from the
  user-level WinGet package directory until the hosting shell receives its updated PATH.
- Ran `terraform fmt -check -recursive`, `terraform init`, and `terraform validate`
  against the existing AWS definition. The locked AWS provider 6.61.0 and Random
  provider 3.9.0 were reused; all checks passed.
- Verified that AWS CLI has no configured profile and that `sts get-caller-identity`
  returns `NoCredentials`. Therefore no AWS account ID, IAM principal, service
  permissions, GHCR secret state, availability zones, resolved RDS patch, or real plan
  can be inspected yet.
- Attempted a read-only manifest lookup for the candidate implementation image
  `ghcr.io/vesialoudoc-crypto/pulseflow-api:sha-b469f401c704d695024654aece24cc7e0a0dab99`.
  GHCR returned `unauthorized`, so its existence is not claimed and it was not selected.
- Rechecked the current public Frankfurt price-list data for the accepted resource
  choices. Amazon MQ documents a 200 GB default EBS volume for the selected
  `mq.m7g.medium` single-instance broker, correcting the earlier 5 GB factual error.
  The resulting post-deployment fixed-ish baseline is about USD 209.98/month before
  usage, applicable free tiers, and tax. No infrastructure configuration changed.

## Resulting repository state

The AWS Terraform configuration remains locally valid and unapplied. AWS CLI v2 and
Terraform are now installed, but there is still no authenticated AWS profile, no
verified immutable GHCR image, no GHCR credential secret ARN, no Terraform plan or
state, and no AWS resource. `terraform apply`, the deployment script, migrations,
service rollout, and smoke tests were not run.

The no-NAT/public-ALB/private-dependency topology and accepted resource classes are
unchanged. The cost documentation now reflects the selected broker's 200 GB default
storage volume and current regional price-list rates.

## Verification

- `aws --version` returned `aws-cli/2.36.29`.
- `aws configure list-profiles` returned no profiles.
- `aws sts get-caller-identity --region eu-central-1 --output json` failed with
  `NoCredentials`; it created no resources.
- `terraform version` returned 1.15.8, which satisfies the configuration's `>= 1.6.0`
  requirement. Terraform reports 1.15.9 is newer, but provider selection remains
  locked at AWS 6.61.0 and Random 3.9.0.
- `terraform -chdir=infra/aws fmt -check -recursive` passed.
- `terraform -chdir=infra/aws init` passed.
- `terraform -chdir=infra/aws validate` passed.
- The read-only GHCR manifest lookup returned `unauthorized`.
- AWS Price List regional offer data was queried for Amazon MQ, RDS, Amazon ECS,
  ElastiCache, Elastic Load Balancing, Amazon VPC, AWS Secrets Manager, and Amazon
  CloudWatch. No AWS account credentials were used.

## Decisions made

- No architecture, Terraform-resource, region, image, credential, or paid-resource
  decision was made. ADRs 0020 and 0021 remain accepted.
- The 5 GB Amazon MQ storage statement was a factual documentation error, not an
  accepted architecture decision. The current 200 GB default is now documented; see
  [ADR 0021](../decisions/0021-define-first-aws-staging-resource-configuration.md).

## Intentionally unresolved

- AWS account bootstrap, authenticated effective principal, account permissions, and
  approved profile name.
- Least-privilege GHCR package-read credential entry, the resulting existing-secret
  ARN, and a verified immutable image SHA.
- The account-specific availability zones, PostgreSQL 17 patch, actual resource plan,
  exact create/change/destroy counts, full plan/security review, and explicit paid
  resource approval.
- Any apply, migration, rollout, health response, smoke-test result, or actual bill.

## Next recommended step

Configure and authenticate the intended `pulseflow-staging` AWS CLI profile, verify its
STS identity in `eu-central-1`, then use a least-privilege GitHub Packages read token to
create or identify the external GHCR credential secret without exposing its value.
Verify a published full-SHA GHCR image, prepare an ignored local tfvars file with only
the profile, image, and secret ARN, and run the real saved Terraform plan. Review that
plan and the updated cost/security checkpoint before requesting explicit permission for
`terraform apply`.
