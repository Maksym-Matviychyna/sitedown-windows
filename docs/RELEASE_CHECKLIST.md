# Release checklist

Before releasing a new SiteDown Windows version:

1. Update `CURRENT_VERSION_CODE`.
2. Update `CURRENT_VERSION`.
3. Make sure the website API returns the correct `windows_latest_version_code`.
4. Build in Release mode.
5. Test:
   - Apply token
   - Monitoring start/stop
   - Website OK line
   - Keyword-missing alert
   - Website timeout/error
   - Internet unavailable behavior
   - Close to tray
   - Single-instance behavior
   - Start with Windows
6. Publish the app.
7. Sign `SiteDown.exe` and installer if code-signing certificate is available.
8. Upload release files to GitHub Releases and/or `www.sitedown.app`.

9. Confirm GitHub repository license is shown as GPL-3.0.

10. Confirm `AUTHORS.md` and `NOTICE` contain the correct developer/owner name.


Current release: 1.0.5 / code 5
