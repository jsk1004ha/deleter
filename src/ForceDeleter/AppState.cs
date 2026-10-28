using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForceDeleter;

public sealed class AppSettings
{
    public bool TurboMode { get; set; } = true;
    public bool SecureDelete { get; set; }
    public bool Confirm { get; set; } = true;
    public bool RemoveLocked { get; set; } = true;
    public bool DarkMode { get; set; } = true;
    public int RetentionDays { get; set; } = 30;

    [JsonIgnore]
    public bool TurboFastMode { get => TurboMode; set => TurboMode = value; }

    [JsonIgnore]
    public bool ShowConfirmationPrompt { get => Confirm; set => Confirm = value; }

    [JsonIgnore]
    public bool RemoveLockedFiles { get => RemoveLocked; set => RemoveLocked = value; }
}

internal sealed class AppState
{
    private readonly string _directory;
    public AppSettings Settings { get; private set; } = new();
    public List<HistoryRecord> History { get; private set; } = [];

    public AppState(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForceDeleter");
        Settings = Read<AppSettings>("settings.json") ?? new();
        History = Read<List<HistoryRecord>>("history.json") ?? [];
        Prune();
    }

    private T? Read<T>(string name)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(_directory, name))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }

    public void Prune()
    {
        if (Settings.RetentionDays > 0)
            History.RemoveAll(h => h.Timestamp < DateTime.Now.AddDays(-Settings.RetentionDays));
    }

    public void Save()
    {
        Prune();
        Directory.CreateDirectory(_directory);
        Write("settings.json", Settings);
        Write("history.json", History);
    }

    private void Write<T>(string name, T value)
    {
        string path = Path.Combine(_directory, name);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
