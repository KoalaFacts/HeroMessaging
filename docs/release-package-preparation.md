# Release package preparation

`create-release.yml` requires a successful main-branch push run of `ci.yml` for
the exact source commit. It downloads `release-packages-<commit>` from that run,
then checks the package identities and nuspec repository commits. It never uses
the `release-packages-latest` artifact.

CI packages have a `-ci.*` version. Renaming their archives does not change that
version, their assembly metadata or their package dependencies. Release
preparation instead checks out the validated commit and runs `dotnet pack` with
both `Version` and `PackageVersion` set to the requested release version. The
version must be canonical `X.Y.Z[-prerelease]`, without build metadata or leading
zeroes in numeric identifiers.

The release set contains HeroMessaging, HeroMessaging.Abstractions and
HeroMessaging.RingBuffer, each with its symbol package. RingBuffer is a direct
dependency of HeroMessaging and must be available at the same release version.
Before signing or uploading, preparation rejects mismatched filenames, package
IDs, versions, source commits, missing packages, incorrect internal dependency
versions and changes to the external dependencies of the CI packages.

`release-manifest.json` records the validated CI run/artifact, source commit and
the identities, dependency versions and SHA256 hashes of the CI and unsigned
release archives. Signing can change archive hashes; the manifest explicitly
labels these as unsigned hashes. The workflow attaches the manifest to the
release alongside the packages.

The helper only prepares local files; it does not sign, tag or publish:

```bash
python3 .github/scripts/prepare_release.py \
  --source /path/to/clean/exact/checkout \
  --ci-packages /path/to/artifact/from/validated/run \
  --output /path/to/new/release-packages \
  --version 1.2.3-rc.2 --commit <40-character-sha> --ci-run-id <run-id>
```

The workflow establishes the remote CI run binding. Supplying a run ID to this
local helper alone does not prove that a CI run occurred. Offline tests use
synthetic metadata or locally built fixture packages and must be reported as
such. Run the fast identity regressions with:

```bash
python3 -m unittest discover -s .github/scripts -p 'test_prepare_release.py' -v
```
