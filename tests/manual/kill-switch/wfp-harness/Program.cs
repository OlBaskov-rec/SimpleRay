using SimpleRay.App.Engine;

// Standalone kill-switch tester. Run ELEVATED. This isolates WfpKillSwitch from the GUI,
// so if the WFP layer misbehaves it only affects this console — the app is untouched.
//
//   dotnet run --project tests/manual/kill-switch/wfp-harness -- [tunName] [singBoxExePath]
//
// It engages the kill-switch, waits (so you can test blocking in another window), then
// disengages on Enter. If a step throws, the exception is printed — send that back.

string tun = args.Length > 0 ? args[0] : "simpleray";
string exe = args.Length > 1 ? args[1] : @"C:\Progs\SimpleRay\SimpleRay-0.2.0-win-x64\core\sing-box.exe";

Console.WriteLine("=== SimpleRay WFP kill-switch harness ===");
Console.WriteLine($"TUN interface : {tun}");
Console.WriteLine($"Allowed exe   : {exe}  (exists: {File.Exists(exe)})");
Console.WriteLine();

Console.WriteLine("[1/3] CleanupLeftovers() — remove filters from any previous run...");
try { WfpKillSwitch.CleanupLeftovers(); Console.WriteLine("      ok"); }
catch (Exception e) { Console.WriteLine("      FAILED: " + e); return; }

var ks = new WfpKillSwitch();

Console.WriteLine("[2/3] Engage() — add the block + permit filters (WFP)...");
try
{
    ks.Engage(tun, exe);
    Console.WriteLine($"      ok, IsEngaged={ks.IsEngaged}");
}
catch (Exception e)
{
    Console.WriteLine("      Engage FAILED (send this):");
    Console.WriteLine(e);
    return;
}

Console.WriteLine();
Console.WriteLine("KILL-SWITCH IS ENGAGED. In ANOTHER window, verify:");
Console.WriteLine("   curl.exe -m 5 https://example.com     # blocked unless routed via TUN/sing-box");
Console.WriteLine("   netsh wfp show filters               # look for 'SimpleRay kill-switch'");
Console.WriteLine();
Console.WriteLine("If your whole network died, that's expected while engaged — pressing Enter");
Console.WriteLine("(or closing this window / rebooting) removes the filters.");
Console.WriteLine();
Console.Write("Press Enter to Disengage and exit... ");
Console.ReadLine();

Console.WriteLine("[3/3] Disengage()...");
try { ks.Disengage(); Console.WriteLine($"      ok, IsEngaged={ks.IsEngaged}"); }
catch (Exception e) { Console.WriteLine("      Disengage FAILED: " + e); }
