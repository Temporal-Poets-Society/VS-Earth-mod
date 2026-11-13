# 0043: Remove an unsupported publication manifest field

Date: 31 October 2026. Status: upload blocker fixed in the unpublished Earth 1.0.0 archive; author must retry the upload.

The author’s Mod DB Add Release screenshot rejects the uploaded `earthmap-1.0.0.zip`: **Unexpected property 'incompatibilities' on modinfo.json with value '{}'**. The empty field existed in the candidate manifest but is not accepted by the site's parser. It is not needed: it declares no incompatibilities. The source manifest now omits it. The release packager refuses to package it if it is reintroduced.

The unpublished main 1.0.0 ZIP was rebuilt using the explicit overwrite path, preserving the failed upload archive. **Only `modinfo.json` differs** between the initial and corrected 1.0.0 packages; its only semantic change is removing `incompatibilities: {}`. DLL/PDB, assets, dependencies, settings and every geographic file are byte-identical. Both data archives remain unchanged. All three versions remain 1.0.0.

Corrected main: **28,490,075 bytes**, SHA-256 `6d1f7fce44972d10fd5d885e53e2ac729d80d61460aee1e7242963c42bfa51ee`. The previous failed ZIP (SHA-256 `3b3dd7321f9ffe1a196d36d599a9a9520c8697eec8aa01f7dc2e9cdf7ff95e21`) is preserved in `Earth/.local/package-backups/earth-1.0.0-before-moddb-manifest-fix/` in the main checkout and in the worktree release packager’s SHA-named backup folder.

Verification: Release build has zero warnings/errors; ZIP CRCs pass; complete payload comparison finds only the manifest change; all remaining manifest fields match the [official modinfo reference](https://wiki.vintagestory.at/Modding:Modinfo). A failure-path check reintroducing the empty field is rejected before writing any archives. Prior 507 package/data checks and 35 dependency checks are retained as the runtime baseline; the metadata-only correction does not claim a new client/server test. Upload acceptance remains unverified until the author removes the previous attachment and selects the corrected local ZIP again.

The second screenshot and author-provided URL identify the public alias as **earth**, at https://mods.vintagestory.at/earth. The publication draft now records that alias; internal mod ID `earthmap` stays unchanged.

Sources: author upload-error/alias screenshots; `modinfo.json`; `tools/package-publication.py`; [current package verification](../../../../../docs/publication-packages-1.0.0.json). The [upstream Mod DB upload wrapper](https://github.com/anegostudios/vsmoddb/blob/master/lib/modinfo.php) invokes its separate `modpeek` parser, so successful game startup alone was not proof of upload compatibility.
