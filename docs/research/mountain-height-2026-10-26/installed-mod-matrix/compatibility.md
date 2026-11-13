# Copied installed-package compatibility check

Two disposable pre.2 K worlds, at heights 384 and 512, used the copied installed package set. Both generated all
fourteen target regions, agreed on sea level and passed the four climate probes. Neither report records missing
river ramps or water seams. Both retain **strict failure** status because each server logs one error:

```text
Failed to find language folder: ...hydrology-0.1.11.zip_...\assets\game\lang
```

There are no other recorded errors. The Hydrology archive has no language-folder entries. A separate control uses
the original installed `earthmap-0.16.14-rivers.2.zip` with the same frozen companion packages at height 384. It
generates all fourteen regions and reproduces the same single error, with no other errors. This establishes that
the packaging diagnostic predates this mountain branch. No installed archive was edited to suppress it.

- [Frozen package names and hashes](inputs.json).
- [Strict experimental results](summary.json) and [site measurements](site-comparisons.csv).
- [Experimental checks and errors](checks.json).
- [Original-build control](original-build-control.json), Earth package SHA-256
  `3d6ccc7a2f78540a82e8d62ec657b6afb021dc4547bdddd405548c71a941136f`.

The checks support dedicated-server generation with this package set. Client-only mods, existing mod configuration,
seasonal rendering and a joined player were not tested. This mountain branch is based on main 0.16.13 and lacks the
separate newer river/seam playtest work; use fresh worlds in a separate test setup pending integration review.
