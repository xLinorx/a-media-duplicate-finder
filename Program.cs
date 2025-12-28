using System.Collections.Concurrent;
using System.Numerics;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using SixLabors.ImageSharp.PixelFormats;

class Program
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tiff" };

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool exitProgram = false;
        while (!exitProgram)
        {
            Console.Clear();
            Console.WriteLine("=== Bild-Duplikat-Finder ===");
            string? imageFolder = SelectFolder("Bitte den Bilderordner auswählen");
            if (string.IsNullOrEmpty(imageFolder))
            {
                Console.WriteLine("Kein Ordner ausgewählt.");
                goto Menu;
            }

            string duplicateFolder = Path.Combine(imageFolder, "duplicates");

            var files = GetImageFiles(imageFolder, duplicateFolder);
            Console.WriteLine($"Gefunden: {files.Count} Bilder. Starte Analyse...");

            var fileHashes = ComputeHashes(files);
            var (unique, duplicates) = FindDuplicates(fileHashes, maxDistance: 3);

            Console.WriteLine($"Analyse beendet. {unique.Count} eindeutige Bilder, {duplicates.Count} Duplikate gefunden.");

            if (duplicates.Count > 0)
            {
                Directory.CreateDirectory(duplicateFolder);
                MoveDuplicates(duplicates, duplicateFolder);
                Console.WriteLine($"{duplicates.Count} Duplikate nach '{duplicateFolder}' verschoben.");

                Console.WriteLine("\nScan abgeschlossen.");
                Console.Write("Soll der Duplikat-Ordner geöffnet werden? (j/n): ");
                string? response = Console.ReadLine();
                if (response?.Trim().ToLower() == "j")
                {
                    OpenFolder(duplicateFolder);
                }
            }
            else
            {
                Console.WriteLine("\nKeine Duplikate gefunden. Scan abgeschlossen.");
            }

            Menu:
            Console.WriteLine("\nWie möchten Sie fortfahren?");
            Console.WriteLine("1. Neuer Scan");
            Console.WriteLine("2. Programm beenden");
            Console.Write("Auswahl: ");

            string? choice = Console.ReadLine();
            if (choice == "2")
            {
                exitProgram = true;
            }
            else if (choice != "1")
            {
                // Bei ungültiger Eingabe standardmäßig beenden oder erneut fragen?
                // Der Nutzer fragte explizit nach 1 und 2.
                exitProgram = true;
            }
        }
    }

    static List<string> GetImageFiles(string folder, string excludeFolder)
    {
        return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(file => IsImage(file) && !file.Contains(excludeFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    static ConcurrentBag<(string File, ulong Hash)> ComputeHashes(List<string> files)
    {
        var hasher = new PerceptualHash();
        var fileHashes = new ConcurrentBag<(string File, ulong Hash)>();
        int total = files.Count;
        int processed = 0;

        Parallel.ForEach(files, file =>
        {
            try
            {
                using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(file);
                ulong hash = hasher.Hash(image);
                fileHashes.Add((file, hash));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nFehler bei {Path.GetFileName(file)}: {ex.Message}");
            }
            finally
            {
                int current = Interlocked.Increment(ref processed);
                if (current % 10 == 0 || current == total)
                {
                    Console.Write($"\rFortschritt: {current}/{total} Bilder verarbeitet...");
                }
            }
        });

        Console.WriteLine();
        return fileHashes;
    }

    static (List<(string File, ulong Hash)> Unique, List<string> Duplicates) FindDuplicates(IEnumerable<(string File, ulong Hash)> fileHashes, int maxDistance)
    {
        var unique = new List<(string File, ulong Hash)>();
        var duplicates = new List<string>();

        foreach (var item in fileHashes)
        {
            bool isDuplicate = false;
            foreach (var entry in unique)
            {
                if (BitOperations.PopCount(item.Hash ^ entry.Hash) <= maxDistance)
                {
                    isDuplicate = true;
                    duplicates.Add(item.File);
                    Console.WriteLine($"Duplikat: {Path.GetFileName(item.File)} ≈ {Path.GetFileName(entry.File)}");
                    break;
                }
            }

            if (!isDuplicate)
            {
                unique.Add(item);
            }
        }

        return (unique, duplicates);
    }

    static void MoveDuplicates(List<string> duplicates, string duplicateFolder)
    {
        foreach (var file in duplicates)
        {
            try
            {
                string destFile = Path.Combine(duplicateFolder, Path.GetFileName(file));
                if (File.Exists(destFile))
                {
                    destFile = Path.Combine(duplicateFolder, Guid.NewGuid().ToString() + Path.GetExtension(file));
                }
                File.Move(file, destFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Verschieben von {file}: {ex.Message}");
            }
        }
    }

    static void OpenFolder(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
            {
                FileName = folderPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
    }

    static string? SelectFolder(string description)
    {
        Console.WriteLine("Öffne Ordnerauswahl-Dialog...");

        string? selectedPath = null;

        // Wir erstellen einen unsichtbaren Wrapper-Thread für den Dialog, 
        // um sicherzustellen, dass er im STA-Modus läuft und einen Besitzer hat.
        var thread = new Thread(() =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = description,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            // Ein unsichtbares Formular erstellen, um den Dialog in den Vordergrund zu zwingen
            using var dummyForm = new Form
            {
                TopMost = true,
                Visible = false,
                ShowInTaskbar = false,
                WindowState = FormWindowState.Normal
            };

            // Das Handle der Konsole als Besitzer verwenden
            IntPtr consoleHandle = GetConsoleWindow();
            if (consoleHandle != IntPtr.Zero)
            {
                SetForegroundWindow(consoleHandle);
            }

            IWin32Window? owner = consoleHandle != IntPtr.Zero ? new WindowWrapper(consoleHandle) : dummyForm;
            
            // Dummy Form handle erzwingen und in den Vordergrund bringen
            _ = dummyForm.Handle;
            SetForegroundWindow(dummyForm.Handle);

            if (dialog.ShowDialog(owner) == DialogResult.OK)
            {
                selectedPath = dialog.SelectedPath;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return selectedPath;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private class WindowWrapper : IWin32Window
    {
        public WindowWrapper(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }
    }

    static bool IsImage(string file)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        return ImageExtensions.Contains(ext);
    }
}