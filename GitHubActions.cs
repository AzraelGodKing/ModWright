namespace SteamCmdHelper;

/// <summary>
/// Sets a mods repo up to upload from GitHub Actions: a workflow file that installs SteamCMD and this program on a
/// Windows runner, restores SteamCMD's saved login from a repository secret, and runs <c>upload --changed</c> when a
/// version tag is pushed. The saved login is SteamCMD's config/config.vdf, which <see cref="LoginSecret"/> reads.
/// </summary>
public static class GitHubActions
{
    public const string WorkflowPath = ".github/workflows/steam-workshop-upload.yml";
    public const string AccountSecret = "STEAM_ACCOUNT";
    public const string LoginSecretName = "STEAM_CONFIG_VDF";

    public const string Workflow = """
        # Uploads this repo's mods to the Steam Workshop with SteamCMD Helper when a tag starting with "v" is pushed,
        # or when started by hand from the Actions tab. Only mods whose version Steam does not have yet are uploaded.
        # Written by SteamCMD Helper (ci-setup).
        #
        # It needs two repository secrets (Settings > Secrets and variables > Actions):
        #   STEAM_ACCOUNT     the Steam login name that owns the mods
        #   STEAM_CONFIG_VDF  SteamCMD's saved login: run "SteamCmdHelper login-secret" (or Options > Git & GitHub > Copy login
        #                     secret) on the PC where you logged in, and paste what it gives you
        # Anyone who can push workflow changes to this repo can use that login, so keep the repo's write access tight.
        # A saved login expires now and then. When uploads fail with a login error, log in again in the app and update
        # STEAM_CONFIG_VDF.
        name: Upload mods to the Steam Workshop

        on:
          push:
            tags: ['v*']
          workflow_dispatch:
            inputs:
              dry_run:
                description: Check and package only, upload nothing
                type: boolean
                default: false

        # Never two uploads at once.
        concurrency:
          group: steam-workshop-upload
          cancel-in-progress: false

        jobs:
          upload:
            runs-on: windows-latest
            steps:
              - uses: actions/checkout@v4

              - name: Get SteamCMD Helper
                env:
                  GH_TOKEN: ${{ github.token }}
                run: gh release download --repo AzraelGodKing/SteamCMDHelper --pattern "*-win-x64.exe" --output "$env:RUNNER_TEMP\SteamCmdHelper.exe"

              - name: Get SteamCMD
                run: |
                  $dir = "$env:RUNNER_TEMP\steamcmd"
                  New-Item -ItemType Directory -Force $dir | Out-Null
                  Invoke-WebRequest https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip -OutFile "$dir\steamcmd.zip"
                  Expand-Archive "$dir\steamcmd.zip" $dir -Force
                  # The first run updates SteamCMD and exits with 7, which is fine.
                  & "$dir\steamcmd.exe" +quit | Out-Host
                  exit 0

              - name: Restore the saved Steam login
                env:
                  STEAM_CONFIG_VDF: ${{ secrets.STEAM_CONFIG_VDF }}
                run: |
                  $config = "$env:RUNNER_TEMP\steamcmd\config"
                  New-Item -ItemType Directory -Force $config | Out-Null
                  [IO.File]::WriteAllBytes("$config\config.vdf", [Convert]::FromBase64String($env:STEAM_CONFIG_VDF))

              - name: Upload
                env:
                  STEAM_ACCOUNT: ${{ secrets.STEAM_ACCOUNT }}
                  DRY_RUN: ${{ inputs.dry_run }}
                run: |
                  $a = @('upload', '--changed', '--repo', '.', '--account', $env:STEAM_ACCOUNT, '--steamcmd', "$env:RUNNER_TEMP\steamcmd\steamcmd.exe")
                  if ($env:DRY_RUN -eq 'true') { $a += '--dry-run' }
                  # Piping makes PowerShell wait for the program and show its output.
                  & "$env:RUNNER_TEMP\SteamCmdHelper.exe" @a | Out-Host
                  exit $LASTEXITCODE

              - name: Remove the saved login
                if: always()
                run: Remove-Item -Force -ErrorAction SilentlyContinue "$env:RUNNER_TEMP\steamcmd\config\config.vdf"
        """;

    /// <summary>What to do after the workflow is written, in plain words.</summary>
    public const string NextSteps = $"""
        Next steps:
          1. In the repo on GitHub, open Settings > Secrets and variables > Actions and add two repository secrets:
             {AccountSecret}     your Steam login name
             {LoginSecretName}  SteamCMD's saved login (SteamCmdHelper login-secret, or Options > Git & GitHub > Copy login secret)
          2. Commit and push {WorkflowPath}.
          3. Push a tag starting with v (git tag v1.2.0, then git push --tags), or start it from the Actions tab.
        """;

    /// <summary>
    /// Writes the workflow into the repo. Returns its path. Refuses to replace an existing file unless
    /// <paramref name="overwrite"/>, so a changed workflow is not lost.
    /// </summary>
    public static string Write(string repo, bool overwrite = false)
    {
        var path = Path.Combine(repo, ProjectConfig.Norm(WorkflowPath));
        if (File.Exists(path) && !overwrite) throw new IOException($"{WorkflowPath} already exists. Delete it first to write a fresh one.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Workflow.Replace("\r\n", "\n") + "\n");
        return path;
    }

    /// <summary>Where SteamCMD keeps its saved login.</summary>
    public static string ConfigFile(string steamCmd) => Path.Combine(Path.GetDirectoryName(steamCmd) ?? "", "config", "config.vdf");

    /// <summary>
    /// SteamCMD's saved login, base64-encoded for a GitHub secret, or null when there is none yet (log in once first).
    /// Whoever has this text can use the Steam login it holds until it expires, so it must only go into a secret.
    /// </summary>
    public static string? LoginSecret(string steamCmd)
    {
        var path = ConfigFile(steamCmd);
        try { return File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : null; }
        catch (IOException) { return null; }
    }
}
