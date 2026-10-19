# Putting the project on GitHub (step by step, no git knowledge needed)

Git keeps a history of your project; GitHub stores a copy online.  You do these steps once, and a few commands each time you want to publish changes.

## What is and isn't uploaded

Only the files listed by the project's `.gitignore` rules are left out: the **game assets** (`client_files/`, `client/assets/`), build output
(`release/`, `build/`), downloaded tools (`tools/godot`, `tools/dotnet` ...), your player data (`userdata/`), server run-time data
(savegame, logs) and private notes.  About 6 MB are uploaded.

Check what would be uploaded at any time with `git status` (shows new / changed files) and `git ls-files` (everything git tracks).

## One-time setup

```bash
git config --global user.name  "Your Name"
git config --global user.email "you@example.com"        # the email of your GitHub account
```

## Make the first upload

In a terminal, inside the project folder:

```bash
git status                                  # look at the list: no assets, no release/ folder
git add -A                                  # stage every file that is not ignored
git commit -m "Initial import of Project Meridian 59"
git branch -M main
git remote add origin https://github.com/catastrophic-impact/project-meridian59.git
git push -u origin main
```

`git push` asks for your GitHub **username** and a **password**.  GitHub no longer accepts your account password there; use a *personal access token*:

1. On github.com: your avatar → *Settings* → *Developer settings* → *Personal access tokens* → *Tokens (classic)* → *Generate new token (classic)*.
2. Name it, tick the **repo** box, choose an expiry, *Generate token*, and copy the token (it is shown once).
3. Paste the token as the password when `git push` asks.  (To avoid typing it again: `git config --global credential.helper store` before pushing.)

Alternatively install the GitHub command line tool (`gh auth login`) and follow its prompts.

## Publishing later changes

```bash
git status                                  # what changed
git add -A
git commit -m "Short description of what you changed"
git push
```

## Things to know

* Never put passwords, tokens or the game assets into the project folder paths that are tracked; if you add a new folder with private data, add it to `.gitignore` first.
* If you ever add assets by accident: `git rm -r --cached <folder>` then commit — but the files stay in the history, so ask for help before pushing.
* To get the project on another machine: `git clone https://github.com/catastrophic-impact/project-meridian59`.
* `git log --oneline` shows the history; `git diff` shows uncommitted changes.
