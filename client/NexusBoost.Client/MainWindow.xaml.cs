using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace NexusBoost.Client
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _monitorTimer;

        private TimeSpan _lastCpuTime;
        private DateTime _lastCpuCheck;

        private ulong _totalMemoryBytes;

        public MainWindow()
        {
            InitializeComponent();

            _lastCpuTime = Process.GetProcesses()
                .Aggregate(TimeSpan.Zero, (current, process) =>
                {
                    try
                    {
                        return current + process.TotalProcessorTime;
                    }
                    catch
                    {
                        return current;
                    }
                });

            _lastCpuCheck = DateTime.UtcNow;

            _monitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };

            _monitorTimer.Tick += MonitorTimer_Tick;

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadBasicSystemInformation();

            await Task.Run(() =>
            {
                LoadHardwareInformation();
            });

            UpdateStorageInformation();
            UpdateMemoryInformation();
            UpdateCpuUsage();

            _monitorTimer.Start();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _monitorTimer.Stop();
        }

        private void LoadBasicSystemInformation()
        {
            MachineText.Text = Environment.MachineName;

            OsText.Text =
                $"{RuntimeInformation.OSDescription}";

            ArchitectureText.Text =
                RuntimeInformation.OSArchitecture.ToString();
        }

        private void LoadHardwareInformation()
        {
            string cpuName = "Processador não identificado";
            string gpuName = "GPU não identificada";

            try
            {
                using var cpuSearcher =
                    new ManagementObjectSearcher(
                        "SELECT Name FROM Win32_Processor");

                foreach (ManagementObject item in cpuSearcher.Get())
                {
                    cpuName =
                        item["Name"]?.ToString()?.Trim()
                        ?? cpuName;

                    break;
                }
            }
            catch
            {
                // O aplicativo continua normalmente caso o WMI falhe.
            }

            try
            {
                using var gpuSearcher =
                    new ManagementObjectSearcher(
                        "SELECT Name FROM Win32_VideoController");

                var gpuNames = gpuSearcher
                    .Get()
                    .Cast<ManagementObject>()
                    .Select(x => x["Name"]?.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();

                if (gpuNames.Length > 0)
                {
                    gpuName = string.Join(" • ", gpuNames);
                }
            }
            catch
            {
                // O aplicativo continua normalmente caso o WMI falhe.
            }

            GetPhysicallyInstalledSystemMemory(out ulong memoryKilobytes);

            _totalMemoryBytes =
                memoryKilobytes * 1024;

            Dispatcher.Invoke(() =>
            {
                CpuNameText.Text = cpuName;
                GpuNameText.Text = gpuName;
            });
        }

        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            UpdateCpuUsage();
            UpdateMemoryInformation();
            UpdateStorageInformation();
        }

        private void UpdateCpuUsage()
        {
            try
            {
                DateTime now = DateTime.UtcNow;

                TimeSpan currentCpuTime =
                    Process.GetProcesses()
                        .Aggregate(TimeSpan.Zero, (current, process) =>
                        {
                            try
                            {
                                return current + process.TotalProcessorTime;
                            }
                            catch
                            {
                                return current;
                            }
                        });

                double elapsedMilliseconds =
                    (now - _lastCpuCheck).TotalMilliseconds;

                double cpuMilliseconds =
                    (currentCpuTime - _lastCpuTime).TotalMilliseconds;

                double cpuUsage = 0;

                if (elapsedMilliseconds > 0)
                {
                    cpuUsage =
                        cpuMilliseconds /
                        (elapsedMilliseconds * Environment.ProcessorCount)
                        * 100;
                }

                cpuUsage =
                    Math.Clamp(cpuUsage, 0, 100);

                CpuProgress.Value = cpuUsage;

                CpuUsageText.Text =
                    $"{cpuUsage:0}%";

                _lastCpuTime = currentCpuTime;
                _lastCpuCheck = now;
            }
            catch
            {
                CpuUsageText.Text = "--";
            }
        }

        private void UpdateMemoryInformation()
        {
            try
            {
                MEMORYSTATUSEX status =
                    new MEMORYSTATUSEX();

                if (!GlobalMemoryStatusEx(status))
                {
                    return;
                }

                ulong total =
                    status.ullTotalPhys;

                ulong available =
                    status.ullAvailPhys;

                ulong used =
                    total - available;

                double percentage =
                    total == 0
                        ? 0
                        : used * 100.0 / total;

                RamProgress.Value =
                    percentage;

                RamUsageText.Text =
                    $"{percentage:0}%";

                RamDetailText.Text =
                    $"{FormatBytes(used)} usados de {FormatBytes(total)}";

                if (_totalMemoryBytes == 0)
                {
                    _totalMemoryBytes = total;
                }
            }
            catch
            {
                RamUsageText.Text = "--";
                RamDetailText.Text =
                    "Não foi possível ler a memória.";
            }
        }

        private void UpdateStorageInformation()
        {
            try
            {
                string systemRoot =
                    Path.GetPathRoot(
                        Environment.SystemDirectory)
                    ?? "C:\\";

                DriveInfo drive =
                    new DriveInfo(systemRoot);

                if (!drive.IsReady)
                {
                    return;
                }

                long total =
                    drive.TotalSize;

                long free =
                    drive.AvailableFreeSpace;

                long used =
                    total - free;

                double percentage =
                    total == 0
                        ? 0
                        : used * 100.0 / total;

                DiskProgress.Value =
                    percentage;

                DiskUsageText.Text =
                    $"{percentage:0}%";

                DiskDetailText.Text =
                    $"{FormatBytes((ulong)used)} usados de {FormatBytes((ulong)total)}";
            }
            catch
            {
                DiskUsageText.Text = "--";
                DiskDetailText.Text =
                    "Não foi possível ler o disco.";
            }
        }

        private static string FormatBytes(ulong bytes)
        {
            const double gb =
                1024d * 1024d * 1024d;

            if (bytes >= gb)
            {
                return $"{bytes / gb:0.0} GB";
            }

            const double mb =
                1024d * 1024d;

            return $"{bytes / mb:0.0} MB";
        }

        private void ShowDashboard()
        {
            DashboardPanel.Visibility =
                Visibility.Visible;

            ModulePanel.Visibility =
                Visibility.Collapsed;

            PageTitle.Text =
                "Visão Geral";

            PageSubtitle.Text =
                "Status e desempenho do seu computador";
        }

        private void ShowModule(
            string title,
            string subtitle,
            string icon,
            string description,
            string status)
        {
            DashboardPanel.Visibility =
                Visibility.Collapsed;

            ModulePanel.Visibility =
                Visibility.Visible;

            PageTitle.Text =
                title;

            PageSubtitle.Text =
                subtitle;

            ModuleIcon.Text =
                icon;

            ModuleTitle.Text =
                title;

            ModuleDescription.Text =
                description;

            ModuleStatus.Text =
                status;
        }

        private void Home_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowDashboard();
        }

        private void BackHome_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowDashboard();
        }

        private void Optimization_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowModule(
                "Otimização",
                "Análise segura de desempenho",
                "⚡",
                "Aqui ficarão os perfis de desempenho, análise de processos, plano de energia e otimizações reversíveis.",
                "As alterações do Windows permanecem bloqueadas até que o sistema de backup, validação e restauração esteja implementado.");
        }

        private void Graphics_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowModule(
                "Gráficos e Imagem",
                "Configurações visuais compatíveis",
                "◉",
                "Central para brilho, gama e recursos gráficos disponíveis no hardware do usuário.",
                "O NEXUS detectará a compatibilidade antes de disponibilizar cada controle. Nenhuma configuração será forçada em hardware incompatível.");
        }

        private void Games_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowModule(
                "Jogos",
                "Perfis individuais",
                "▶",
                "Área destinada à detecção de jogos e aos perfis personalizados de desempenho.",
                "Arquivos executáveis dos jogos e sistemas anti-cheat não serão modificados.");
        }

        private void Monitoring_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowModule(
                "Monitoramento",
                "Informações do computador em tempo real",
                "▥",
                "O monitoramento reúne informações de CPU, memória, armazenamento e hardware.",
                "CPU, RAM e armazenamento já estão sendo monitorados no painel inicial.");
        }

        private void Cleaning_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowModule(
                "Limpeza",
                "Análise de arquivos temporários",
                "◇",
                "O módulo de limpeza identificará somente categorias seguras e mostrará o que pode ser removido antes da exclusão.",
                "Nenhum arquivo está sendo excluído nesta versão.");
        }

        private void Tools_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowModule(
                "Ferramentas",
                "Utilitários do NEXUS",
                "⚙",
                "Central para diagnósticos, benchmark, informações avançadas e futuras ferramentas do sistema.",
                "Ferramentas avançadas serão adicionadas progressivamente.");
        }

        private void Settings_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowModule(
                "Configurações",
                "Preferências do aplicativo",
                "☷",
                "Configurações do NEXUS BOOST, atualizações, restauração e informações de licença.",
                "O sistema de licenciamento será conectado ao backend posteriormente.");
        }

        private void OptimizeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "A análise do computador está funcionando.\n\n" +
                "Nesta versão, o NEXUS BOOST ainda não realizará alterações no Windows. " +
                "Primeiro implementaremos o sistema de backup e restauração para garantir que as otimizações sejam reversíveis.",
                "NEXUS BOOST — Modo Seguro",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool
            GlobalMemoryStatusEx(
                [In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool
            GetPhysicallyInstalledSystemMemory(
                out ulong totalMemoryInKilobytes);

        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            public uint dwLength =
                (uint)Marshal.SizeOf<MEMORYSTATUSEX>();

            public uint dwMemoryLoad;

            public ulong ullTotalPhys;
            public ulong ullAvailPhys;

            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;

            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;

            public ulong ullAvailExtendedVirtual;
        }
    }
}