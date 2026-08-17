using System.IO;
using System.Text.Json;

namespace Huge;

/// <summary>
/// DeepSeek API 配置。
/// 配置文件保存在 %APPDATA%\Huge\settings.json，避免把 API Key 硬编码进程序。
/// </summary>
public class ApiSettings
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "deepseek-chat";

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Huge", "settings.json");

    public static ApiSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<ApiSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // 配置损坏时回退到默认值
        }
        return new ApiSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不阻断主流程
        }
    }
}