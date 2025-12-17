namespace AutoBackupService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private FileSystemWatcher? _watcher;

    private static string SourcePath = string.Empty;
    private static string BackupPath = string.Empty;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backup service started at {time}", DateTimeOffset.Now);

        Initialize();

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private void Initialize()
    {
        LoadConfiguration();
        ValidateDirectories();

        // Run initial backup in background
        _ = Task.Run(() => InitialBackup());

        StartWatcher();
    }

    private void LoadConfiguration()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        SourcePath = config["BackupSettings:SourcePath"]
            ?? throw new Exception("SourcePath is missing in appsettings.json");

        BackupPath = config["BackupSettings:BackupPath"]
            ?? throw new Exception("BackupPath is missing in appsettings.json");
    }

    private void ValidateDirectories()
    {
        if (!Directory.Exists(SourcePath))
            throw new DirectoryNotFoundException($"Source directory not found: {SourcePath}");

        if (!Directory.Exists(BackupPath))
            Directory.CreateDirectory(BackupPath);
    }

    private void InitialBackup()
    {
        foreach (var file in Directory.GetFiles(SourcePath, "*", SearchOption.AllDirectories))
        {
            BackupFile(file);
        }
    }

    private void StartWatcher()
    {
        _watcher = new FileSystemWatcher(SourcePath)
        {
            IncludeSubdirectories = true,
            NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.LastWrite |
                NotifyFilters.Size
        };

        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
        _watcher.EnableRaisingEvents = true;

        _logger.LogInformation("FileSystemWatcher started on {path}", SourcePath);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath)) return;

        _logger.LogInformation("Detected change: {file}", e.FullPath);
        BackupFile(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _logger.LogInformation("Renamed: {old} -> {new}", e.OldFullPath, e.FullPath);
        BackupFile(e.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error occurred.");
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _logger.LogInformation("Backup service stopped.");
        return base.StopAsync(cancellationToken);
    }

    private static void BackupFile(string sourceFile)
    {
        try
        {
            string relativePath = Path.GetRelativePath(SourcePath, sourceFile);
            string backupFile = Path.Combine(BackupPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);

            if (File.Exists(backupFile))
            {
                if (File.GetLastWriteTimeUtc(sourceFile)
                    <= File.GetLastWriteTimeUtc(backupFile))
                    return;
            }

            CopyWithRetry(sourceFile, backupFile);
        }
        catch
        {
            // logging handled by caller
        }
    }

    private static void CopyWithRetry(string source, string destination, int retries = 3)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                File.Copy(source, destination, true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(500);
            }
        }
    }
}
