# Security Policy

Cocinaris is in an early foundation phase. Security, privacy, household isolation, food-safety behavior, and AI data handling are treated as product requirements from the beginning.

## Reporting a vulnerability

Do not disclose a suspected vulnerability in a public issue when it could expose:

- credentials or secrets;
- personal or household data;
- authentication or authorization bypasses;
- cross-household data access;
- destructive data behavior;
- prompt or model behavior that enables private-data disclosure;
- unsafe allergy or food-safety handling.

Until a private reporting channel is formally published, contact the repository owner through a private GitHub channel available to you and provide only the minimum information needed to establish contact. Do not include production data, credentials, or unnecessary personal information in the first message.

## Include in a report

- affected component and environment;
- clear impact description;
- privacy-safe reproduction steps;
- relevant version, branch, or commit;
- sanitized logs or correlation identifiers;
- whether exploitation required authentication or a specific household role;
- suggested mitigation when known.

## Handling principles

Security reports should be handled using:

- least disclosure;
- prompt acknowledgement and triage;
- impact assessment covering confidentiality, integrity, availability, safety, and privacy;
- a reproducible fix and regression test;
- coordinated disclosure when applicable;
- documentation of operational or migration steps.

## Supported versions

No production release is currently supported. A supported-version policy will be published before the first external release.

## Secret and data rules

- Never commit API keys, tokens, passwords, production configuration, or private certificates.
- Never use production household data in development, tests, issues, or pull requests.
- Redact personal data and full AI transcripts from logs and reports.
- Rotate any credential immediately if it is exposed, even if the commit is later removed.
