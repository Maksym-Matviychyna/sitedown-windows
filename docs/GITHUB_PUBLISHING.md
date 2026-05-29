# Publishing this project to GitHub

## Recommended repository name

```text
sitedown-windows
```

## First upload

Create an empty public repository on GitHub. Do not add a README, license, or `.gitignore` on GitHub because this project already contains them.

Then run these commands from the `SiteDownWindows` folder:

```cmd
git init
git add .
git commit -m "Initial open source release"
git branch -M main
git remote add origin https://github.com/YOUR_USERNAME/sitedown-windows.git
git push -u origin main
```

Replace `YOUR_USERNAME` with your GitHub username or organization name.

## What not to upload

Do not upload:

```text
bin/
obj/
publish/
installer/
*.pfx
*.p12
*.key
*.pem
```

These are already ignored by `.gitignore`.

## Creating a GitHub release

1. Publish the app in Release mode.
2. Create a ZIP from the publish folder.
3. On GitHub, open the repository.
4. Go to **Releases**.
5. Click **Draft a new release**.
6. Use a tag like:

```text
v1.0.7
```

7. Attach the portable ZIP or installer.
8. Publish the release.


## License

This repository uses GPLv3 / GNU General Public License v3.0.


## Author

Developer / owner: Maksym Matviychyna

Copyright (C) 2026 Maksym Matviychyna
