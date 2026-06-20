using IPWatcherPro.Core;
using IPWatcherPro.Infrastructure;
using IPWatcherPro.Services;
using IPWatcherPro.UI;
using System.Reflection;

namespace IPWatcherPro;

internal static class Program
{
    private static JsonLinesLogger? _logger;

    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        _logger = new JsonLinesLogger();

        Application.ThreadException += (_, e) =>
        {
            _logger?.Write("UIThreadException", new
            {
                Error = e.Exception.ToString()
            });

            MessageBox.Show(
                e.Exception.ToString(),
                "IPWatcherPro UI Crash",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            _logger?.Write("UnhandledException", new
            {
                Error = e.ExceptionObject?.ToString()
            });
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger?.Write("UnobservedTaskException", new
            {
                Error = e.Exception.ToString()
            });

            e.SetObserved();
        };

        try
        {
            var config = AppConfiguration.Load();
            AppConfiguration.Save(config);

            var version =
                System.Reflection.Assembly
                    .GetExecutingAssembly()
                    .GetName()
                    .Version?
                    .ToString();

            AutoStartManager.Apply(config.AutoStart, _logger);

            var hub = new NetworkEventHub();

            var logger = _logger;
            var activityStore = new HunterActivityStore();

            var tray = new TrayController(hub, config);
            HunterActivityForm? activityForm = null;

            IpWatcherService ipService = new(config, hub, logger);
            GeoLeakMonitor geoService = new(config, hub, logger, new ProcessResolver(), activityStore);

            tray.OnShowActivityRequested = () =>
            {
                activityForm ??= new HunterActivityForm(activityStore);

                if (!activityForm.Visible)
                    activityForm.Show();

                activityForm.Activate();
            };

            tray.OnSettingsRequested = () =>
            {
                try
                {
                    config = AppConfiguration.Load();

                    logger.Write("SettingsOpened", new
                    {
                        config.TargetCountryCode,
                        AppConfiguration.ConfigFilePath
                    });

                    using var form = new SettingsForm(config);

                    if (form.ShowDialog() != DialogResult.OK || form.SavedConfiguration is null)
                        return;

                    config = form.SavedConfiguration;
                    AppConfiguration.Save(config);

                    logger.Write("SettingsSaved", new
                    {
                        config.TargetCountryCode,
                        config.RefreshIntervalMinutes,
                        config.NotifyOnCountryChange,
                        config.AutoStart,
                        config.HunterMode_AggressivePolling,
                        config.HunterMode_LogAllChanges,
                        config.HunterMode_ExtendedScan,
                        config.HunterActivityMaxItems,
                        config.LogMaxFileMb
                    });

                    AutoStartManager.Apply(config.AutoStart, logger);

                    ipService.StopAsync().GetAwaiter().GetResult();
                    geoService.StopAsync().GetAwaiter().GetResult();

                    ipService.Dispose();
                    geoService.Dispose();

                    ipService = new IpWatcherService(config, hub, logger);
                    geoService = new GeoLeakMonitor(config, hub, logger, new ProcessResolver(), activityStore);

                    ipService.StartAsync().GetAwaiter().GetResult();

                    if (config.HunterMode_AggressivePolling)
                    {
                        geoService.StartAsync().GetAwaiter().GetResult();
                    }
                    else
                    {
                        logger.Write("GeoLeakMonitorDisabled", new
                        {
                            Reason = "HunterMode_AggressivePolling is false"
                        });
                    }

                    tray.UpdateConfig(config);

                    tray.ShowBalloon(
                        "Settings Saved",
                        $"Target country: {config.TargetCountryCode}",
                        ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    logger.Write("SettingsError", new
                    {
                        Error = ex.ToString()
                    });

                    tray.ShowBalloon(
                        "Settings Error",
                        ex.Message,
                        ToolTipIcon.Error);
                }
            };

            tray.OnRefreshRequested = () =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ipService.TriggerPollAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.Write("ManualRefreshError", new
                        {
                            Error = ex.ToString()
                        });
                    }
                });
            };

            var shutdownStarted = false;

            tray.OnExitRequested = () =>
            {
                if (shutdownStarted)
                    return;

                shutdownStarted = true;

                try
                {
                    logger.Write("ExitRequested", new { });

                    ipService.StopAsync().GetAwaiter().GetResult();
                    geoService.StopAsync().GetAwaiter().GetResult();

                    activityForm?.Dispose();
                    tray.Dispose();

                    ipService.Dispose();
                    geoService.Dispose();

                    logger.Write("ExitRequestedCompleted", new { });
                    logger.Dispose();
                }
                catch (Exception ex)
                {
                    try
                    {
                        logger.Write("ExitRequestedError", new
                        {
                            Error = ex.ToString()
                        });
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    Application.ExitThread();
                }
            };

            logger.Write("Startup", new
            {
                Version = version,
                AppConfiguration.ConfigDirectory,
                AppConfiguration.ConfigFilePath,
                config.TargetCountryCode,
                config.RefreshIntervalMinutes
            });

            ipService.StartAsync().GetAwaiter().GetResult();

            if (config.HunterMode_AggressivePolling)
            {
                geoService.StartAsync().GetAwaiter().GetResult();
            }
            else
            {
                logger.Write("GeoLeakMonitorDisabled", new
                {
                    Reason = "HunterMode_AggressivePolling is false"
                });
            }

            Application.ApplicationExit += (_, _) =>
            {
                if (shutdownStarted)
                    return;

                try
                {
                    logger.Write("ApplicationExit", new { });

                    ipService.StopAsync().GetAwaiter().GetResult();
                    geoService.StopAsync().GetAwaiter().GetResult();

                    activityForm?.Dispose();
                    tray.Dispose();

                    ipService.Dispose();
                    geoService.Dispose();

                    logger.Write("ApplicationExitCompleted", new { });
                    logger.Dispose();
                }
                catch (Exception ex)
                {
                    try
                    {
                        logger.Write("ShutdownError", new
                        {
                            Error = ex.ToString()
                        });
                    }
                    catch
                    {
                    }
                }
            };

            Application.Run(new ApplicationContext());
        }
        catch (Exception ex)
        {
            _logger?.Write("CriticalStartupError", new
            {
                Error = ex.ToString()
            });

            MessageBox.Show(
                ex.ToString(),
                "IPWatcherPro Crash",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}