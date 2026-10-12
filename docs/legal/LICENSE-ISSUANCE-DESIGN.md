# Manual License Issuance: Design Note (DRAFT)

Decision (2026-10-12): no checkout and no billing provider for now. Customers email a request, the Licensor
reviews it and issues a license key by hand. This note keeps that process compatible with later automation.

## Principles
- **Fail-open:** an absent, bad or expired key never touches redirect resolution (see Free Forever Promise).
- **Offline validation:** the Premium package verifies the key locally. No license server, so a Licensor
  outage can never break a customer's site.
- **Grace period:** after expiry, installed features keep working [30 days] with a dashboard notice; after that
  only new-version access and support lapse (perpetual fallback per the EULA draft).

## Key format
A signed token: `base64url(payload).base64url(signature)`.
- Payload JSON: `v`, `id` (key ID), `edition` (standard-plus | premium | company), `licensee`, `domains[]`
  (wildcards allowed for subdomains), `issued`, `expires`, `maxVersion` (for the perpetual fallback).
- Signature: Ed25519 with the private key held only by the Licensor (offline machine or password manager).
  The public key ships in the Premium package.
- Domain match is advisory: mismatch shows a notice, never disables redirects. Dev, staging and `localhost`
  are always allowed.

## Manual workflow
1. Request email arrives; Licensor checks Umbraco version, domains, use case.
2. Licensor runs a small local CLI (`tools/license/issue`, to be written in Phase 1) that signs the payload and
   appends a row to a private register (CSV or SQLite, kept out of git).
3. Key is emailed with install steps. Revocation is a published list the Premium package may fetch
   (fail-open if unreachable) [optional, decide in Phase 1].

## Future automation
If volume grows, put Polar (license keys, Turkey payouts via Stripe Connect Express, per 2026 docs) or a
similar provider in front of the same signed-key format; only the issuing step changes.

## Open questions
- Grace and expiry lengths. Max number of domain changes per term.
- Where the private signing key lives and how it is backed up (loss means re-issuing every key).
- Whether telemetry may include a hashed key ID for support, opt-in only.
