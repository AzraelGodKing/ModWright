using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;

namespace SteamCmdHelper.UiTests;

/// <summary>Clicks through The Drop Pod in the real window, the way a player would.</summary>
public sealed class DropPodClickTests
{
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
    readonly ITestOutputHelper _out;

    public DropPodClickTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Activating_a_needed_mod_and_saving_writes_ModsConfig()
    {
        using var setup = new FakeSetup();
        Run(setup, (window, cf) =>
        {
            var mods = WaitFor(() => window.FindFirstDescendant(cf.ByName("Installed mods; ticked mods are active, in load order")), "the mod list");
            WaitUntil(() => mods.FindAllChildren(cf.ByControlType(ControlType.ListItem)).Length >= 3, "three mods in the list");
            var problems = WaitFor(() => window.FindFirstDescendant(cf.ByName("Problems with the active mods")), "the problems list");
            WaitUntil(() => Texts(problems).Any(t => t.Contains("Test Mod B") && t.Contains("not active")), "the inactive dependency in Problems");

            Click(window, cf, "Activate needed mods");
            Click(window, cf, "Save to game");
            WaitUntil(() => File.ReadAllText(setup.ModsConfig).Contains("test.modb"), "test.modb in ModsConfig.xml");
            WaitUntil(() => !Texts(problems).Any(t => t.Contains("Test Mod B")), "the problem gone");
        });
    }

    [Fact]
    public void Switching_to_Sun_Haven_lists_its_plugins_and_saving_moves_the_dependency_in()
    {
        using var setup = new FakeSetup();
        Run(setup, (window, cf) =>
        {
            var game = WaitFor(() => window.FindFirstDescendant(cf.ByName("Game").And(cf.ByControlType(ControlType.ComboBox)))?.AsComboBox(), "the game picker");
            try { game.Select("Sun Haven"); }
            catch (Exception e)
            {
                // The drop-down's items aren't always reachable through UI Automation; the keyboard always works.
                _out.WriteLine("Select failed, using the keyboard: " + e.Message);
                game.Focus();
                Keyboard.Press(VirtualKeyShort.DOWN);
            }
            var mods = WaitFor(() => window.FindFirstDescendant(cf.ByName("Installed mods; ticked mods are active, in load order")), "the mod list");
            WaitUntil(() => mods.FindAllChildren(cf.ByControlType(ControlType.ListItem)).Select(i => i.Name).Contains("Farming"), "the Farming plugin listed");
            var problems = WaitFor(() => window.FindFirstDescendant(cf.ByName("Problems with the active mods")), "the problems list");
            WaitUntil(() => Texts(problems).Any(t => t.Contains("Lib") && t.Contains("not active")), "the disabled dependency in Problems");

            Click(window, cf, "Activate needed mods");
            Click(window, cf, "Save to game");
            WaitUntil(() => Directory.Exists(Path.Combine(setup.SunHaven, "BepInEx", "plugins", "Author-Lib")), "Author-Lib moved into BepInEx\\plugins");
        });
    }

    void Run(FakeSetup setup, Action<Window, ConditionFactory> test)
    {
        // Our own Process, so the app is always ended here: a copy left running would make the next test's start hand
        // over to it (the app allows one copy at a time) and exit.
        EndTestCopies();
        using var process = Process.Start(new ProcessStartInfo(setup.Exe) { WorkingDirectory = setup.App, UseShellExecute = false })
            ?? throw new InvalidOperationException("The app did not start.");
        try { process.WaitForInputIdle(15000); } catch (InvalidOperationException) { } // already gone; said below
        if (process.HasExited) throw new InvalidOperationException($"The app exited at once (code {process.ExitCode}); another copy may still be running.");
        var app = Application.Attach(process);
        using var automation = new UIA3Automation();
        try
        {
            var window = app.GetMainWindow(automation, Wait) ?? throw new TimeoutException("The main window did not open.");
            _out.WriteLine("Window: " + window.Title);
            test(window, automation.ConditionFactory);
        }
        catch (Exception e)
        {
            _out.WriteLine("Failed: " + e);
            try { _out.WriteLine(process.HasExited ? $"The app had exited (code {process.ExitCode})." : Dump(app.GetMainWindow(automation, TimeSpan.FromSeconds(2)))); } catch { }
            throw;
        }
        finally
        {
            try { if (!process.HasExited) { process.CloseMainWindow(); process.WaitForExit(5000); } } catch { }
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); } } catch { }
            EndTestCopies();
        }
    }

    /// <summary>Ends any copy of the app started from a test folder (one left behind would take over the next start).</summary>
    static void EndTestCopies()
    {
        var prefix = Path.Combine(Path.GetTempPath(), FakeSetup.FolderPrefix);
        foreach (var p in Process.GetProcessesByName("SteamCmdHelper"))
            using (p)
            {
                try
                {
                    if (p.MainModule?.FileName is not { } file || !file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
    }

    static void Click(Window window, ConditionFactory cf, string name)
    {
        var b = WaitFor(() => window.FindFirstDescendant(cf.ByName(name).And(cf.ByControlType(ControlType.Button)))?.AsButton(), name);
        WaitUntil(() => b.IsEnabled, name + " enabled");
        b.Invoke();
    }

    static IEnumerable<string> Texts(AutomationElement list) =>
        list.FindAllDescendants().Select(e => e.Properties.Name.ValueOrDefault ?? "").Where(s => s.Length > 0);

    static T WaitFor<T>(Func<T?> find, string what) where T : class =>
        Retry.WhileNull(find, Wait, TimeSpan.FromMilliseconds(250)).Result ?? throw new TimeoutException("Not found: " + what);

    static void WaitUntil(Func<bool> check, string what)
    {
        if (!Retry.WhileFalse(() => { try { return check(); } catch { return false; } }, Wait, TimeSpan.FromMilliseconds(250)).Result)
            throw new TimeoutException("Timed out waiting for " + what);
    }

    /// <summary>The window's buttons, lists and texts, to see what was there when a test failed.</summary>
    static string Dump(Window? w) =>
        w == null ? "(no window)" : string.Join("\n", w.FindAllDescendants().Take(400).Select(e => $"{e.ControlType}: {e.Properties.Name.ValueOrDefault}"));
}
