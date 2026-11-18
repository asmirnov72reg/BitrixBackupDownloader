using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BitrixBackupDownloader
{
    public partial class Form1 : Form
    {
        private TextBox txtBaseFolder;
        private Button btnBrowse;
        private TextBox txtUrl;
        private Button btnDownload;
        private Button btnCancel;
        private TextBox txtLog;
        private ProgressBar progressBar;
        private Label lblStatus;

        private CancellationTokenSource cts;
        private string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_folder.txt");

        private class BackupPart
        {
            public string Url { get; set; }
            public string FilePath { get; set; }
            public long Size { get; set; }
        }

        public Form1()
        {
            SetupUI();
            LoadSavedFolder();
        }

        private void SetupUI()
        {
            this.Text = "Скачивание бекапов 1С-Битрикс";
            this.Size = new Size(580, 445);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            Label lblFolder = new Label();
            lblFolder.Text = "Основная папка для сохранения бекапов:";
            lblFolder.Left = 15;
            lblFolder.Top = 15;
            lblFolder.Width = 530;

            txtBaseFolder = new TextBox();
            txtBaseFolder.Left = 15;
            txtBaseFolder.Top = 38;
            txtBaseFolder.Width = 445;
            txtBaseFolder.ReadOnly = true;

            btnBrowse = new Button();
            btnBrowse.Text = "Обзор...";
            btnBrowse.Left = 470;
            btnBrowse.Top = 36;
            btnBrowse.Width = 80;
            btnBrowse.Height = 24;
            btnBrowse.Click += BtnBrowse_Click;

            Label lblUrl = new Label();
            lblUrl.Text = "Ссылка для переноса из админки Битрикс:";
            lblUrl.Left = 15;
            lblUrl.Top = 75;
            lblUrl.Width = 530;

            txtUrl = new TextBox();
            txtUrl.Left = 15;
            txtUrl.Top = 98;
            txtUrl.Width = 535;

            btnDownload = new Button();
            btnDownload.Text = "Скачать все части бекапа";
            btnDownload.Left = 15;
            btnDownload.Top = 135;
            btnDownload.Width = 415;
            btnDownload.Height = 35;
            btnDownload.Font = new Font(this.Font, FontStyle.Bold);
            btnDownload.Click += BtnDownload_Click;

            btnCancel = new Button();
            btnCancel.Text = "Отмена";
            btnCancel.Left = 440;
            btnCancel.Top = 135;
            btnCancel.Width = 110;
            btnCancel.Height = 35;
            btnCancel.Enabled = false;
            btnCancel.Click += BtnCancel_Click;

            txtLog = new TextBox();
            txtLog.Left = 15;
            txtLog.Top = 185;
            txtLog.Width = 535;
            txtLog.Height = 145;
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.ReadOnly = true;

            progressBar = new ProgressBar();
            progressBar.Left = 15;
            progressBar.Top = 342;
            progressBar.Width = 535;
            progressBar.Height = 22;
            progressBar.Minimum = 0;
            progressBar.Maximum = 1000;

            lblStatus = new Label();
            lblStatus.Text = "Готов к работе";
            lblStatus.Left = 15;
            lblStatus.Top = 370;
            lblStatus.Width = 535;
            lblStatus.Height = 22;
            lblStatus.Font = new Font(this.Font.FontFamily, 8.5f);

            this.Controls.Add(lblFolder);
            this.Controls.Add(txtBaseFolder);
            this.Controls.Add(btnBrowse);
            this.Controls.Add(lblUrl);
            this.Controls.Add(txtUrl);
            this.Controls.Add(btnDownload);
            this.Controls.Add(btnCancel);
            this.Controls.Add(txtLog);
            this.Controls.Add(progressBar);
            this.Controls.Add(lblStatus);
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (cts != null)
            {
                cts.Cancel();
                Log("Запрос на отмену скачивания...");
            }
        }

        private void LoadSavedFolder()
        {
            if (File.Exists(configPath))
                txtBaseFolder.Text = File.ReadAllText(configPath).Trim();
            else
                txtBaseFolder.Text = @"D:\BitrixBackups";
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                if (Directory.Exists(txtBaseFolder.Text))
                    fbd.SelectedPath = txtBaseFolder.Text;

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtBaseFolder.Text = fbd.SelectedPath;
                    File.WriteAllText(configPath, fbd.SelectedPath);
                }
            }
        }

        private void Log(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string>(Log), message);
                return;
            }
            txtLog.AppendText(string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, message, Environment.NewLine));
        }

        private void UpdateStatus(int permille, string statusText)
        {
            if (lblStatus.InvokeRequired)
            {
                lblStatus.Invoke(new Action<int, string>(UpdateStatus), permille, statusText);
                return;
            }
            progressBar.Value = Math.Max(0, Math.Min(1000, permille));
            lblStatus.Text = statusText;
        }

        private async void BtnDownload_Click(object sender, EventArgs e)
        {
            string url = txtUrl.Text.Trim();
            string baseFolder = txtBaseFolder.Text.Trim();

            if (string.IsNullOrEmpty(url) || !url.StartsWith("http"))
            {
                MessageBox.Show("Вставьте корректную ссылку на архив Битрикс!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(baseFolder))
            {
                MessageBox.Show("Выберите папку для сохранения!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnDownload.Enabled = false;
            btnBrowse.Enabled = false;
            btnCancel.Enabled = true;
            txtLog.Clear();
            UpdateStatus(0, "Анализ архива на сервере...");

            cts = new CancellationTokenSource();

            await Task.Run(() => DownloadBackup(url, baseFolder, cts.Token));

            btnDownload.Enabled = true;
            btnBrowse.Enabled = true;
            btnCancel.Enabled = false;
        }

        private void DownloadBackup(string url, string baseFolder, CancellationToken token)
        {
            try
            {
                Uri uri = new Uri(url);
                string baseFileName = Path.GetFileName(uri.LocalPath);

                string subFolderName = "backup_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
                Match match = Regex.Match(baseFileName, @"^(.+?)_(\d{4})(\d{2})(\d{2})_");
                if (match.Success)
                {
                    string domain = match.Groups[1].Value;
                    string dateFormatted = string.Format("{0}-{1}-{2}", match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value);
                    subFolderName = string.Format("{0}_{1}", domain, dateFormatted);
                }

                string targetDir = Path.Combine(baseFolder, subFolderName);
                Directory.CreateDirectory(targetDir);
                Log("Папка назначения: " + targetDir);

                Log("Подсчет количества частей и общего объема...");
                List<BackupPart> parts = new List<BackupPart>();
                long totalBytes = 0;

                long firstSize = GetRemoteFileSize(url);
                if (firstSize <= 0)
                {
                    Log("Ошибка: основной файл недоступен по ссылке.");
                    UpdateStatus(0, "Ошибка доступа к файлу");
                    return;
                }

                BackupPart firstPart = new BackupPart();
                firstPart.Url = url;
                firstPart.FilePath = Path.Combine(targetDir, baseFileName);
                firstPart.Size = firstSize;
                parts.Add(firstPart);
                totalBytes += firstSize;

                int partIndex = 1;
                while (!token.IsCancellationRequested)
                {
                    string pUrl = string.Format("{0}.{1}", url, partIndex);
                    long pSize = GetRemoteFileSize(pUrl);
                    if (pSize <= 0) break;

                    BackupPart nextPart = new BackupPart();
                    nextPart.Url = pUrl;
                    nextPart.FilePath = Path.Combine(targetDir, string.Format("{0}.{1}", baseFileName, partIndex));
                    nextPart.Size = pSize;
                    parts.Add(nextPart);

                    totalBytes += pSize;
                    partIndex++;
                }

                if (token.IsCancellationRequested)
                {
                    Log("Операция отменена пользователем.");
                    UpdateStatus(0, "Отменено");
                    return;
                }

                Log(string.Format("Найдено частей: {0}. Общий размер: {1}", parts.Count, FormatBytes(totalBytes)));

                Stopwatch sw = Stopwatch.StartNew();
                long initialExistingBytes = 0;
                foreach (BackupPart p in parts)
                {
                    if (File.Exists(p.FilePath))
                        initialExistingBytes += new FileInfo(p.FilePath).Length;
                }

                for (int i = 0; i < parts.Count; i++)
                {
                    if (token.IsCancellationRequested) break;

                    BackupPart currentPart = parts[i];
                    Log(string.Format("Скачивание части {0} из {1}: {2}", i + 1, parts.Count, Path.GetFileName(currentPart.FilePath)));

                    bool ok = RunCurlWithProgress(currentPart.Url, currentPart.FilePath, token, () =>
                    {
                        long currentDownloaded = 0;
                        foreach (BackupPart p in parts)
                        {
                            if (File.Exists(p.FilePath))
                                currentDownloaded += new FileInfo(p.FilePath).Length;
                        }

                        double elapsedSec = sw.Elapsed.TotalSeconds;
                        long sessionBytes = Math.Max(0, currentDownloaded - initialExistingBytes);
                        double speedBps = elapsedSec > 0.5 ? sessionBytes / elapsedSec : 0;

                        long remainingBytes = Math.Max(0, totalBytes - currentDownloaded);
                        string etaStr = speedBps > 0
                            ? FormatTimeSpan(TimeSpan.FromSeconds(remainingBytes / speedBps))
                            : "--:--:--";

                        int permille = totalBytes > 0 ? (int)((currentDownloaded * 1000) / totalBytes) : 0;
                        double percent = permille / 10.0;

                        string status = string.Format(
                            "{0:0.0}% | {1} из {2} | Скорость: {3}/с | Прошло: {4} | Осталось: {5}",
                            percent,
                            FormatBytes(currentDownloaded),
                            FormatBytes(totalBytes),
                            FormatBytes((long)speedBps),
                            FormatTimeSpan(sw.Elapsed),
                            etaStr
                        );

                        UpdateStatus(permille, status);
                    });

                    if (token.IsCancellationRequested)
                    {
                        Log("Скачивание прервано пользователем.");
                        UpdateStatus(progressBar.Value, "Приостановлено / Отменено (можно докачать позже)");
                        return;
                    }

                    if (!ok)
                    {
                        Log(string.Format("Ошибка при скачивании части {0}.", i + 1));
                        UpdateStatus(progressBar.Value, "Ошибка при скачивании");
                        return;
                    }
                }

                sw.Stop();
                UpdateStatus(1000, string.Format("100% | Скачано {0} за {1}", FormatBytes(totalBytes), FormatTimeSpan(sw.Elapsed)));
                Log("ГОТОВО! Все части резервной копии успешно сохранены.");
                Process.Start("explorer.exe", targetDir);
            }
            catch (Exception ex)
            {
                Log("Критическая ошибка: " + ex.Message);
                UpdateStatus(0, "Произошла ошибка");
            }
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            return string.Format("{0:00}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
        }

        private long GetRemoteFileSize(string url)
        {
            string curlPath = GetCurlPath();
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = curlPath;
            psi.Arguments = string.Format("-s -L -f -I \"{0}\"", url);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.CreateNoWindow = true;

            using (Process proc = Process.Start(psi))
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode != 0) return -1;

                MatchCollection statusMatches = Regex.Matches(output, @"HTTP/\d(?:\.\d)?\s+(\d{3})", RegexOptions.IgnoreCase);
                if (statusMatches.Count > 0)
                {
                    string lastStatus = statusMatches[statusMatches.Count - 1].Groups[1].Value;
                    if (lastStatus != "200") return -1;
                }

                MatchCollection typeMatches = Regex.Matches(output, @"Content-Type:\s*([^\r\n;]+)", RegexOptions.IgnoreCase);
                if (typeMatches.Count > 0)
                {
                    string lastType = typeMatches[typeMatches.Count - 1].Groups[1].Value.Trim().ToLowerInvariant();
                    if (lastType.Contains("text/html") || lastType.Contains("text/plain"))
                        return -1;
                }

                MatchCollection matches = Regex.Matches(output, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
                long size = 0;
                if (matches.Count > 0 && long.TryParse(matches[matches.Count - 1].Groups[1].Value, out size))
                {
                    return size;
                }
                return -1;
            }
        }

        private bool RunCurlWithProgress(string url, string outputPath, CancellationToken token, Action onTick)
        {
            string curlPath = GetCurlPath();
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = curlPath;
            psi.Arguments = string.Format("-L -f -s -C - -o \"{0}\" \"{1}\"", outputPath, url);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            using (Process proc = Process.Start(psi))
            {
                while (!proc.WaitForExit(400))
                {
                    if (token.IsCancellationRequested)
                    {
                        try { proc.Kill(); } catch { }
                        return false;
                    }
                    if (onTick != null) onTick();
                }
                if (onTick != null) onTick();
                return proc.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
            }
        }

        private string GetCurlPath()
        {
            string sysCurl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            return File.Exists(sysCurl) ? sysCurl : "curl.exe";
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return string.Format("{0:0.##} {1}", len, sizes[order]);
        }
    }
}
