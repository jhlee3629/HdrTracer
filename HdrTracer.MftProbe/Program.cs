using System.Diagnostics;
using HdrTracer.Core;
namespace HdrTracer.MftProbe;
internal static class Program
{
    static int Main(string[] args)
    {
        var drive = args.Length > 0 ? args[0].ToUpperInvariant() : "C";
        if (drive.Length == 1) drive = drive + ":";
        Console.WriteLine($"[HdrTracer.MftProbe] Indexing {drive} ...");
        FileIndex index;
        try
        {
            var sw = Stopwatch.StartNew();
            // raw MFT 직접 읽기 (메인 앱과 동일한 인덱서). LinkParents는 내부에서 수행됨.
            var (idx, _, _) = RawMftReader.BuildIndexWithJournalInfo(drive);
            index = idx;
            sw.Stop();
            Console.WriteLine($"  Indexed {index.Count:N0} entries in {sw.ElapsedMilliseconds:N0} ms.");
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("ERROR: Run as Administrator.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
        var engine = new SearchEngine();
        UsnJournalMonitor? monitor = null;
        try
        {
            monitor = new UsnJournalMonitor(index, drive);
            monitor.Start();
            Console.WriteLine("USN Journal monitor started. Index will auto-update.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not start USN monitor: {ex.Message}");
        }
        Console.WriteLine();
        Console.WriteLine("Path reconstruction sanity check (5 random entries):");
        var rng = new Random(42);
        for (int n = 0; n < 5; n++)
        {
            int idx = rng.Next(index.Count);
            Console.WriteLine($"  {(index.IsDirectory(idx) ? "[D]" : "   ")} {index.GetFullPath(idx)}");
        }
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  (search text)  = search");
        Console.WriteLine("  :stats         = show USN monitor stats");
        Console.WriteLine("  :quit          = exit (empty line also works)");
        Console.WriteLine();
        while (true)
        {
            Console.Write("> ");
            var query = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(query)) break;
            if (query == ":quit") break;
            if (query == ":stats")
            {
                if (monitor is null)
                {
                    Console.WriteLine("  Monitor not running.");
                }
                else
                {
                    Console.WriteLine($"  Events processed : {monitor.EventsProcessed:N0}");
                    Console.WriteLine($"  Entries added    : {monitor.EntriesAdded:N0}");
                    Console.WriteLine($"  Entries removed  : {monitor.EntriesRemoved:N0}");
                    Console.WriteLine($"  Entries renamed  : {monitor.EntriesRenamed:N0}");
                    Console.WriteLine($"  Current index    : {index.Count:N0} entries");
                }
                Console.WriteLine();
                continue;
            }
            var sw = Stopwatch.StartNew();
            List<SearchHit> hits;
            var indexList = new[] { index };
            lock (index)
            {
                hits = engine.Search(indexList, query, maxResults: 50);
            }
            sw.Stop();
            Console.WriteLine($"  {hits.Count} hits in {sw.ElapsedMilliseconds} ms (showing up to 50)");
            foreach (var hit in hits)
            {
                Console.WriteLine($"    {(hit.Index.IsDirectory(hit.EntryIndex) ? "[D]" : "   ")} {hit.Index.GetFullPath(hit.EntryIndex)}");
            }
            Console.WriteLine();
        }
        monitor?.Dispose();
        return 0;
    }
}
