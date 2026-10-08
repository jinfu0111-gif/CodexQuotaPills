# Notices

## CodexUsageOverlay

Codex Quota Pills is a modified work based on:

- Project: https://github.com/ivan51769/CodexUsageOverlay
- Version used as the base: v1.3.6
- Base revision: `e8615da7293b1523c3a9dd7f398a99e1c8c6ab85`
- License: GNU Affero General Public License, version 3

The Codex MSIX updater backend in `MsixUpdater/` was adapted from the same
project at revision `26dfa7f94cc784bd8c724ddbcbde511c8bcf0e0f` (2026-09-28).
It is integrated in-process with a new Quota Pills UI and stricter package
identity and digest checks. It does not install or launch the upstream app.

The original copyright notices and AGPL-3.0 license are retained. Changes made
for Codex Quota Pills include the composer-anchored pill layout, responsive
field prioritization, manual quota refresh interaction, project branding,
documentation, and related tests.

## Cockpit Tools

The public behavior of showing small quota fields near the Codex composer was
reviewed in `jlcodes99/cockpit-tools`. At the time of review, no repository-level
license was present. No source code from that repository is included in this
project. The Windows pill renderer in `ComposerUsagePills.cs` is an independent
implementation.

## OpenAI

Codex and OpenAI are trademarks of OpenAI. This is an unofficial community
project and is not affiliated with or endorsed by OpenAI.
