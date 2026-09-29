# Contributing to Tell Messenger

Thanks for considering a contribution.

## Before opening a pull request

1. Keep changes focused and explain the user-visible reason for them.
2. Build the plugin in Release configuration when possible.
3. Test changes in an appropriate Dalamud development environment.
4. Keep message history on the player's own machine, and do not add network requests beyond the Lodestone portrait lookup without documenting them in the README.
5. Preserve third-party attribution and licensing when changing vendored or derived data.
6. If generative AI materially produced the submitted code, say so in the pull request description and describe what you personally tested or reviewed.

## Code style

Follow the existing C# style and `.editorconfig`. Prefer small, readable changes over large unrelated refactors.

## Bug reports

Include the Tell Messenger version, Dalamud/API version, steps to reproduce, expected behavior, actual behavior, and relevant Dalamud log output (`/xllog`) when safe to share.
