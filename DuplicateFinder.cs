using System.Collections.Concurrent;
using System.Numerics;
using CoenM.ImageHash.HashAlgorithms;
using SixLabors.ImageSharp.PixelFormats;

namespace A_Image_Duplicate_Finder;

public class DuplicateFinder
{
    public static readonly HashSet<string> DefaultExtensions = new(StringComparer.OrdinalIgnoreCase) 
    { 
        ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tiff" 
    };

    public event Action<int, int>? ProgressChanged;
    public event Action<string>? LogMessage;

    public List<string> GetImageFiles(string folder, string excludeFolder, HashSet<string> allowedExtensions)
    {
        if (!Directory.Exists(folder)) return new List<string>();

        return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(file => allowedExtensions.Contains(Path.GetExtension(file)) && !file.Contains(excludeFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public ConcurrentBag<(string File, ulong Hash)> ComputeHashes(List<string> files, CancellationToken ct)
    {
        var hasher = new PerceptualHash();
        var fileHashes = new ConcurrentBag<(string File, ulong Hash)>();
        int total = files.Count;
        int processed = 0;

        var options = new ParallelOptions 
        { 
            CancellationToken = ct,
            MaxDegreeOfParallelism = Environment.ProcessorCount 
        };

        try
        {
            Parallel.ForEach(files, options, (file, state) =>
            {
                if (ct.IsCancellationRequested)
                {
                    state.Stop();
                    return;
                }

                try
                {
                    using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(file);
                    ulong hash = hasher.Hash(image);
                    fileHashes.Add((file, hash));
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Error at {Path.GetFileName(file)}: {ex.Message}");
                }
                finally
                {
                    int current = Interlocked.Increment(ref processed);
                    ProgressChanged?.Invoke(current, total);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Canceled by user
        }

        return fileHashes;
    }

    public (List<(string File, ulong Hash)> Unique, List<string> Duplicates) FindDuplicates(IEnumerable<(string File, ulong Hash)> fileHashes, int maxDistance)
    {
        var unique = new List<(string File, ulong Hash)>();
        var duplicates = new List<string>();
        var sortedHashes = fileHashes.OrderBy(x => x.Hash).ToList();

        foreach (var item in sortedHashes)
        {
            bool isDuplicate = false;
            // Wir suchen in der Liste der Unique-Bilder
            foreach (var entry in unique)
            {
                if (BitOperations.PopCount(item.Hash ^ entry.Hash) <= maxDistance)
                {
                    isDuplicate = true;
                    duplicates.Add(item.File);
                    LogMessage?.Invoke($"Duplicate: {Path.GetFileName(item.File)} ≈ {Path.GetFileName(entry.File)}");
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

    public void MoveDuplicates(List<string> duplicates, string duplicateFolder)
    {
        if (duplicates.Count == 0) return;
        
        Directory.CreateDirectory(duplicateFolder);
        foreach (var file in duplicates)
        {
            try
            {
                if (!File.Exists(file)) continue;

                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(duplicateFolder, fileName);
                
                if (File.Exists(destFile))
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    destFile = Path.Combine(duplicateFolder, $"{nameWithoutExt}_{Guid.NewGuid().ToString().AsSpan(0, 8)}{ext}");
                }
                
                File.Move(file, destFile);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"ERROR: Could not move {file}: {ex.Message}");
            }
        }
    }

}
