# Security Review Plan

This document defines the structured process for conducting a security-focused code review on this repository. It is intended to be used alongside AI-assisted review tools (e.g., Cursor, Copilot) and referenced before beginning any security pass.

---

## Guiding Philosophy

> **Map before you evaluate. Enumerate before you exploit.**

Do not begin evaluating risk until you have finished building a threat surface inventory. Jumping straight to code review without context produces incomplete coverage and false confidence.

---

## Phase 1 — Establish Context

Before touching a single file, answer these questions. Document the answers in your review notes.

| Question | Notes |
|---|---|
| What does this application do? | |
| Who are the users and what are their trust levels? | |
| What external systems does it communicate with? | |
| What data does it handle, and how sensitive is it? | |
| Where is data persisted? (DB, blob, queue, cache) | |
| What authentication and authorization mechanisms are in use? | |
| Are there any known compliance requirements? (SOC2, HIPAA, etc.) | |

---

## Phase 2 — Threat Surface Inventory

Work through each surface area below and **list every relevant entry point or handler** found in the codebase. Do not evaluate or fix yet — only enumerate.

### 2.1 Input Surfaces
- API controllers and route handlers
- Form inputs and query parameters
- File upload handlers
- Message queue consumers / event handlers
- Webhook receivers
- CLI argument parsers

### 2.2 Authentication & Authorization
- Login, token issuance, and session management
- Auth middleware and filters — especially failure paths
- Role and permission checks — note anywhere they are absent or bypassed
- Token validation logic (JWT, OAuth, API keys)

### 2.3 Secrets & Configuration
- Environment variable and config file access
- Connection strings
- Key Vault / secrets manager integration points
- Any hardcoded values that look like credentials or keys

### 2.4 Outbound Calls
- HTTP/HTTPS calls to external services
- Database queries
- Storage operations (file system, blob, queue)
- Inter-service calls (gRPC, message bus, etc.)

### 2.5 Data Persistence
- ORM usage and raw query construction
- Caching layers
- Log output — note anywhere sensitive data could appear in logs
- Temp file creation and cleanup

### 2.6 Dependencies
- Review `package.json`, `*.csproj`, `Cargo.toml`, `requirements.txt`, etc.
- Flag packages that are outdated, unmaintained, or unfamiliar
- Note any packages with broad system-level access

---

## Phase 3 — Targeted Evaluation

Once the inventory from Phase 2 is complete, work through each identified item using the questions below. Use these as prompts for both manual review and AI-assisted review.

### 3.1 Input Validation & Injection
- Is all external input validated before use?
- Are there any SQL, command, LDAP, or path injection vectors?
- Are file paths sanitized and constrained to expected directories?
- Is deserialization of untrusted data occurring anywhere?

### 3.2 Authentication & Authorization
- Can any authenticated endpoint be reached without valid credentials?
- Are authorization checks applied consistently, or only at the controller level?
- Can a lower-privileged user escalate by manipulating IDs or parameters (IDOR)?
- Are tokens validated for signature, expiry, and audience?

### 3.3 Secrets & Sensitive Data
- Are secrets ever written to logs, error messages, or responses?
- Are credentials committed to source control or present in config files?
- Is sensitive data encrypted at rest and in transit?
- Are secrets rotated, or are they long-lived static values?

### 3.4 Outbound & Third-Party Calls
- Can user input influence the destination of an outbound HTTP call (SSRF)?
- Are TLS certificates validated on outbound connections?
- Is third-party API error output safely handled and not leaked to end users?

### 3.5 Error Handling & Logging
- Do error responses reveal stack traces, internal paths, or system details?
- Are exceptions caught at appropriate boundaries without swallowing security-relevant failures?
- Is there sufficient audit logging for auth events, privilege changes, and data access?

### 3.6 Dependencies
- Do any flagged packages have known CVEs? (Check [OSV](https://osv.dev) or [NVD](https://nvd.nist.gov))
- Are dependencies pinned to specific versions or floating on ranges?
- Are any dependencies pulling in unexpected transitive dependencies?

---

## Phase 4 — AI-Assisted Review Guidance

When using an AI tool (Cursor, Copilot, etc.) as part of this review, follow these practices.

### Prompting Strategy
- **Prefer specific threat questions over role framing.** Instead of "act as a security expert", ask "what SSRF vectors exist in this code?" Role prompts have a mild positive effect but do not substitute for targeted questions.
- **Use enumeration mode first.** Ask the model to list all input entry points or outbound calls before asking it to evaluate any of them.
- **Chain passes by concern.** Run separate passes for auth, input validation, secrets, and dependencies rather than asking for everything at once.

### Suggested Prompt Patterns

```
Without fixing anything, list every location in this file where external input is accepted,
an outbound call is made, or credentials are handled. Do not evaluate them yet.
```

```
Review this [controller / middleware / service] for authorization bypass scenarios.
What happens if the auth check fails or is skipped entirely?
```

```
Where does this code trust input it should not? Focus on trust boundaries between
[external users / internal services / queued messages].
```

```
Are there any locations in this code where sensitive data could appear in a log,
error response, or exception message?
```

### Caveats
- AI tools do not have full codebase context unless explicitly provided. Paste relevant dependencies and interfaces, not just the file under review.
- AI findings are starting points for investigation, not confirmed vulnerabilities.
- AI tools will miss subtle business logic flaws. Manual review of Phase 1 context is not optional.

---

## Phase 5 — Findings & Remediation Tracking

Document findings using the format below. Severity uses the [CVSS](https://www.first.org/cvss/) scale as a guide: Critical / High / Medium / Low / Informational.

| # | Location | Description | Severity | Remediation | Status |
|---|---|---|---|---|---|
| 1 | | | | | Open |

---

## Review Checklist

Use this as a final gate before closing a review.

- [ ] Phase 1 context questions answered and documented
- [ ] All input surfaces identified
- [ ] Auth and authorization paths reviewed, including failure cases
- [ ] No hardcoded secrets or credentials found
- [ ] Outbound calls reviewed for SSRF and certificate validation
- [ ] Error handling reviewed — no sensitive detail leakage
- [ ] Dependencies checked for known CVEs
- [ ] All findings documented with severity and remediation path
- [ ] AI-assisted review prompts used in targeted mode, not generic mode

---

*This document should be updated as the application's architecture evolves. Review plans that go stale produce false confidence.*