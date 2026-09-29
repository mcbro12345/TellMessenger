# Security Policy

## Supported Versions

Only the latest released version of Tell Messenger receives security fixes. Update to the newest release before reporting an issue, in case it is already fixed.

## Reporting a Vulnerability

Report security vulnerabilities privately using GitHub's [private vulnerability reporting](https://github.com/mcbro12345/TellMessenger/security/advisories/new), not a public issue. This keeps details out of view until a fix is available.

Include:

- The affected Tell Messenger version and Dalamud/API version
- Steps to reproduce
- The potential impact

Expect an initial response within a few days. Confirmed vulnerabilities will be fixed and disclosed once a patched release is available.

## Scope

Tell Messenger is a client-side Dalamud plugin that runs inside your own FINAL FANTASY XIV process. It does not run a server or accept network connections, but it does display messages and links sent by other players, stores your message history locally, and looks up portraits on the Lodestone. Relevant reports include things like unsafe memory access, crashes or unintended actions triggered by a crafted tell or chat link, or exposure of your stored message history.
