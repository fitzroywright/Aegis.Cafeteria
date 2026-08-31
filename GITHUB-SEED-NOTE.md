# Repository seed note

`master` currently contains only the initial `.gitignore`; the Aegis.Cafeteria v6 source baseline has not yet been committed to GitHub.

This ChatGPT branch therefore contains the complete new v7 Engineering subsystem source plus small integration patches that are intended to be applied to the v6 baseline. It deliberately does not overwrite `master` or pretend that the repository is already a complete buildable checkout.

Once v6 is seeded into `master`, apply the files and patches from this branch (or rebase this branch onto that source baseline) and run the normal restore/build/test/static-audit gate.
