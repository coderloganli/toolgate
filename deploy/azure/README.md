# Deploying to Microsoft Azure

ToolGate runs as a single container on **Azure Container Apps**, with its database on **Azure Database for
PostgreSQL – Flexible Server** or **Azure SQL Database**, and callers authenticated by **Microsoft Entra ID**.

## Resources

| Resource | Purpose |
| --- | --- |
| Azure Container Registry | Holds the `toolgate-api` image built from the repository `Dockerfile`. |
| Azure Container Apps environment + app | Runs the gateway on port 8080 with HTTPS ingress. |
| Azure Database for PostgreSQL or Azure SQL Database | Policies, approvals and the audit trail. |
| Microsoft Entra ID app registration | Issues the OAuth 2.0 access tokens the gateway validates. Define the `ToolGate.Operator` and `ToolGate.Admin` app roles on it. |
| Azure Key Vault | Holds the database connection string, referenced as a Container Apps secret. |

## Container app configuration

| Setting | Value |
| --- | --- |
| `ConnectionStrings__ToolGate` | Secret reference to the connection string in Key Vault |
| `Database__Provider` | `PostgreSQL` or `SqlServer` |
| `Auth__Authority` | `https://login.microsoftonline.com/<tenant-id>/v2.0` |
| `Auth__Audience` | Application ID URI or client ID of the ToolGate app registration |
| `Auth__SubjectClaim` | `sub` (or `oid` to bind policies to Entra object IDs) |

Swagger UI is off outside Development; set `OpenApi__Enabled=true` to expose it.

## GitHub Actions

`.github/workflows/deploy-azure.yml` is a manually triggered workflow that builds the image in the registry and
rolls out a new Container Apps revision. It signs in with OpenID Connect federation, so it needs no stored
secrets, only these repository variables:

- `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` – a service principal or user-assigned managed
  identity with a federated credential for this repository's `production` environment
- `AZURE_ACR_NAME`, `AZURE_RESOURCE_GROUP`, `AZURE_CONTAINERAPP_NAME`
