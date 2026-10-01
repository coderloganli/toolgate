# ToolGate

An enterprise gateway that stands between AI agents and internal APIs.

Internal services are registered as agent-callable tools, and every call is checked against a policy engine before it reaches the service behind it. A policy binds a caller identity to a set of tools and a parameter allowlist. Calls outside the authorized scope are refused, and calls marked sensitive are held for human approval rather than executed.

## Architecture

- **Gateway**: C# on ASP.NET Core. Callers authenticate with OAuth 2.0 bearer tokens; the gateway validates the JWT and maps its claims to the caller identity the policy engine checks.
- **API**: REST, documented as an OpenAPI specification served through Swagger UI.
- **MCP**: the tool surface is exposed as an MCP server over JSON-RPC, so any MCP-speaking agent can use it without a custom integration.
- **Persistence**: policies and a full call audit trail are stored through EF Core, running against both SQL Server and PostgreSQL. Every action an agent takes against an internal system traces back to a caller, a policy decision and an approver.
- **Console**: React and TypeScript, where an operator clears pending approvals and queries the audit trail.
- **Delivery**: containerized with Docker, tested with xUnit in GitHub Actions CI, deployed to Microsoft Azure.

## Status

Under active development. The scope above is committed; code is being added to this repository as it is built.
