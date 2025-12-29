using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace A_Image_Duplicate_Finder;

public partial class MainForm : Form
{
    private TextBox txtFolderPath = null!;
    private Button btnBrowse = null!;
    private Button btnScan = null!;
    private Button btnCancel = null!;
    private ProgressBar progressBar = null!;
    private ListBox lstLog = null!;
    private Label lblStatus = null!;
    private Button btnMove = null!;
    private Button btnOpenDuplicates = null!;
    private FlowLayoutPanel pnlExtensions = null!;
    private List<CheckBox> extensionCheckBoxes = new List<CheckBox>();
    
    private List<string> duplicatesFound = new List<string>();
    private string? currentFolder;
    private string? duplicateFolder;
    private readonly DuplicateFinder finder = new DuplicateFinder();
    private CancellationTokenSource? cts;

    public MainForm()
    {
        InitializeComponentManual();
        finder.ProgressChanged += (current, total) => {
            if (IsDisposed) return;
            this.Invoke(() => {
                progressBar.Maximum = total;
                progressBar.Value = current;
                lblStatus.Text = $"Processing {current} of {total} images...";
            });
        };
        finder.LogMessage += (msg) => {
            if (IsDisposed) return;
            this.Invoke(() => {
                lstLog.Items.Add(msg);
                lstLog.TopIndex = lstLog.Items.Count - 1;
            });
        };
    }

    private void InitializeComponentManual()
    {
        this.Text = "Media Duplicate Finder";
        this.Size = new Size(600, 500);
        this.StartPosition = FormStartPosition.CenterScreen;

        Label lblFolder = new Label { Text = "Folder to scan:", Location = new Point(10, 15), Width = 150 };
        txtFolderPath = new TextBox { Location = new Point(10, 40), Width = 460, ReadOnly = true };
        btnBrowse = new Button { Text = "Browse...", Location = new Point(480, 38), Width = 90 };
        btnBrowse.Click += BtnBrowse_Click;

        Label lblExt = new Label { Text = "Extensions:", Location = new Point(10, 75), Width = 80 };
        pnlExtensions = new FlowLayoutPanel { Location = new Point(90, 70), Width = 480, Height = 30 };
        foreach (var ext in DuplicateFinder.DefaultExtensions)
        {
            var cb = new CheckBox { Text = ext, Checked = true, AutoSize = true };
            extensionCheckBoxes.Add(cb);
            pnlExtensions.Controls.Add(cb);
        }

        btnScan = new Button { Text = "Start Scan", Location = new Point(10, 110), Width = 120, Enabled = false };
        btnScan.Click += BtnScan_Click;

        btnCancel = new Button { Text = "Cancel", Location = new Point(140, 110), Width = 100, Enabled = false };
        btnCancel.Click += (s, e) => cts?.Cancel();

        progressBar = new ProgressBar { Location = new Point(250, 110), Width = 320, Height = 23 };
        lblStatus = new Label { Text = "Ready", Location = new Point(10, 140), Width = 560 };

        lstLog = new ListBox { Location = new Point(10, 170), Width = 560, Height = 170 };

        btnMove = new Button { Text = "Move Duplicates", Location = new Point(10, 350), Width = 180, Enabled = false };
        btnMove.Click += BtnMove_Click;

        btnOpenDuplicates = new Button { Text = "Open Duplicates Folder", Location = new Point(200, 350), Width = 180, Enabled = false };
        btnOpenDuplicates.Click += BtnOpenDuplicates_Click;

        this.Controls.AddRange(new Control[] { lblFolder, txtFolderPath, btnBrowse, lblExt, pnlExtensions, btnScan, btnCancel, progressBar, lblStatus, lstLog, btnMove, btnOpenDuplicates });
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            txtFolderPath.Text = dialog.SelectedPath;
            currentFolder = dialog.SelectedPath;
            duplicateFolder = Path.Combine(currentFolder, "duplicates");
            btnScan.Enabled = true;
            btnMove.Enabled = false;
            btnOpenDuplicates.Enabled = false;
            lstLog.Items.Clear();
            duplicatesFound.Clear();
            lblStatus.Text = "Folder selected. Ready to scan.";
        }
    }

    private async void BtnScan_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(currentFolder)) return;

        btnScan.Enabled = false;
        btnBrowse.Enabled = false;
        btnCancel.Enabled = true;
        btnMove.Enabled = false;
        btnOpenDuplicates.Enabled = false;
        lstLog.Items.Clear();
        duplicatesFound.Clear();
        progressBar.Value = 0;
        
        cts = new CancellationTokenSource();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var selectedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cb in extensionCheckBoxes)
            {
                if (cb.Checked) selectedExtensions.Add(cb.Text);
            }

            if (selectedExtensions.Count == 0)
            {
                MessageBox.Show("Please select at least one file extension.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            lblStatus.Text = "Searching for images...";
            var files = await Task.Run(() => finder.GetImageFiles(currentFolder, duplicateFolder!, selectedExtensions), cts.Token);
            
            if (files.Count == 0)
            {
                lblStatus.Text = "No images found.";
                return;
            }

            lblStatus.Text = $"{files.Count} images found. Calculating hashes...";
            var fileHashes = await Task.Run(() => finder.ComputeHashes(files, cts.Token), cts.Token);

            if (cts.Token.IsCancellationRequested) return;

            lblStatus.Text = "Comparing images...";
            var result = await Task.Run(() => finder.FindDuplicates(fileHashes, 3), cts.Token);
            
            watch.Stop();
            duplicatesFound = result.Duplicates;
            lblStatus.Text = $"Scan complete ({watch.Elapsed.TotalSeconds:F1}s). {result.Unique.Count} unique images, {duplicatesFound.Count} duplicates found.";

            if (duplicatesFound.Count > 0)
            {
                btnMove.Enabled = true;
            }
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "Scan canceled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during scan: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            lblStatus.Text = "An error occurred.";
        }
        finally
        {
            btnScan.Enabled = true;
            btnBrowse.Enabled = true;
            btnCancel.Enabled = false;
            cts?.Dispose();
            cts = null;
        }
    }

    private void BtnMove_Click(object? sender, EventArgs e)
    {
        if (duplicatesFound.Count == 0 || string.IsNullOrEmpty(duplicateFolder)) return;

        try
        {
            finder.MoveDuplicates(duplicatesFound, duplicateFolder);
            lblStatus.Text = $"{duplicatesFound.Count} duplicates moved to '{duplicateFolder}'.";
            btnMove.Enabled = false;
            btnOpenDuplicates.Enabled = true;
            MessageBox.Show("Duplicates moved successfully.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error while moving: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnOpenDuplicates_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(duplicateFolder) || !Directory.Exists(duplicateFolder)) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
        {
            FileName = duplicateFolder,
            UseShellExecute = true,
            Verb = "open"
        });
    }
}
