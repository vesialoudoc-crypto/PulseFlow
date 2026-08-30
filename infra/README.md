# Infrastructure layout

Infrastructure follows the provider-first convention:

```text
infra/<provider>/<deployment-model>/
```

Examples:

- `infra/aws/ec2` for the active EC2 provisioning root;
- `infra/aws/managed-legacy` for the preserved, non-active managed AWS implementation;
- `infra/render` for the existing provider-specific Render definition;
- future `infra/azure/<model>`; and
- future `infra/railway/<model>` when a separate deployment model is needed.

Do not create empty directories for future providers. Provider-wide helpers may sit
outside a deployment-model directory only when they genuinely apply to more than one
deployment model.
