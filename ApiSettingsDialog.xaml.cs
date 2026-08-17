using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Huge;

public partial class ApiSettingsDialog : Window
{
    private readonly ApiSettings _settings;

    public ApiSettingsDialog(ApiSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        BaseUrlBox.Text = settings.BaseUrl;
        ApiKeyBox.Password = settings.ApiKey;
        // 选中已保存模型（若列表中不存在则默认选中 deepseek-chat）
        var savedModel = settings.Model;
        var selectedItem = ModelBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => (item.Content as string) == savedModel)
            ?? ModelBox.Items.Cast<ComboBoxItem>().FirstOrDefault();
        if (selectedItem != null) ModelBox.SelectedItem = selectedItem;

        TestBtn.Click += TestBtn_Click;
        OkBtn.Click += OkBtn_Click;
        CancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
    }

    private void TestBtn_Click(object sender, RoutedEventArgs e)
    {
        // 用当前输入测试一个小请求
        var baseUrl = BaseUrlBox.Text.Trim().TrimEnd('/');
        var apiKey = ApiKeyBox.Password.Trim();
        var model = (ModelBox.SelectedItem as ComboBoxItem)?.Content as string ?? "deepseek-chat";

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
        {
            MessageBox.Show(Owner, "请填写 Base URL 和 API Key。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TestBtn.IsEnabled = false;
        TestBtn.Content = "连接中...";

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var body = new
            {
                model,
                messages = new[]
                {
                    new { role = "user", content = "ping" }
                },
                max_tokens = 5,
                stream = false
            };

            var content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = client.PostAsync($"{baseUrl}/chat/completions", content).Result;
            var respBody = response.Content.ReadAsStringAsync().Result;

            if (response.IsSuccessStatusCode)
            {
                MessageBox.Show(Owner, "连接成功！API 配置有效。", "测试通过",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(Owner,
                    $"连接失败 ({(int)response.StatusCode}):\n{respBody}",
                    "测试失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Owner, $"连接失败:\n{ex.Message}",
                "测试失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestBtn.IsEnabled = true;
            TestBtn.Content = "测试连接";
        }
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.BaseUrl = BaseUrlBox.Text.Trim();
        _settings.ApiKey = ApiKeyBox.Password.Trim();
        _settings.Model = (ModelBox.SelectedItem as ComboBoxItem)?.Content as string ?? "deepseek-chat";
        _settings.Save();
        DialogResult = true;
        Close();
    }
}