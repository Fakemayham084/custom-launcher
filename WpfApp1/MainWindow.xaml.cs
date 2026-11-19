using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace WpfApp1
{
    public enum State
    {
        None = 0,
        Updating,
        Checking,
        Updated
    }

    public partial class MainWindow : Window
    {
        private State currentState = State.None;
        private static readonly HttpClient httpClient = new HttpClient();

        private const string VersionUrl = "https://raw.githubusercontent.com/Fakemayham084/test/main/version.txt";
        private const string UpdateZipUrl = "https://raw.githubusercontent.com/Fakemayham084/test/main/test.zip";
        private const string LocalVersionFile = "version.txt";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateLocalVersionDisplay();
            await CheckForUpdateAsync();
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdateAsync();
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            string exePath = FindTargetExe();

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                Process.Start(startInfo);
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show("No launchable .exe file was found in the app directory.", "Executable Not Found");
            }
        }

        private string FindTargetExe()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentExePath = Assembly.GetExecutingAssembly().Location;

            string[] allExes = Directory.GetFiles(baseDir, "*.exe", SearchOption.AllDirectories);

            return allExes.FirstOrDefault(path =>
                !string.Equals(Path.GetFullPath(path), Path.GetFullPath(currentExePath), StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".vshost.exe", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(path).Equals("createdump.exe", StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateLocalVersionDisplay()
        {
            string localVer = File.Exists(LocalVersionFile)
                ? File.ReadAllText(LocalVersionFile).Trim()
                : "0.0.0";
            VersionText.Text = $"v{localVer}";
        }

        private void SetState(State newState, string title, string subtitle)
        {
            currentState = newState;
            StatusTitle.Text = title;
            StatusSubtitle.Text = subtitle;

            switch (newState)
            {
                case State.Checking:
                case State.Updating:
                    UpdateProgress.IsIndeterminate = true;
                    ActionButton.IsEnabled = false;
                    LaunchButton.IsEnabled = false;
                    break;
                case State.Updated:
                    UpdateProgress.IsIndeterminate = false;
                    UpdateProgress.Value = 100;
                    ActionButton.IsEnabled = true;
                    ActionButton.Content = "Check Again";
                    LaunchButton.IsEnabled = true;
                    break;
                case State.None:
                    UpdateProgress.IsIndeterminate = false;
                    UpdateProgress.Value = 0;
                    ActionButton.IsEnabled = true;
                    ActionButton.Content = "Retry";
                    LaunchButton.IsEnabled = !string.IsNullOrEmpty(FindTargetExe());
                    break;
            }
        }

        public async Task CheckForUpdateAsync()
        {
            SetState(State.Checking, "Checking for updates...", "Connecting to GitHub repository");

            try
            {
                string remoteText = (await httpClient.GetStringAsync(VersionUrl)).Trim();

                string localText = File.Exists(LocalVersionFile)
                    ? File.ReadAllText(LocalVersionFile).Trim()
                    : "0.0.0";

                if (Version.TryParse(remoteText, out Version remoteVersion) &&
                    Version.TryParse(localText, out Version localVersion))
                {
                    if (remoteVersion > localVersion)
                    {
                        await DownloadAndExtractAsync(UpdateZipUrl, AppDomain.CurrentDomain.BaseDirectory, remoteText);

                        if (currentState == State.Updated)
                        {
                            File.WriteAllText(LocalVersionFile, remoteText);
                            UpdateLocalVersionDisplay();
                        }
                    }
                    else
                    {
                        SetState(State.Updated, "Lastest Version", $"Running latest version (v{localText})");
                    }
                }
                else
                {
                    SetState(State.None, "Version Error", "Invalid version format in version.txt.");
                }
            }
            catch (Exception ex)
            {
                SetState(State.None, "Update Check Failed", ex.Message);
            }
        }

        public async Task DownloadAndExtractAsync(string zipUrl, string extractFolder, string targetVersion)
        {
            SetState(State.Updating, $"Downloading v{targetVersion}...", "Downloading and extracting update package");
            string tempZipPath = Path.Combine(Path.GetTempPath(), "update_package.zip");

            try
            {
                using (Stream downloadStream = await httpClient.GetStreamAsync(zipUrl))
                using (FileStream fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await downloadStream.CopyToAsync(fileStream);
                }

                Directory.CreateDirectory(extractFolder);

                using (ZipArchive archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string destinationPath = Path.GetFullPath(Path.Combine(extractFolder, entry.FullName));

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                }

                SetState(State.Updated, "Update Complete!", $"Successfully updated to v{targetVersion}");
            }
            catch (Exception ex)
            {
                SetState(State.None, "Download Failed", ex.Message);
            }
            finally
            {
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
            }
        }
    }
}
