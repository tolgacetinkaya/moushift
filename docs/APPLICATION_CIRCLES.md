# Application-specific Circles

1. Select a mouse in **Devices**, then **Edit Circle**.
2. **All applications** is that mouse's fallback Circle. Choose **+ App-specific Circle** to open the searchable app list inside the workspace. The mouse inspector's **+ Add app** opens the same flow.
3. Select an app and choose **Add & edit Circle**. A new Circle starts as a copy of this mouse's current fallback draft, including folders. Selecting an existing app reopens its Circle without replacing it.
4. Keep **Switch Circle automatically with the active app** enabled for automatic switching. Drag actions onto the Circle and choose **Save changes**. Nothing is written when browsing or cancelling the picker; closing a changed editor offers to discard its draft.
5. Assign Click Circle to a supported button for this mouse and enable Circle. The same button opens the app-specific Circle when that app is in front; other apps use **All applications**. Other mice keep their own profiles.

The editor's **Circle for** selector chooses what to edit, not the foreground app. **Switch with active app** controls automatic Circle switching for this mouse. When disabled, the adjacent selector chooses a fixed Circle. Existing saved switching preferences are preserved unless explicitly changed.

## App discovery and limits

Discovery is read-only, asynchronous and local: Windows AppsFolder, current-user/machine App Paths, registered package manifests and windowed running applications. It does not launch applications, install dependencies, scan the entire disk, upload an inventory or change registry settings. Entries are merged by executable path, not display name. Protected entries can be skipped; unresolved entries are shown with Browse instructions and cannot create a broken profile.

Windows does not expose every program through one complete list. Use **Refresh list** after opening a missing portable app, or **Browse for an app** to select its executable. A running launcher and the actual application may be different executables: select the process that owns the application window. App matching uses the foreground executable path, not browser tabs, websites or document names. Some hosted Store apps do not expose their own foreground executable, and a package update can move its executable; these cases may need reselecting the executable and are not universally supported.

Windows references: [App Paths registration](https://learn.microsoft.com/en-us/windows/win32/shell/app-registration), [package enumeration](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagesbypackagefamily), [package locations](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagepathbyfullname).

Verification uses isolated settings and fixtures for profile independence, fallback, trigger inheritance, duplicate selection, draft persistence and the inline picker. A read-only local discovery diagnostic is available with `dotnet run --project tests/Clickloom.Windows.Tests -c Release -- --application-discovery`. It prints counts only. These checks do not establish live foreground switching for every installed application.
