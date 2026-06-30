# Contributing to KingdomMod

Thanks for considering a contribution.

- **Dev environment setup, build, and deploy-for-testing steps:** see
  [Developer Setup](docs/getting-started.md#developer-setup) in the getting
  started guide.
- **Writing a mod against the SDK:** see
  [docs/api-reference.md](docs/api-reference.md) and
  [Writing Your First Mod](docs/getting-started.md#writing-your-first-mod).
- **Mount/steed stat tuning:** see [docs/mount-modding.md](docs/mount-modding.md).
- **Bugs and feature ideas:** open an issue using the templates, or ask first
  in the [KingdomMod Discord](https://discord.gg/VpuCg6Hcrs).

## Pull requests

- Keep PRs focused on one change; explain the *why* in the description.
- Match the existing code style (defensive `try/catch` around game-state
  access, no unrelated reformatting).
- If your change touches a released mod or the loader, note whether it needs
  a version bump and a `docs/releases/<tag>.md` entry.
