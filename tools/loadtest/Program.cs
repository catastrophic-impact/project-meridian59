using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Bots;

// usage: dotnet Bots.dll [--count N] [--minutes M (0 = until stopped)] [--stagger SECONDS] [--host H] [--port P] [--rows] [--verbose]
//        --rows prints machine-readable SUM|... and BOT|... lines every 5 s (used by the admin TUI)
//        dotnet Bots.dll --user U --pw P ...        (single account, no accounts.txt)
// Bot accounts come from bots/accounts.txt (made by bots/provision.py); characters are created on first login.
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
string host = "127.0.0.1", user = null, pw = "m59test";
ushort port = 5959; double minutes = 10, stagger = 0.5; int count = 1; bool rows = false;
string rsb = Path.Combine(root, "server/rsc0000.rsb"), rooms = Path.Combine(root, "server/rooms");
for (int i = 0; i < args.Length; i++)
    switch (args[i])
    {
        case "--host": host = args[++i]; break;
        case "--port": port = ushort.Parse(args[++i]); break;
        case "--user": user = args[++i]; break;
        case "--pw": pw = args[++i]; break;
        case "--count": count = int.Parse(args[++i]); break;
        case "--minutes": minutes = double.Parse(args[++i]); break;
        case "--stagger": stagger = double.Parse(args[++i]); break;
        case "--verbose": Bot.Verbose = true; break;
        case "--rows": rows = true; break;
    }

var accounts = new List<(string u, string p)>();
if (user != null) accounts.Add((user, pw));
else
    foreach (var line in File.ReadAllLines(Path.Combine(root, "bots/accounts.txt")).Take(count))
    { var a = line.Split(':'); accounts.Add((a[0], a[1])); }

string[] syl = { "al", "bar", "cor", "dan", "el", "fen", "gor", "hal", "ir", "jor", "kel", "lun", "mor", "nar", "or", "pel", "quin", "ros", "sil", "tor", "ul", "vek", "wyn", "yar", "zel" };
string CharName(int n) => char.ToUpper(syl[n % 25][0]) + syl[n % 25].Substring(1) + syl[(n / 25) % 25] + syl[(n / 625) % 25];

var bots = new List<Bot>();
var proc = Process.GetCurrentProcess();
var start = DateTime.UtcNow; var end = minutes > 0 ? start.AddMinutes(minutes) : DateTime.MaxValue;
bool stopRequested = false;     // SIGINT / SIGTERM: log the bots out cleanly instead of just dying
using var sigInt = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGINT, c => { c.Cancel = true; stopRequested = true; });
using var sigTerm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, c => { c.Cancel = true; stopRequested = true; });
double reportEvery = rows ? 5 : 30;
var nextReport = start.AddSeconds(reportEvery); var nextLaunch = start; int launched = 0;
TimeSpan cpu0 = proc.TotalProcessorTime; DateTime wall0 = start;

while (DateTime.UtcNow < end && !stopRequested)
{
    var now = DateTime.UtcNow;
    if (launched < accounts.Count && now >= nextLaunch)
    {
        var (u, p) = accounts[launched];
        var b = new Bot(u, rsb, rooms, Environment.TickCount + launched);
        b.Connect(host, port, u, p, user == null ? CharName(launched + 1) : null);
        bots.Add(b); launched++; nextLaunch = now.AddSeconds(stagger);
    }
    foreach (var b in bots) b.Tick();
    if (now >= nextReport)
    {
        proc.Refresh();
        double cpuPct = (proc.TotalProcessorTime - cpu0).TotalSeconds / (now - wall0).TotalSeconds * 100;
        cpu0 = proc.TotalProcessorTime; wall0 = now;
        int playing = bots.Count(b => b.Ready), failed = bots.Count(b => b.Client.State == GameClient.Phase.Failed);
        if (rows)
        {
            Console.WriteLine($"SUM|{bots.Count}|{playing}|{failed}|{bots.Sum(b => b.MovesSent)}|{bots.Sum(b => b.ChatsSent)}|{cpuPct:F0}|{proc.WorkingSet64 / 1048576}");
            foreach (var b in bots) Console.WriteLine($"BOT|{b.Name}|{b.Client.CharacterName}|{b.Client.State}|{b.RoomsEntered}|{b.MovesSent}|{b.ChatsSent}");
        }
        else
        Console.WriteLine($"[{(now - start).TotalSeconds,5:F0}s] bots={bots.Count} playing={playing} failed={failed} moves={bots.Sum(b => b.MovesSent)} chats={bots.Sum(b => b.ChatsSent)} botCPU={cpuPct:F0}% botMem={proc.WorkingSet64 / 1048576}MB");
        foreach (var b in bots.Where(b => b.Client.State == GameClient.Phase.Failed)) Console.WriteLine("  FAILED " + b.Summary());
        nextReport = now.AddSeconds(reportEvery);
    }
    Thread.Sleep(50);
}
foreach (var b in bots) Console.WriteLine("FINAL " + b.Summary());
foreach (var b in bots) b.Client.Disconnect();
Thread.Sleep(300);          // let the disconnects flush
Environment.Exit(0);
