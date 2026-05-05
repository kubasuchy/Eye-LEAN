# Security

Eye_lean is a research toolkit, not production infrastructure, but we
still take security reports seriously.

## Reporting a vulnerability

**Please do not open a public issue for security-sensitive reports.**

Email **jhs212@scarletmail.rutgers.edu** with:

- A description of the vulnerability and its impact.
- Steps to reproduce or a proof-of-concept.
- Affected version(s) — tag, commit hash, or "main as of \<date\>".
- Optional: a suggested fix.

You can expect an acknowledgement within seven days. We'll work with
you on a coordinated disclosure timeline; ninety days is the default
unless the severity warrants something tighter.

## Scope

In scope:

- Code execution, sandbox escape, or path traversal in the Python
  analysis package's CSV loader, replay parser, or any helper that
  reads researcher-supplied files.
- Vulnerabilities in the Unity-side data export path that could be
  triggered by malformed device input or scene assets.
- Insecure-by-default configuration that exposes participant data.

Out of scope:

- Issues that require physical access to a researcher's machine or
  headset.
- Vulnerabilities in third-party dependencies (report those upstream;
  we'll bump after a fix lands).
- Social-engineering or phishing scenarios.

## Supported versions

Only the most recent release line receives security fixes. There is
no LTS.
