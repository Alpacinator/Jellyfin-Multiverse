#!/usr/bin/env python3
"""
Adds a new release to manifest.json, the file Jellyfin reads to learn which plugin versions exist.

Run by .github/workflows/release.yml after the release zip has been uploaded. It:
  1. computes the MD5 checksum of the zip (Jellyfin uses it to verify the download)
  2. builds the download link of the zip on the GitHub release
  3. puts the new version at the top of the "versions" list (newest first)

Related to: manifest.json, Jellyfin.Plugin.Multiverse/meta.json
"""
import argparse
import hashlib
import json


def md5_of_file(path):
    """Returns the uppercase hex MD5 of a file, read in chunks so big files are fine."""
    digest = hashlib.md5()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True, help="path to manifest.json")
    parser.add_argument("--meta", required=True, help="path to the plugin's meta.json")
    parser.add_argument("--zip", required=True, help="path to the release zip")
    parser.add_argument("--version", required=True, help="four part version, e.g. 1.1.0.0")
    parser.add_argument("--repo", required=True, help="GitHub repo as OWNER/NAME")
    parser.add_argument("--owner", required=True, help="plugin owner shown in Jellyfin")
    parser.add_argument("--timestamp", required=True, help="UTC time, e.g. 2026-10-03T12:00:00Z")
    parser.add_argument("--changelog", default="", help="short text shown in the plugin catalog")
    args = parser.parse_args()

    with open(args.meta, encoding="utf-8") as handle:
        meta = json.load(handle)
    with open(args.manifest, encoding="utf-8") as handle:
        manifest = json.load(handle)

    # The manifest is a list of plugins. We only ship one, so use the first entry.
    plugin = manifest[0]

    # Keep the descriptive fields in sync with meta.json.
    for field in ("guid", "name", "description", "overview", "category"):
        plugin[field] = meta[field]
    plugin["owner"] = args.owner

    zip_name = args.zip.replace("\\", "/").split("/")[-1]
    entry = {
        "version": args.version,
        "changelog": args.changelog,
        "targetAbi": meta["targetAbi"],
        "sourceUrl": f"https://github.com/{args.repo}/releases/download/v{args.version}/{zip_name}",
        "checksum": md5_of_file(args.zip),
        "timestamp": args.timestamp,
    }

    # Re-running a release replaces the old entry instead of creating a duplicate.
    versions = [v for v in plugin.get("versions", []) if v.get("version") != args.version]
    versions.insert(0, entry)
    plugin["versions"] = versions

    with open(args.manifest, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")

    print(f"Added version {args.version} (md5 {entry['checksum']})")


if __name__ == "__main__":
    main()
