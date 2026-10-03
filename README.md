# Jellyfin Multiverse

Lets users of paired Jellyfin servers sign in to each other's servers, with limits and library
restrictions, plus a combined library view. Requires Jellyfin 12.1 or newer.

## Install

1. In Jellyfin go to Dashboard > Plugins > Repositories and add a repository.
2. Name: `Jellyfin Multiverse`
3. URL: `https://raw.githubusercontent.com/Alpacinator/Jellyfin-Multiverse/main/manifest.json`
4. Open the Catalog tab, install Jellyfin Multiverse, and restart Jellyfin.

The plugin shows up after the first release is published: `git tag v1.1.0.0` then `git push origin v1.1.0.0`.

More details: [Jellyfin.Plugin.Multiverse/README.md](Jellyfin.Plugin.Multiverse/README.md)
