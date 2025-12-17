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
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            Start();

            await Task.Delay(1000, stoppingToken);
        }
    }

    public void Start()
    {
        LoadConfiguration();

        Console.WriteLine("Auto Folder Backup Started...");
        ValidateDirectories();
        InitialBackup();
        StartWatcher();

        Console.WriteLine("Listening for changes... Press ENTER to exit.");
        Console.ReadLine();
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

        _logger.LogInformation("FileSystemWatcher started.");
    }


    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath))
            return;

        _logger.LogInformation("Detected change: {File}", e.FullPath);
        BackupFile(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _logger.LogInformation("Renamed: {Old} -> {New}", e.OldFullPath, e.FullPath);
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
            string backupDir = Path.GetDirectoryName(backupFile)!;

            Directory.CreateDirectory(backupDir);

            if (File.Exists(backupFile))
            {
                var sourceTime = File.GetLastWriteTimeUtc(sourceFile);
                var backupTime = File.GetLastWriteTimeUtc(backupFile);

                if (sourceTime <= backupTime)
                    return; // no change
            }

            CopyWithRetry(sourceFile, backupFile);
            Console.WriteLine($"Backed up: {relativePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error backing up {sourceFile}: {ex.Message}");
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
