# Infrastructure layout

Infrastructure follows the provider-first convention:

```text
infra/<provider>/<deployment-model>/
```

Examples:

- `infra/aws/ec2` for the active EC2 provisioning root;
- `infra/render` for the existing provider-specific Render definition;
- future `infra/azure/<model>`; and
- future `infra/railway/<model>` when a separate deployment model is needed.

Do not create empty directories for future providers. Provider-wide helpers may sit
outside a deployment-model directory only when they genuinely apply to more than one
deployment model.

The former managed AWS implementation is preserved by Git history, ADRs, pull
requests, and historical checkpoints; it is not live infrastructure code in this
repository tree.
