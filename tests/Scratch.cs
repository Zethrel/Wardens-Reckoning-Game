// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Tests;

/// <summary>
/// One folder in the system's temporary folder for everything a test run writes (copies of the fixture world with
/// changed settings, saves), deleted when the run ends, or else by the next run. Without it every run left its copies
/// behind, and they add up: each copy of the fixture world is about 3 MB, a run makes over a hundred.
/// </summary>
static class Scratch
{
    public static readonly string Root = Make();

    static string Make()
    {
        // The test host does not always run the clean-up below when it ends, so each run first deletes what earlier
        // runs left: folders untouched for half an hour (a run going on at the same time keeps its own).
        foreach (string old in Directory.GetDirectories(Path.GetTempPath(), "wr-tests-*"))
            try
            {
                if (Directory.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddMinutes(-30))
                    Directory.Delete(old, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        string root = Path.Combine(Path.GetTempPath(), "wr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A file still open: the folder stays, and the system's own clean-up takes it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        };
        return root;
    }
}
