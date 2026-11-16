
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UltraLightAI;

public partial class Form1 : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_SHOW_HIDE = 1;
    private const int HOTKEY_CODING_CAPTURE = 2;
    private const int HOTKEY_PRACTICE_CAPTURE = 3;
    private const int HOTKEY_EXIT = 4;
    private const int HOTKEY_OPACITY_100 = 5;
    private const int HOTKEY_OPACITY_50 = 6;
    private const int HOTKEY_OPACITY_10 = 7;

    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;

    private static readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    private PictureBox previewBox = null!;
    private RichTextBox resultBox = null!;
    private Label statusLabel = null!;
    private Button analyzeButton = null!;
    private Button saveButton = null!;
    private Button practiceButton = null!;
    private Button codingButton = null!;
    private Button clearButton = null!;

    private Bitmap? currentScreenshot;
    private CancellationTokenSource? analysisCancellation;
    private System.Windows.Forms.Timer? captureTimer;

    private bool isCapturing;
    private bool hotkeysRegistered;
    private int screenshotVersion;
    private string captureMode = "Practice";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int Msg,
        IntPtr wParam,
        IntPtr lParam
    );

    public Form1()
    {
        InitializeComponent();

        Controls.Clear();

        BuildInterface();

        ConfigureWindow();

        RegisterGlobalHotkeys();
    }

    private void ConfigureWindow()
    {
        Text = "UltraLightAI";
        Size = new Size(700, 590);
        MinimumSize = new Size(550, 450);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(18, 20, 28);
        ForeColor = Color.White;
        TopMost = true;
        Opacity = 1.0;
        DoubleBuffered = true;
    }

    private void BuildInterface()
    {
        Panel titleBar = new()
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = Color.FromArgb(27, 30, 42)
        };

        Label title = new()
        {
            Text = "UltraLightAI",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            Location = new Point(16, 10),
            AutoSize = true
        };

        Label closeButton = new()
        {
            Text = "×",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            Size = new Size(45, 45),
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(Width - 55, 0),
            Cursor = Cursors.Hand
        };

        closeButton.Click += (_, _) => Close();

        titleBar.Controls.Add(title);
        titleBar.Controls.Add(closeButton);

        titleBar.MouseDown += DragWindow;
        title.MouseDown += DragWindow;

        Panel mainPanel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            BackColor = Color.FromArgb(18, 20, 28)
        };

        FlowLayoutPanel buttonPanel = new()
        {
            Dock = DockStyle.Top,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        practiceButton = CreateButton("Practice Capture", Color.FromArgb(42, 110, 190));
        codingButton = CreateButton("Coding Capture", Color.FromArgb(100, 75, 180));
        analyzeButton = CreateButton("Analyze Latest", Color.FromArgb(25, 145, 105));
        saveButton = CreateButton("Save Image", Color.FromArgb(75, 85, 100));
        clearButton = CreateButton("Clear", Color.FromArgb(160, 65, 65));

        practiceButton.Click += (_, _) => StartCapture("Practice");
        codingButton.Click += (_, _) => StartCapture("Coding");
        analyzeButton.Click += async (_, _) => await AnalyzeLatestScreenshotAsync();
        saveButton.Click += (_, _) => SaveScreenshot();
        clearButton.Click += (_, _) => ClearPreview();

        buttonPanel.Controls.AddRange(
        [
            practiceButton,
            codingButton,
            analyzeButton,
            saveButton,
            clearButton
        ]);

        previewBox = new PictureBox
        {
            Dock = DockStyle.Top,
            Height = 210,
            BackColor = Color.FromArgb(30, 33, 44),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle
        };

        Label resultTitle = new()
        {
            Text = "AI Analysis",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };

        resultBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(27, 30, 42),
            ForeColor = Color.FromArgb(225, 228, 235),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 10),
            ReadOnly = true,
            WordWrap = true,
            DetectUrls = true
        };

        statusLabel = new Label
        {
            Text = "Ready",
            Dock = DockStyle.Bottom,
            Height = 27,
            ForeColor = Color.LightGreen,
            TextAlign = ContentAlignment.MiddleLeft
        };

        mainPanel.Controls.Add(resultBox);
        mainPanel.Controls.Add(resultTitle);
        mainPanel.Controls.Add(previewBox);
        mainPanel.Controls.Add(buttonPanel);
        mainPanel.Controls.Add(statusLabel);

        Controls.Add(mainPanel);
        Controls.Add(titleBar);

        UpdateButtonStates();
    }

    private Button CreateButton(string text, Color color)
    {
        return new Button
        {
            Text = text,
            Width = 124,
            Height = 36,
            Margin = new Padding(0, 0, 7, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
    }

    private void RegisterGlobalHotkeys()
    {
        if (!IsHandleCreated)
            return;

        bool success = true;

        success &= RegisterHotKey(Handle, HOTKEY_SHOW_HIDE, MOD_SHIFT, (uint)Keys.S);
        success &= RegisterHotKey(Handle, HOTKEY_CODING_CAPTURE, MOD_SHIFT, (uint)Keys.J);
        success &= RegisterHotKey(Handle, HOTKEY_PRACTICE_CAPTURE, MOD_SHIFT, (uint)Keys.Z);
        success &= RegisterHotKey(Handle, HOTKEY_EXIT, MOD_SHIFT, (uint)Keys.E);

        success &= RegisterHotKey(Handle, HOTKEY_OPACITY_100, MOD_CONTROL | MOD_ALT, (uint)Keys.D1);
        success &= RegisterHotKey(Handle, HOTKEY_OPACITY_50, MOD_CONTROL | MOD_ALT, (uint)Keys.D2);
        success &= RegisterHotKey(Handle, HOTKEY_OPACITY_10, MOD_CONTROL | MOD_ALT, (uint)Keys.D3);

        hotkeysRegistered = success;

        if (!success)
            SetStatus("Some global hotkeys could not be registered.");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HOTKEY_SHOW_HIDE:
                    ToggleVisibility();
                    break;

                case HOTKEY_CODING_CAPTURE:
                    StartCapture("Coding");
                    break;

                case HOTKEY_PRACTICE_CAPTURE:
                    StartCapture("Practice");
                    break;

                case HOTKEY_EXIT:
                    Close();
                    break;

                case HOTKEY_OPACITY_100:
                    Opacity = 1.0;
                    break;

                case HOTKEY_OPACITY_50:
                    Opacity = 0.5;
                    break;

                case HOTKEY_OPACITY_10:
                    Opacity = 0.1;
                    break;
            }
        }

        base.WndProc(ref m);
    }

    private void ToggleVisibility()
    {
        if (Visible)
            Hide();
        else
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }
    }

    private void StartCapture(string mode)
    {
        if (isCapturing)
            return;

        if (analysisCancellation != null)
        {
            analysisCancellation.Cancel();
            analysisCancellation.Dispose();
            analysisCancellation = null;
        }

        screenshotVersion++;

        captureMode = mode;

        isCapturing = true;

        currentScreenshot?.Dispose();
        currentScreenshot = null;

        previewBox.Image = null;

        resultBox.Clear();

        UpdateButtonStates();

        SetStatus("Preparing screenshot...");

        Hide();

        captureTimer?.Stop();
        captureTimer?.Dispose();

        captureTimer = new System.Windows.Forms.Timer
        {
            Interval = 500
        };

        captureTimer.Tick += (_, _) =>
        {
            captureTimer?.Stop();
            captureTimer?.Dispose();
            captureTimer = null;

            CaptureScreen();
        };

        captureTimer.Start();
    }

    private void CaptureScreen()
    {
        try
        {
            Rectangle bounds = SystemInformation.VirtualScreen;

            if (bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException("Screen dimensions are invalid.");

            Bitmap screenshot = new(bounds.Width, bounds.Height);

            using (Graphics graphics = Graphics.FromImage(screenshot))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    bounds.Size
                );
            }

            currentScreenshot?.Dispose();

            currentScreenshot = screenshot;

            previewBox.Image = currentScreenshot;

            SetStatus($"{captureMode} screenshot captured successfully.");

            resultBox.Text = "Screenshot captured. Click Analyze Latest to analyze it.";
        }
        catch (Exception ex)
        {
            SetStatus("Capture failed: " + ex.Message);
        }
        finally
        {
            isCapturing = false;

            Show();
            WindowState = FormWindowState.Normal;
            Activate();

            UpdateButtonStates();
        }
    }

    private async Task AnalyzeLatestScreenshotAsync()
    {
        if (currentScreenshot == null)
        {
            SetStatus("Capture a screenshot first.");
            return;
        }

        string apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetStatus("GEMINI_API_KEY environment variable is missing.");
            return;
        }

        string model = Environment.GetEnvironmentVariable("GEMINI_MODEL")
            ?? "gemini-3.8-flash";

        if (string.IsNullOrWhiteSpace(model))
            model = "gemini-3.8-flash";

        model = model.Trim();

        int currentVersion = screenshotVersion;

        analysisCancellation?.Cancel();
        analysisCancellation?.Dispose();

        analysisCancellation = new CancellationTokenSource();

        CancellationToken token = analysisCancellation.Token;

        analyzeButton.Enabled = false;
        SetStatus("Analyzing screenshot...");

        try
        {
            string base64Image = ConvertBitmapToBase64(currentScreenshot);

            string prompt = captureMode == "Coding"
                ? "Act as a programming tutor. Read the coding problem in this screenshot. Explain the problem, identify the input and output, provide an approach, analyze edge cases, and provide a correct solution with time and space complexity. If code is visible, review it for syntax, runtime, logical, and edge-case errors. Do not assume unreadable text."
                : "Act as a study tutor. Read the question in this screenshot carefully. Explain what it asks, show the reasoning step by step, and provide the answer with a concise explanation. If any text is unclear, mention that instead of guessing.";

            object requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                text = prompt
                            },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = "image/jpeg",
                                    data = base64Image
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 4096
                }
            };

            string json = JsonSerializer.Serialize(requestBody);

            using StringContent content = new(
                json,
                Encoding.UTF8,
                "application/json"
            );

            string url =
                $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

            using HttpResponseMessage response = await httpClient.PostAsync(
                url,
                content,
                token
            );

            string responseText = await response.Content.ReadAsStringAsync(token);

            if (!response.IsSuccessStatusCode)
            {
                string errorMessage = ExtractApiError(responseText);

                if (currentVersion == screenshotVersion)
                {
                    resultBox.Text = errorMessage;
                    SetStatus($"Gemini API error: {(int)response.StatusCode}");
                }

                return;
            }

            string answer = ExtractGeminiText(responseText);

            if (currentVersion != screenshotVersion || token.IsCancellationRequested)
                return;

            if (string.IsNullOrWhiteSpace(answer))
            {
                resultBox.Text = "Gemini returned an empty response.";
                SetStatus("No analysis received.");
                return;
            }

            resultBox.Text = answer;
            SetStatus("Analysis completed.");
        }
        catch (OperationCanceledException)
        {
            if (currentVersion == screenshotVersion)
                SetStatus("Analysis cancelled.");
        }
        catch (Exception ex)
        {
            if (currentVersion == screenshotVersion)
            {
                resultBox.Text = ex.Message;
                SetStatus("Analysis failed.");
            }
        }
        finally
        {
            if (currentVersion == screenshotVersion)
                UpdateButtonStates();
        }
    }

    private string ConvertBitmapToBase64(Bitmap source)
    {
        const int maxDimension = 1600;

        int width = source.Width;
        int height = source.Height;

        double scale = Math.Min(
            1.0,
            (double)maxDimension / Math.Max(width, height)
        );

        int newWidth = Math.Max(1, (int)(width * scale));
        int newHeight = Math.Max(1, (int)(height * scale));

        using Bitmap resized = new(newWidth, newHeight);

        using (Graphics graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            graphics.DrawImage(
                source,
                new Rectangle(0, 0, newWidth, newHeight)
            );
        }

        using MemoryStream stream = new();

        resized.Save(
            stream,
            System.Drawing.Imaging.ImageFormat.Jpeg
        );

        return Convert.ToBase64String(stream.ToArray());
    }

    private string ExtractGeminiText(string responseJson)
    {
        using JsonDocument document = JsonDocument.Parse(responseJson);

        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("candidates", out JsonElement candidates))
            return "No candidates were returned by Gemini.";

        if (candidates.GetArrayLength() == 0)
            return "Gemini did not return any answer.";

        JsonElement candidate = candidates[0];

        if (!candidate.TryGetProperty("content", out JsonElement content))
            return "Gemini response did not contain content.";

        if (!content.TryGetProperty("parts", out JsonElement parts))
            return "Gemini response did not contain text parts.";

        StringBuilder answer = new();

        foreach (JsonElement part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out JsonElement text))
                answer.AppendLine(text.GetString());
        }

        return answer.ToString().Trim();
    }

    private string ExtractApiError(string responseJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseJson);

            if (document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                if (error.TryGetProperty("message", out JsonElement message))
                    return message.GetString() ?? responseJson;
            }
        }
        catch
        {
        }

        return responseJson;
    }

    private void SaveScreenshot()
    {
        if (currentScreenshot == null)
        {
            SetStatus("No screenshot available to save.");
            return;
        }

        using SaveFileDialog dialog = new()
        {
            Title = "Save Screenshot",
            Filter = "PNG Image|*.png|JPEG Image|*.jpg",
            FileName = $"UltraLightAI_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();

            System.Drawing.Imaging.ImageFormat format =
                extension == ".jpg" || extension == ".jpeg"
                    ? System.Drawing.Imaging.ImageFormat.Jpeg
                    : System.Drawing.Imaging.ImageFormat.Png;

            currentScreenshot.Save(dialog.FileName, format);

            SetStatus("Screenshot saved successfully.");
        }
        catch (Exception ex)
        {
            SetStatus("Save failed: " + ex.Message);
        }
    }

    private void ClearPreview()
    {
        screenshotVersion++;

        analysisCancellation?.Cancel();
        analysisCancellation?.Dispose();
        analysisCancellation = null;

        currentScreenshot?.Dispose();
        currentScreenshot = null;

        previewBox.Image = null;
        resultBox.Clear();

        SetStatus("Cleared.");

        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        bool hasScreenshot = currentScreenshot != null;

        if (analyzeButton != null)
            analyzeButton.Enabled = hasScreenshot && !isCapturing;

        if (saveButton != null)
            saveButton.Enabled = hasScreenshot && !isCapturing;

        if (practiceButton != null)
            practiceButton.Enabled = !isCapturing;

        if (codingButton != null)
            codingButton.Enabled = !isCapturing;
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null && !IsDisposed)
            statusLabel.Text = message;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        captureTimer?.Stop();
        captureTimer?.Dispose();
        captureTimer = null;

        analysisCancellation?.Cancel();
        analysisCancellation?.Dispose();
        analysisCancellation = null;

        currentScreenshot?.Dispose();
        currentScreenshot = null;

        if (hotkeysRegistered)
        {
            UnregisterHotKey(Handle, HOTKEY_SHOW_HIDE);
            UnregisterHotKey(Handle, HOTKEY_CODING_CAPTURE);
            UnregisterHotKey(Handle, HOTKEY_PRACTICE_CAPTURE);
            UnregisterHotKey(Handle, HOTKEY_EXIT);
            UnregisterHotKey(Handle, HOTKEY_OPACITY_100);
            UnregisterHotKey(Handle, HOTKEY_OPACITY_50);
            UnregisterHotKey(Handle, HOTKEY_OPACITY_10);
        }

        base.OnFormClosed(e);
    }
}
