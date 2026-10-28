using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ForceDeleter;

/// <summary>
/// 환경설정(AppSettings)과 삭제 이력(HistoryRecord)을 %LocalAppData%\ForceDeleter 경로에
/// JSON 포맷으로 영구 저장하고 로드하는 경량 영속화 매니저입니다.
/// </summary>
public static class PersistenceManager
{
    public static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ForceDeleter");

    private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");
    private static readonly string HistoryFilePath = Path.Combine(AppDataFolder, "history.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings LoadSettings(string? folder = null)
    {
        try
        {
            string filePath = folder != null ? Path.Combine(folder, "settings.json") : SettingsFilePath;
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // 손상된 설정 파일의 경우 기본값으로 복구
        }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings, string? folder = null)
    {
        try
        {
            string targetFolder = folder ?? AppDataFolder;
            Directory.CreateDirectory(targetFolder);
            string filePath = Path.Combine(targetFolder, "settings.json");
            string tempPath = filePath + ".tmp";
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, true);
        }
        catch
        {
            // 권한 또는 디스크 공간 부족 시 무시
        }
    }

    public static List<HistoryRecord> LoadHistory(string? folder = null)
    {
        try
        {
            string filePath = folder != null ? Path.Combine(folder, "history.json") : HistoryFilePath;
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<List<HistoryRecord>>(json, JsonOptions) ?? [];
            }
        }
        catch
        {
            // 손상된 이력 파일의 경우 빈 목록 반환
        }
        return [];
    }

    public static void SaveHistory(IEnumerable<HistoryRecord> history, string? folder = null)
    {
        try
        {
            string targetFolder = folder ?? AppDataFolder;
            Directory.CreateDirectory(targetFolder);
            string filePath = Path.Combine(targetFolder, "history.json");
            string tempPath = filePath + ".tmp";
            string json = JsonSerializer.Serialize(history, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, true);
        }
        catch
        {
            // 권한 또는 디스크 공간 부족 시 무시
        }
    }
}
